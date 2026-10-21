using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Configurable PAD rules end to end: booked days shared between projects,
/// unbooked services consuming PAD, lump-sum PAD invoicing (allocation incl.
/// expiry, billed once across supplements), separately identified Additional
/// PAD, and the working-time start rule. Numbers follow a lump-sum PAD
/// contract's worked examples.
/// </summary>
public class PadRulesTests : IClassFixture<ContractFlowWebApplicationFactory>
{
    private static readonly DateOnly Now = new(2031, 1, 15);
    private readonly ContractFlowWebApplicationFactory factory;

    public PadRulesTests(ContractFlowWebApplicationFactory factory)
    {
        this.factory = factory;
        factory.Clock.Set(Now);
    }

    public record IdRef(Guid Id);
    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    /// <summary>Contract with PAD terms (+ extra fields) and two projects.</summary>
    private async Task<(Scaffold.Ids Ids, Guid SecondProject)> TwoProjectsAsync(string tag, object? extraTerms = null)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        var terms = new Dictionary<string, object?>
        {
            ["effectiveFrom"] = "2026-01-01", ["currency"] = "SEK", ["holidayCalendar"] = "SE",
            ["timeBilling"] = "hourly", ["hourlyRate"] = 1000m, ["padEnabled"] = true, ["padRate"] = 1000m,
        };
        if (extraTerms is not null)
            foreach (var p in extraTerms.GetType().GetProperties()) terms[p.Name] = p.GetValue(extraTerms);
        (await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", terms)).EnsureSuccessStatusCode();
        var second = (await (await Admin.PostAsJsonAsync("/api/projects", new
        {
            contractId = ids.ContractId, name = $"Second {tag}",
        })).Content.ReadFromJsonAsync<IdRef>())!.Id;
        return (ids, second);
    }

    private Task<JsonElement> DashAsync(Guid projectId, int y, int m) =>
        Admin.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/pad/{y}/{m}");

    private static decimal Prop(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    private async Task<HttpResponseMessage> SharedBookingAsync(
        Scaffold.Ids ids, Guid second, string date, decimal primaryHours, decimal secondHours) =>
        await Admin.PostAsJsonAsync("/api/bookings", new
        {
            projectId = ids.ProjectId, resourceId = ids.ResourceId, date, status = "requested",
            shares = new[]
            {
                new { projectId = ids.ProjectId, plannedHours = primaryHours },
                new { projectId = second, plannedHours = secondHours },
            },
        });

    private async Task<Guid> ConfirmedSharedAsync(Scaffold.Ids ids, Guid second, string date, decimal a, decimal b)
    {
        var res = await SharedBookingAsync(ids, second, date, a, b);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var id = (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;
        var confirm = await Admin.PostAsync($"/api/bookings/{id}/confirm", null);
        Assert.True(confirm.IsSuccessStatusCode, await confirm.Content.ReadAsStringAsync());
        return id;
    }

    // ── Shared booked day ───────────────────────────────────────────────────

    [Fact]
    public async Task SharedDay_CommitsSplit_AndCompletionResplitsByActualTime()
    {
        var (ids, second) = await TwoProjectsAsync("share");
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 3, 5m);
        await Scaffold.SetBaselineAsync(Admin, ids with { ProjectId = second }, 2030, 3, 5m);

        // Planned 2 h + 4 h → the 1.00 minimum commits 0.33 / 0.67.
        var booking = await ConfirmedSharedAsync(ids, second, "2030-03-04", 2m, 4m);
        Assert.Equal(0.33m, Prop(await DashAsync(ids.ProjectId, 2030, 3), "committedOpen"));
        Assert.Equal(0.67m, Prop(await DashAsync(second, 2030, 3), "committedOpen"));

        var dto = await Admin.GetFromJsonAsync<JsonElement>($"/api/bookings/{booking}");
        Assert.Equal(6m, Prop(dto, "plannedHours"));
        Assert.Equal(2, dto.GetProperty("shares").GetArrayLength());
        Assert.Empty(dto.GetProperty("warnings").EnumerateArray());

        // Actual 6 h + 6 h → 1.50 PAD, 0.75 each (overrun on the first project).
        var complete = await Admin.PostAsJsonAsync($"/api/bookings/{booking}/complete", new
        {
            actualHoursByProject = new Dictionary<Guid, decimal> { [ids.ProjectId] = 6m, [second] = 6m },
        });
        Assert.True(complete.IsSuccessStatusCode, await complete.Content.ReadAsStringAsync());
        Assert.Equal(1.5m, Prop(await complete.Content.ReadFromJsonAsync<JsonElement>(), "padUtilised"));

        var a = await DashAsync(ids.ProjectId, 2030, 3);
        var b = await DashAsync(second, 2030, 3);
        Assert.Equal(0.75m, Prop(a, "utilised"));
        Assert.Equal(0.75m, Prop(b, "utilised"));
        // No commitment is left open on either project.
        Assert.True(Prop(a, "committedOpen") <= 0m);
        Assert.True(Prop(b, "committedOpen") <= 0m);
    }

    [Fact]
    public async Task SharedDay_ShorterActualOnOneProject_ReleasesItsSurplus()
    {
        var (ids, second) = await TwoProjectsAsync("share-rel");
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 3, 5m);
        await Scaffold.SetBaselineAsync(Admin, ids with { ProjectId = second }, 2030, 3, 5m);
        var booking = await ConfirmedSharedAsync(ids, second, "2030-03-05", 4m, 4m); // 0.50 / 0.50

        // Actual 2 h + 4 h → 0.33 / 0.67: the first releases 0.17, the second overruns 0.17.
        (await Admin.PostAsJsonAsync($"/api/bookings/{booking}/complete", new
        {
            actualHoursByProject = new Dictionary<Guid, decimal> { [ids.ProjectId] = 2m, [second] = 4m },
        })).EnsureSuccessStatusCode();
        Assert.Equal(0.33m, Prop(await DashAsync(ids.ProjectId, 2030, 3), "utilised"));
        Assert.Equal(0.67m, Prop(await DashAsync(second, 2030, 3), "utilised"));
    }

