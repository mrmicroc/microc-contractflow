using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Contract-structure rules beyond PAD arithmetic, all configured per
/// contract: packages outside the contract scope, included visits, late
/// cancellation on hourly / day-rate contracts, one-off scheduled charges,
/// the baseline schedule and authorised representatives.
/// </summary>
public class ContractStructureTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record IdRef(Guid Id);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Manager => factory.CreateUserClient(DevSeed.ManagerId);
    private HttpClient Finance => factory.CreateUserClient(DevSeed.FinanceId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    private async Task<Scaffold.Ids> GraphAsync(string tag, object terms)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        var res = await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", terms);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return ids;
    }

    private async Task<Guid> PostAsync(string url, object body, HttpClient? client = null)
    {
        var res = await (client ?? Admin).PostAsJsonAsync(url, body);
        Assert.True(res.IsSuccessStatusCode, $"{url}: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<Guid> PackageAsync(Guid projectId, string name, string pricingMode = "contract",
        bool countsAsVisit = false) =>
        PostAsync("/api/work-packages", new { projectId, name, pricingMode, countsAsVisit });

    private Task<Guid> ResourceAsync(string name) =>
        PostAsync("/api/resources", new { displayName = name, type = "subcontractor" });

    private async Task ApproveWeekAsync(Guid resourceId, string date)
    {
        var d = DateOnly.Parse(date, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
        var submit = await Admin.PostAsJsonAsync("/api/timesheets/submit",
            new { resourceId, year = ISOWeek.GetYear(d), isoWeek = ISOWeek.GetWeekOfYear(d) });
        Assert.True(submit.IsSuccessStatusCode, await submit.Content.ReadAsStringAsync());
        var sheetId = (await submit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sheet").GetProperty("id").GetGuid();
        var approve = await Admin.PostAsync($"/api/timesheets/{sheetId}/approve", null);
        Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> InvoiceAsync(Guid projectId, int year, int month, bool supplement = false)
    {
        var id = await PostAsync("/api/invoices/generate", new { projectId, year, month, supplement });
        return await Admin.GetFromJsonAsync<JsonElement>($"/api/invoices/{id}");
    }

    private static List<JsonElement> Lines(JsonElement detail, string kind) =>
        detail.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("kind").GetString() == kind).ToList();

    private static List<string> Warnings(JsonElement detail) =>
        detail.GetProperty("invoice").GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetString()!).ToList();

    // ── Packages outside the contract scope ─────────────────────────────────

    [Fact]
    public async Task OutsideContractPackage_CommitsNoPad_AndBillsByTime()
    {
        var ids = await GraphAsync("outside", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "none", padEnabled = true, padRate = 8000m, hourlyRate = 1000m,
        });
        await Scaffold.SetBaselineAsync(Admin, ids, 2031, 4, 10m);
        var inside = await PackageAsync(ids.ProjectId, "Main site");
        var outside = await PackageAsync(ids.ProjectId, "Remote site", pricingMode: "outsideContract");

        var inBooking = await PostAsync("/api/bookings", new
        {
            projectId = ids.ProjectId, resourceId = ids.ResourceId, workPackageId = inside,
            date = "2031-04-07", plannedHours = 8m, status = "requested",
        });
        var outBooking = await PostAsync("/api/bookings", new
        {
            projectId = ids.ProjectId, resourceId = ids.ResourceId, workPackageId = outside,
            date = "2031-04-08", plannedHours = 8m, status = "requested",
        });
        foreach (var b in new[] { inBooking, outBooking })
            (await Admin.PostAsync($"/api/bookings/{b}/confirm", null)).EnsureSuccessStatusCode();

        // Only the in-scope day reserves PAD.
        var dash = await Admin.GetFromJsonAsync<JsonElement>($"/api/projects/{ids.ProjectId}/pad/2031/4");
        Assert.Equal(1m, dash.GetProperty("committedOpen").GetDecimal());

        foreach (var (booking, date) in new[] { (inBooking, "2031-04-07"), (outBooking, "2031-04-08") })
            await PostAsync("/api/time-entries", new
            {
                resourceId = ids.ResourceId, projectId = ids.ProjectId, bookingId = booking,
                date, category = "work", actualHours = 8m,
            });
        await ApproveWeekAsync(ids.ResourceId, "2031-04-07");

        // The outside day bills 8 h × 1 000 even though PAD is on and free;
        // the in-scope day rides the PAD line (none utilised yet → no line).
        var detail = await InvoiceAsync(ids.ProjectId, 2031, 4);
        var hours = Assert.Single(Lines(detail, "hours"));
        Assert.Equal("2031-04-08", hours.GetProperty("date").GetString());
        Assert.Equal(8000m, hours.GetProperty("amount").GetDecimal());
        Assert.Contains("outside the contract scope", hours.GetProperty("explanation").GetString());
    }

    // ── Included visits ─────────────────────────────────────────────────────

    [Fact]
    public async Task Visits_BeyondIncludedLimit_BillTravel_TwoInspectorsAreTwoVisits()
    {
        var ids = await GraphAsync("visits", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 1000m, includedVisits = 1,
        });
        var remote = await PackageAsync(ids.ProjectId, "Accessory plant", countsAsVisit: true);
        var second = await ResourceAsync("Second Inspector");

        foreach (var resourceId in new[] { ids.ResourceId, second })
        {
            await PostAsync("/api/travel-records", new
            {
                resourceId, projectId = ids.ProjectId, date = "2031-05-05", mode = "car",
                fromPlace = "Base", toPlace = "Plant", distanceKm = 300m, durationHours = 2m,
            });
            await PostAsync("/api/expenses", new
            {
                resourceId, projectId = ids.ProjectId, date = "2031-05-05", category = "accommodation",
                amount = 500m, currency = "SEK", reimbursableToResource = true,
                // The first inspector flags it rechargeable — the included visit still covers it.
                rechargeableToCustomer = resourceId == ids.ResourceId,
            });
        }
        // Same trip, two inspectors: confirmed in turn → visit 1 and visit 2.
        foreach (var resourceId in new[] { ids.ResourceId, second })
            await PostAsync("/api/mobilisations", new
            {
                projectId = ids.ProjectId, resourceId, workPackageId = remote,
                startDate = "2031-05-05", endDate = "2031-05-07",
            });
        await ApproveWeekAsync(ids.ResourceId, "2031-05-05");
        await ApproveWeekAsync(second, "2031-05-05");

        var counter = await Admin.GetFromJsonAsync<JsonElement>($"/api/contracts/{ids.ContractId}/visits");
        Assert.Equal(2, counter.GetProperty("used").GetInt32());
        Assert.Equal(1, counter.GetProperty("included").GetInt32());
        Assert.Equal(0, counter.GetProperty("remaining").GetInt32());

        var periods = await Admin.GetFromJsonAsync<JsonElement[]>($"/api/mobilisations?projectId={ids.ProjectId}");
        var p2 = periods!.Single(p => p.GetProperty("resourceId").GetGuid() == second);
        Assert.Equal(2, p2.GetProperty("visitNumber").GetInt32());
        Assert.True(p2.GetProperty("additionalTravel").GetBoolean());
        var p1 = periods!.Single(p => p.GetProperty("resourceId").GetGuid() == ids.ResourceId);
        Assert.False(p1.GetProperty("additionalTravel").GetBoolean());

        var detail = await InvoiceAsync(ids.ProjectId, 2031, 5);
        var travel = Assert.Single(Lines(detail, "travel"));
        Assert.Equal(2000m, travel.GetProperty("amount").GetDecimal()); // 2 h × 1 000, second inspector only
        Assert.Contains("visit 2", travel.GetProperty("explanation").GetString());
        var expense = Assert.Single(Lines(detail, "expense")); // second inspector's, unflagged but billable
        Assert.Equal(500m, expense.GetProperty("amount").GetDecimal());
        Assert.Contains(Warnings(detail), w => w.Contains("included visit"));
    }

    // ── Late cancellation on hourly / day-rate contracts ────────────────────

    private async Task<Guid> ConfirmedBookingAsync(Scaffold.Ids ids, string date, decimal hours)
    {
        var id = await PostAsync("/api/bookings", new
        {
            projectId = ids.ProjectId, resourceId = ids.ResourceId, date,
            plannedHours = hours, status = "requested",
        });
        (await Admin.PostAsync($"/api/bookings/{id}/confirm", null)).EnsureSuccessStatusCode();
        return id;
    }

    private async Task CancelOnAsync(Guid bookingId, DateOnly today)
    {
        factory.Clock.Set(today);
        try { (await Admin.PostAsync($"/api/bookings/{bookingId}/cancel", null)).EnsureSuccessStatusCode(); }
        finally { factory.Clock.Reset(); }
    }

    [Fact]
    public async Task Hourly_LateCancel_BillsTheDailyMinimum_TimelyIsFree()
    {
        var ids = await GraphAsync("cxl-hourly", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 1000m, minBillableHoursPerDay = 8m,
            cancellationDeadlineBusinessDays = 4,
        });
        var late = await ConfirmedBookingAsync(ids, "2031-06-04", 4m);
        var timely = await ConfirmedBookingAsync(ids, "2031-06-25", 4m);
        await CancelOnAsync(late, new DateOnly(2031, 6, 3));
        await CancelOnAsync(timely, new DateOnly(2031, 6, 3));

        var detail = await InvoiceAsync(ids.ProjectId, 2031, 6);
        var line = Assert.Single(Lines(detail, "cancellation"));
        Assert.Equal("2031-06-04", line.GetProperty("date").GetString());
        Assert.Equal(8m, line.GetProperty("quantity").GetDecimal()); // 4 h planned, raised to the 8 h minimum
        Assert.Equal(8000m, line.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task DayRate_LateCancel_FullOrHalfDay_LadderFraction()
    {
        var ids = await GraphAsync("cxl-daily", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "dailyRate", dayRate = 8000m, halfDayRate = 4500m, halfDayMaxHours = 4m,
            cancellationSteps = new object[]
            {
                new { minBusinessDaysBefore = 10 },                        // ≥ 10 days: free
                new { minBusinessDaysBefore = 2, chargeDayFraction = 0.5m }, // 2–9 days: half the day rate
            },
        });
        var fullDay = await ConfirmedBookingAsync(ids, "2031-09-03", 8m);
        var shortDay = await ConfirmedBookingAsync(ids, "2031-09-02", 4m); // same-day cancel
        var rung = await ConfirmedBookingAsync(ids, "2031-09-12", 8m);
        var free = await ConfirmedBookingAsync(ids, "2031-09-29", 8m);
        var today = new DateOnly(2031, 9, 2);
        foreach (var b in new[] { fullDay, shortDay, rung, free }) await CancelOnAsync(b, today);

        var detail = await InvoiceAsync(ids.ProjectId, 2031, 9);
        var lines = Lines(detail, "cancellation")
            .ToDictionary(l => l.GetProperty("date").GetString()!, l => l.GetProperty("amount").GetDecimal());
        Assert.Equal(8000m, lines["2031-09-03"]); // full day
        Assert.Equal(4500m, lines["2031-09-02"]); // planned 4 h ≤ half-day ceiling → half-day rate
        Assert.Equal(4000m, lines["2031-09-12"]); // rung: 0.5 × 8 000
        Assert.False(lines.ContainsKey("2031-09-29"));
    }

    [Fact]
    public async Task CancellationDayFraction_OutOfRange_BadRequest()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "cxl-bad");
        var res = await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2027-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "dailyRate", dayRate = 8000m,
            cancellationSteps = new[] { new { minBusinessDaysBefore = 2, chargeDayFraction = 1.5m } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── One-off scheduled charges ───────────────────────────────────────────

    [Fact]
    public async Task ScheduledCharge_DefaultProject_BilledOnce_FrozenOnceIssued()
    {
        var ids = await GraphAsync("charges", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE", timeBilling = "none",
        });
        // Alphabetically first active project is the default target ("Project charges" < "Zeta").
        var other = await PostAsync("/api/projects", new { contractId = ids.ContractId, name = "Zeta follow-up" });
        var charge = await PostAsync($"/api/contracts/{ids.ContractId}/charges",
            new { date = "2031-10-15", amount = 25000m, description = "Instalment 1" });

        var list = await Finance.GetFromJsonAsync<JsonElement[]>($"/api/contracts/{ids.ContractId}/charges");
        Assert.Equal(ids.ProjectId, Assert.Single(list!).GetProperty("billedProjectId").GetGuid());

        var spec = await InvoiceAsync(ids.ProjectId, 2031, 10);
        Assert.Equal(25000m, Assert.Single(Lines(spec, "scheduledCharge")).GetProperty("amount").GetDecimal());
        Assert.Empty(Lines(await InvoiceAsync(other, 2031, 10), "scheduledCharge"));

        var specId = spec.GetProperty("invoice").GetProperty("id").GetGuid();
        foreach (var step in new[] { "review", "finalize", "issue" })
            (await Admin.PostAsync($"/api/invoices/{specId}/{step}", null)).EnsureSuccessStatusCode();

        // Frozen: no change, no delete, and a supplement never bills it again.
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.DeleteAsync($"/api/contracts/{ids.ContractId}/charges/{charge}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.PutAsJsonAsync(
            $"/api/contracts/{ids.ContractId}/charges/{charge}",
            new { date = "2031-10-15", amount = 1m, description = "Changed" })).StatusCode);
        var supplement = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2031, month = 10, supplement = true });
        Assert.NotEqual(HttpStatusCode.OK, supplement.StatusCode); // nothing left to bill
    }

    [Fact]
    public async Task ScheduledCharges_AdminWrites_ManagerReads_ResourceForbidden()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "charges-auth");
        var body = new { date = "2031-11-01", amount = 100m, description = "Fee" };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Manager.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/charges", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/charges", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Manager.GetAsync($"/api/contracts/{ids.ContractId}/charges")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.GetAsync($"/api/contracts/{ids.ContractId}/charges")).StatusCode);

        var foreignProject = (await Scaffold.CommercialGraphAsync(Admin, "charges-foreign")).ProjectId;
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync(
            $"/api/contracts/{ids.ContractId}/charges",
            new { projectId = foreignProject, date = "2031-11-01", amount = 100m, description = "Fee" })).StatusCode);
    }

    // ── Baseline schedule ───────────────────────────────────────────────────

    [Fact]
    public async Task BaselineSchedule_SetsMonths_ChecksContractedTotal()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "schedule");
        (await Admin.PutAsJsonAsync($"/api/projects/{ids.ProjectId}", new
        {
            contractId = ids.ContractId, name = "Project schedule", contractedBaselinePad = 30m,
        })).EnsureSuccessStatusCode();

        var url = $"/api/projects/{ids.ProjectId}/pad/baseline-schedule";
        var months = new[]
        {
            new { year = 2032, month = 1, amount = 8m },
            new { year = 2032, month = 2, amount = 8m },
            new { year = 2032, month = 3, amount = 10m },
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await Manager.PutAsJsonAsync(url, months)).StatusCode);
        var put = await Admin.PutAsJsonAsync(url, months);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var schedule = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(26m, schedule.GetProperty("scheduled").GetDecimal());
        Assert.Equal(4m, schedule.GetProperty("difference").GetDecimal());
        Assert.NotNull(schedule.GetProperty("warning").GetString());

        // Re-setting a month writes a delta; the dashboard reflects it.
        (await Admin.PutAsJsonAsync(url, new[] { new { year = 2032, month = 3, amount = 14m } }))
            .EnsureSuccessStatusCode();
        var dash = await Admin.GetFromJsonAsync<JsonElement>($"/api/projects/{ids.ProjectId}/pad/2032/3");
        Assert.Equal(14m, dash.GetProperty("baseline").GetDecimal());
        var read = await Manager.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal(30m, read.GetProperty("scheduled").GetDecimal());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("warning").ValueKind);

        // Closed months refuse the whole schedule.
        await factory.SeedAsync(async db =>
        {
            db.ProjectMonthCloses.Add(new ProjectMonthClose { ProjectId = ids.ProjectId, Year = 2032, Month = 1 });
            await db.SaveChangesAsync();
        });
        var closed = await Admin.PutAsJsonAsync(url, months);
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Contains("2032-01", await closed.Content.ReadAsStringAsync());
    }

    // ── Authorised representatives & instruction evidence ───────────────────

    [Fact]
    public async Task Representatives_AndInstructionEvidence_RoundTrip()
    {
        var ids = await GraphAsync("reps", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "none", padEnabled = true, padRate = 8000m,
        });
        var contract = await Admin.GetFromJsonAsync<JsonElement>($"/api/contracts/{ids.ContractId}");
        (await Admin.PutAsJsonAsync($"/api/contracts/{ids.ContractId}", new
        {
            customerId = ids.CustomerId, name = contract.GetProperty("name").GetString(),
            startDate = "2026-01-01",
            representatives = new[] { new { name = "Alex Example", email = "alex@example.org", authority = "Bookings and Additional PAD" } },
        })).EnsureSuccessStatusCode();
        var detail = await Admin.GetFromJsonAsync<JsonElement>($"/api/contracts/{ids.ContractId}");
        var rep = Assert.Single(detail.GetProperty("representatives").EnumerateArray());
        Assert.Equal("Alex Example", rep.GetProperty("name").GetString());

        var booking = await PostAsync("/api/bookings", new
        {
            projectId = ids.ProjectId, resourceId = ids.ResourceId, date = "2031-12-01",
            status = "requested", instructedBy = "Alex Example", instructedAtUtc = "2031-11-20T09:00:00Z",
        });
        var b = await Admin.GetFromJsonAsync<JsonElement>($"/api/bookings/{booking}");
        Assert.Equal("Alex Example", b.GetProperty("instructedBy").GetString());

        var request = await PostAsync("/api/pad-requests", new
        {
            projectId = ids.ProjectId, year = 2031, month = 12, quantity = 1m, reason = "Extra day",
        });
        var approve = await Admin.PostAsJsonAsync($"/api/pad-requests/{request}/approve",
            new { approvedByRepresentative = "Alex Example" });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var approved = await approve.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Alex Example", approved.GetProperty("approvedByRepresentative").GetString());
    }
}