    [Fact]
    public async Task SharedDay_LateCancellation_ChargesTheMinimumBySharedPlan()
    {
        var (ids, second) = await TwoProjectsAsync("share-cancel");
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 6, 5m);
        await Scaffold.SetBaselineAsync(Admin, ids with { ProjectId = second }, 2030, 6, 5m);
        factory.Clock.Set(new DateOnly(2030, 6, 3));
        try
        {
            var booking = await ConfirmedSharedAsync(ids, second, "2030-06-04", 2m, 4m);
            var cancel = await Admin.PostAsync($"/api/bookings/{booking}/cancel", null);
            Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());
        }
        finally { factory.Clock.Set(Now); }

        // Late → the whole 1.00 commitment is consumed, split 0.33 / 0.67.
        Assert.Equal(0.33m, Prop(await DashAsync(ids.ProjectId, 2030, 6), "utilised"));
        Assert.Equal(0.67m, Prop(await DashAsync(second, 2030, 6), "utilised"));
    }

    [Fact]
    public async Task SharedDay_PerProjectMinimum_CommitsAFullMinimumEach()
    {
        var (ids, second) = await TwoProjectsAsync("share-per", new { sharedDayMinimum = "perProject" });
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 4, 5m);
        await Scaffold.SetBaselineAsync(Admin, ids with { ProjectId = second }, 2030, 4, 5m);
        await ConfirmedSharedAsync(ids, second, "2030-04-02", 2m, 4m);
        Assert.Equal(1m, Prop(await DashAsync(ids.ProjectId, 2030, 4), "committedOpen"));
        Assert.Equal(1m, Prop(await DashAsync(second, 2030, 4), "committedOpen"));
    }

    [Fact]
    public async Task SharedDay_ProjectsOfDifferentContracts_BadRequest()
    {
        var (ids, _) = await TwoProjectsAsync("share-x");
        var other = await Scaffold.CommercialGraphAsync(Admin, "share-x-other");
        var res = await SharedBookingAsync(ids, other.ProjectId, "2030-04-03", 2m, 4m);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("sharesDifferentContracts",
            (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task SharedDay_OneProjectShort_NamesItsShortfall()
    {
        var (ids, second) = await TwoProjectsAsync("share-short");
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 5, 5m); // second project: no PAD
        var res = await SharedBookingAsync(ids, second, "2030-05-06", 2m, 4m);
        var id = (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;

        var confirm = await Admin.PostAsync($"/api/bookings/{id}/confirm", null);
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        var body = await confirm.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("insufficientPad", body.GetProperty("error").GetString());
        var shortfall = Assert.Single(body.GetProperty("shortfalls").EnumerateArray());
        Assert.Equal(second, shortfall.GetProperty("projectId").GetGuid());
        Assert.Equal(0.67m, Prop(shortfall, "shortfallPad"));
        // Nothing was committed on either project.
        Assert.Equal(0m, Prop(await DashAsync(ids.ProjectId, 2030, 5), "committedOpen"));
    }

    // ── Unbooked services consume PAD ───────────────────────────────────────

    private async Task<Guid> ApprovedUnbookedAsync(Scaffold.Ids ids, string date, params (decimal Hours, string Billing)[] rows)
    {
        foreach (var (hours, billing) in rows)
            (await Admin.PostAsJsonAsync("/api/time-entries", new
            {
                resourceId = ids.ResourceId, projectId = ids.ProjectId, date, category = "work",
                actualHours = hours, billing,
            })).EnsureSuccessStatusCode();
        var d = DateOnly.Parse(date, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
        var submit = await Admin.PostAsJsonAsync("/api/timesheets/submit", new
        {
            resourceId = ids.ResourceId, year = ISOWeek.GetYear(d), isoWeek = ISOWeek.GetWeekOfYear(d),
        });
        Assert.True(submit.IsSuccessStatusCode, await submit.Content.ReadAsStringAsync());
        var sheet = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sheet").GetProperty("id").GetGuid();
        var approve = await Admin.PostAsync($"/api/timesheets/{sheet}/approve", null);
        Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
        return sheet;
    }

    [Fact]
    public async Task UnbookedTime_ConsumesPad_AndNeverAlsoBillsAsHours()
    {
        var (ids, _) = await TwoProjectsAsync("unbooked", new { unbookedTimeConsumesPad = true });
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 7, 2m);
        // A 2 h review consumes 0.25 PAD; a 1 h call forced to plain hours bills as hours.
        await ApprovedUnbookedAsync(ids, "2030-07-03", (2m, "contract"), (1m, "hours"));

        var dash = await DashAsync(ids.ProjectId, 2030, 7);
        Assert.Equal(0.25m, Prop(dash, "utilised"));

        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate", new { projectId = ids.ProjectId, year = 2030, month = 7 });
        Assert.True(gen.IsSuccessStatusCode, await gen.Content.ReadAsStringAsync());
        var spec = await Admin.GetFromJsonAsync<JsonElement>(
            $"/api/invoices/{(await gen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()}");
        var lines = spec.GetProperty("lines").EnumerateArray().ToList();
        var pad = Assert.Single(lines, l => l.GetProperty("kind").GetString() == "pad");
        Assert.Equal(0.25m, Prop(pad, "quantity"));
        var hours = Assert.Single(lines, l => l.GetProperty("kind").GetString() == "hours");
        Assert.Equal(1m, Prop(hours, "quantity"));
    }

    [Fact]
    public async Task UnbookedTime_BeyondFreePad_NeedsApprovalBeforeClose_AndReopenReverses()
    {
        var (ids, _) = await TwoProjectsAsync("unbooked-over", new { unbookedTimeConsumesPad = true });
        var sheet = await ApprovedUnbookedAsync(ids, "2030-08-06", (4m, "contract")); // no baseline

        var txs = (await DashAsync(ids.ProjectId, 2030, 8)).GetProperty("transactions").EnumerateArray().ToList();
        Assert.Contains(txs, t => t.GetProperty("type").GetString() == "autoAdditional" && Prop(t, "amount") == 0.5m);
        var preview = await Admin.GetFromJsonAsync<JsonElement>($"/api/projects/{ids.ProjectId}/pad/2030/8/close/preview");
        Assert.Contains(preview.GetProperty("checklist").EnumerateArray(),
            i => i.GetProperty("code").GetString() == "additionalPadAwaitingApproval" && i.GetProperty("blocking").GetBoolean());

        // An approved Additional PAD request covering it clears the item.
        var req = await (await Admin.PostAsJsonAsync("/api/pad-requests", new
        {
            projectId = ids.ProjectId, year = 2030, month = 8, quantity = 0.5m, reason = "review work",
        })).Content.ReadFromJsonAsync<IdRef>();
        (await Admin.PostAsync($"/api/pad-requests/{req!.Id}/approve", null)).EnsureSuccessStatusCode();
        preview = await Admin.GetFromJsonAsync<JsonElement>($"/api/projects/{ids.ProjectId}/pad/2030/8/close/preview");
        Assert.DoesNotContain(preview.GetProperty("checklist").EnumerateArray(),
            i => i.GetProperty("code").GetString() == "additionalPadAwaitingApproval");

        // Reopening the week gives the PAD back — append-only, net zero.
        var reopen = await Admin.PostAsJsonAsync($"/api/timesheets/{sheet}/reopen", new { note = "wrong project" });
        Assert.True(reopen.IsSuccessStatusCode, await reopen.Content.ReadAsStringAsync());
        Assert.Equal(0m, Prop(await DashAsync(ids.ProjectId, 2030, 8), "utilised"));
    }

    [Fact]
    public async Task UnbookedTime_OffByDefault_BillsAsHours()
    {
        var (ids, _) = await TwoProjectsAsync("unbooked-off");
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 9, 2m);
        await ApprovedUnbookedAsync(ids, "2030-09-03", (2m, "contract"));
        Assert.Equal(0m, Prop(await DashAsync(ids.ProjectId, 2030, 9), "utilised"));
    }

    // ── Lump-sum PAD invoicing ──────────────────────────────────────────────

    private Task SeedLedgerAsync(Guid projectId, int y, int m, PadTransactionType type, decimal amount) =>
        factory.SeedAsync(async db =>
        {
            db.PadTransactions.Add(new PadTransaction
            {
                ProjectId = projectId, Year = y, Month = m, Type = type, Amount = amount, Note = "test",
            });
            await db.SaveChangesAsync();
        });

    private async Task<JsonElement> InvoiceAsync(Guid projectId, int y, int m, bool supplement = false)
    {
        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate", new { projectId, year = y, month = m, supplement });
        Assert.True(gen.IsSuccessStatusCode, await gen.Content.ReadAsStringAsync());
        return await Admin.GetFromJsonAsync<JsonElement>(
            $"/api/invoices/{(await gen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()}");
    }

    private static decimal PadQuantity(JsonElement spec, string kind = "pad") =>
        spec.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("kind").GetString() == kind).Sum(l => Prop(l, "quantity"));

    [Fact]
    public async Task AllocationConsumed_BillsCarryInPlusBaselineMinusCarryOut_EachPadOnce()
    {
        var (ids, _) = await TwoProjectsAsync("lump", new { padInvoicing = "allocationConsumed" });
        var p = ids.ProjectId;

        // Month 1: baseline 10, 7 utilised → 1 carries, 2 expire → bills 10 − 1 = 9.
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 1, 10m);
        await SeedLedgerAsync(p, 2030, 1, PadTransactionType.Utilise, 7m);
        (await Admin.PostAsync($"/api/projects/{p}/pad/2030/1/close", null)).EnsureSuccessStatusCode();
        var jan = await InvoiceAsync(p, 2030, 1);
        Assert.Equal(9m, PadQuantity(jan));
        Assert.Equal(9000m, jan.GetProperty("invoice").GetProperty("totalAmount").GetDecimal());

        // Month 2: zero baseline, the carried 1 unused → expires → bills 1.
        (await Admin.PostAsync($"/api/projects/{p}/pad/2030/2/close", null)).EnsureSuccessStatusCode();
        Assert.Equal(1m, PadQuantity(await InvoiceAsync(p, 2030, 2)));

        // Over both months exactly the 10 allocated PAD were billed.
        // Month 3 open: utilised so far bills, with a warning that expiry is unknown.
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 3, 5m);
        await SeedLedgerAsync(p, 2030, 3, PadTransactionType.Utilise, 2m);
        var mar = await InvoiceAsync(p, 2030, 3);
        Assert.Equal(2m, PadQuantity(mar));
        Assert.Contains(mar.GetProperty("invoice").GetProperty("warnings").EnumerateArray(),
            w => w.GetString()!.Contains("not closed"));

        // Issue it, close the month (3 unused → 1 carries, 2 expire), and the
        // supplement bills only the newly known 2 — never the first 2 again.
        var marId = mar.GetProperty("invoice").GetProperty("id").GetGuid();
        foreach (var step in new[] { "review", "finalize", "issue" })
            (await Admin.PostAsync($"/api/invoices/{marId}/{step}", null)).EnsureSuccessStatusCode();
        (await Admin.PostAsync($"/api/projects/{p}/pad/2030/3/close", null)).EnsureSuccessStatusCode();
        var supplement = await InvoiceAsync(p, 2030, 3, supplement: true);
        Assert.Equal(2m, PadQuantity(supplement));
    }

    [Fact]
    public async Task UtilisedMode_ExpiryIsNotBilled()
    {
        var (ids, _) = await TwoProjectsAsync("utilised");
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 1, 10m);
        await SeedLedgerAsync(ids.ProjectId, 2030, 1, PadTransactionType.Utilise, 7m);
        (await Admin.PostAsync($"/api/projects/{ids.ProjectId}/pad/2030/1/close", null)).EnsureSuccessStatusCode();
        Assert.Equal(7m, PadQuantity(await InvoiceAsync(ids.ProjectId, 2030, 1)));
    }

    [Fact]
    public async Task AutoAdditional_BillsOnItsOwnLine_AtTheAdditionalRate()
    {
        var (ids, _) = await TwoProjectsAsync("auto", new { additionalPadRate = 1500m });
        await Scaffold.SetBaselineAsync(Admin, ids, 2030, 10, 2m);
        await SeedLedgerAsync(ids.ProjectId, 2030, 10, PadTransactionType.AutoAdditional, 0.5m);
        await SeedLedgerAsync(ids.ProjectId, 2030, 10, PadTransactionType.Utilise, 2.5m);

        var spec = await InvoiceAsync(ids.ProjectId, 2030, 10);
        Assert.Equal(2m, PadQuantity(spec));
        var auto = Assert.Single(spec.GetProperty("lines").EnumerateArray(),
            l => l.GetProperty("kind").GetString() == "autoAdditionalPad");
        Assert.Equal(0.5m, Prop(auto, "quantity"));
        Assert.Equal(1500m, Prop(auto, "unitPrice"));
    }

    // ── Working-time start ──────────────────────────────────────────────────

    [Fact]
    public async Task StartTime_LockedAtNoticeDeadline_LateChangeNeverPostpones()
    {
        var (ids, _) = await TwoProjectsAsync("start");
        factory.Clock.Set(new DateOnly(2030, 11, 1));
        try
        {
            var created = await Admin.PostAsJsonAsync("/api/bookings", new
            {
                projectId = ids.ProjectId, resourceId = ids.ResourceId, date = "2030-11-20",
                status = "requested", startTime = "09:00:00",
            });
            created.EnsureSuccessStatusCode();
            var id = (await created.Content.ReadFromJsonAsync<IdRef>())!.Id;
            Assert.Equal("09:00:00", (await Admin.GetFromJsonAsync<JsonElement>($"/api/bookings/{id}"))
                .GetProperty("effectiveStartTime").GetString());

            // The day before: moving the start later does not postpone working time…
            factory.Clock.Set(new DateOnly(2030, 11, 19));
            (await Admin.PutAsJsonAsync($"/api/bookings/{id}", new { startTime = "11:00:00" })).EnsureSuccessStatusCode();
            var late = await Admin.GetFromJsonAsync<JsonElement>($"/api/bookings/{id}");
            Assert.Equal("11:00:00", late.GetProperty("startTime").GetString());
            Assert.Equal("09:00:00", late.GetProperty("effectiveStartTime").GetString());

            // …but bringing it forward applies.
            (await Admin.PutAsJsonAsync($"/api/bookings/{id}", new { startTime = "07:00:00" })).EnsureSuccessStatusCode();
            Assert.Equal("07:00:00", (await Admin.GetFromJsonAsync<JsonElement>($"/api/bookings/{id}"))
                .GetProperty("effectiveStartTime").GetString());

            // No start agreed by the deadline → the terms default (08:00).
            var plain = await Scaffold.CreateBookingAsync(Admin, ids, "2030-11-21");
            Assert.Equal("08:00:00", (await Admin.GetFromJsonAsync<JsonElement>($"/api/bookings/{plain.Id}"))
                .GetProperty("effectiveStartTime").GetString());

            await factory.SeedAsync(async db =>
                Assert.Equal(3, await db.BookingStartTimeChanges.CountAsync(c => c.BookingId == id)));
        }
        finally { factory.Clock.Set(Now); }
    }
}
