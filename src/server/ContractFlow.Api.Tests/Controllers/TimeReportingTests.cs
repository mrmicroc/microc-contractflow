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
/// Time reporting → timesheet approval → mobilisation grouping (Phase 2).
/// The test factory shares one database per class, so all resource-role calls
/// resolve to one canonical linked resource (<see cref="LinkedId"/>) — tests
/// isolate themselves with distinct weeks/date ranges.
/// </summary>
public class TimeReportingTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    /// <summary>The single resource the dev Resource user is linked to.</summary>
    public static readonly Guid LinkedId =
        Guid.Parse("00000000-0000-4000-8000-0000000000aa");

    public record EntryDto(Guid Id, string Date, decimal ActualHours,
        decimal? PayableHours, decimal? BillableHours, decimal? MinimumAdjustmentHours,
        Guid? TimesheetId);
    public record SheetDto(Guid Id, string Status, int Year, int IsoWeek,
        int EntryCount, decimal TotalActualHours);
    public record IdRef(Guid Id);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);
    private HttpClient Finance => factory.CreateUserClient(DevSeed.FinanceId);

    /// <summary>Commercial graph + the canonical user-linked resource.</summary>
    private async Task<Scaffold.Ids> LinkedGraphAsync(string tag)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        await factory.SeedAsync(async db =>
        {
            if (!await db.Resources.AnyAsync(r => r.Id == LinkedId))
            {
                db.Resources.Add(new Resource
                {
                    Id = LinkedId,
                    DisplayName = "Linked Tester",
                    Type = ResourceType.Subcontractor,
                    UserId = DevSeed.ResourceId,
                });
                await db.SaveChangesAsync();
            }
        });
        return ids;
    }

    private static (int Year, int Week) IsoOf(DateOnly d) =>
        (ISOWeek.GetYear(d.ToDateTime(TimeOnly.MinValue)),
         ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue)));

    private async Task<IdRef> PostEntryAsync(
        HttpClient client, Guid projectId, string date, decimal hours,
        string category = "work", Guid? resourceId = null)
    {
        var res = await client.PostAsJsonAsync("/api/time-entries", new
        {
            resourceId, projectId, date, category, actualHours = hours,
            description = "test",
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<IdRef>())!;
    }

    // ── Time entries ────────────────────────────────────────────────────────

    [Fact]
    public async Task Resource_ReportsOwnEntry_EditDelete()
    {
        var ids = await LinkedGraphAsync("crud");
        var entry = await PostEntryAsync(Consultant, ids.ProjectId, "2027-09-06", 6m);

        var listed = await Consultant.GetFromJsonAsync<EntryDto[]>(
            "/api/time-entries?from=2027-09-06&to=2027-09-06");
        var mine = Assert.Single(listed!);
        Assert.Equal(entry.Id, mine.Id);
        Assert.Null(mine.PayableHours); // not yet approved

        var updated = await Consultant.PutAsJsonAsync($"/api/time-entries/{entry.Id}", new
        {
            projectId = ids.ProjectId, date = "2027-09-06", actualHours = 7.5m,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var del = await Consultant.DeleteAsync($"/api/time-entries/{entry.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Resource_CannotReportForAnotherResource()
    {
        var ids = await LinkedGraphAsync("other");
        var res = await Consultant.PostAsJsonAsync("/api/time-entries", new
        {
            resourceId = ids.ResourceId, // the scaffold resource — not theirs
            projectId = ids.ProjectId, date = "2027-09-07", actualHours = 8m,
        });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Finance_CannotWrite_CanRead()
    {
        var ids = await LinkedGraphAsync("fin");
        await PostEntryAsync(Consultant, ids.ProjectId, "2027-09-08", 8m);

        var write = await Finance.PostAsJsonAsync("/api/time-entries", new
        {
            resourceId = LinkedId, projectId = ids.ProjectId,
            date = "2027-09-08", actualHours = 8m,
        });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);

        var read = await Finance.GetAsync("/api/time-entries");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task List_ResourceSeesOnlyOwnEntries()
    {
        var ids = await LinkedGraphAsync("scope");
        await PostEntryAsync(Consultant, ids.ProjectId, "2027-09-09", 4m);
        await PostEntryAsync(Admin, ids.ProjectId, "2027-09-09", 8m,
            resourceId: ids.ResourceId); // admin reports for someone else

        var mine = await Consultant.GetFromJsonAsync<EntryDto[]>(
            "/api/time-entries?from=2027-09-09&to=2027-09-09");
        var mineRow = Assert.Single(mine!);
        Assert.Equal(4m, mineRow.ActualHours);
        var all = await Admin.GetFromJsonAsync<EntryDto[]>(
            "/api/time-entries?from=2027-09-09&to=2027-09-09");
        Assert.True(all!.Length >= 2);
    }

    [Fact]
    public async Task Entry_InClosedMonth_IsRejected()
    {
        var ids = await LinkedGraphAsync("closed");
        await Scaffold.SetBaselineAsync(Admin, ids, 2027, 9, 2m);
        var close = await Admin.PostAsync(
            $"/api/projects/{ids.ProjectId}/pad/2027/9/close", null);
        close.EnsureSuccessStatusCode();

        var res = await Consultant.PostAsJsonAsync("/api/time-entries", new
        {
            projectId = ids.ProjectId, date = "2027-09-15", actualHours = 8m,
        });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    // ── Timesheet flow ──────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_Approve_FreezesPayableAndBillableWithMinSplit()
    {
        var ids = await LinkedGraphAsync("calc");
        var project2 = await (await Admin.PostAsJsonAsync("/api/projects", new
        {
            contractId = ids.ContractId, name = "Second Project",
        })).Content.ReadFromJsonAsync<IdRef>();
        // Contract-scoped agreement: payable min 8 h/day at 10/h (beats any
        // default-scope rows). Terms carry no billable minimum.
        await Admin.PostAsJsonAsync($"/api/resources/{LinkedId}/agreements", new
        {
            contractId = ids.ContractId, effectiveFrom = "2026-01-01",
            currency = "SEK", hourlyRate = 10m, minPayableHoursPerDay = 8m,
        });

        var monday = new DateOnly(2027, 10, 11);
        var e1 = await PostEntryAsync(Consultant, ids.ProjectId, "2027-10-11", 2m);
        var e2 = await PostEntryAsync(Consultant, project2!.Id, "2027-10-11", 4m);

        var (year, week) = IsoOf(monday);
        var submit = await Consultant.PostAsJsonAsync("/api/timesheets/submit",
            new { year, isoWeek = week });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        var sheets = await Admin.GetFromJsonAsync<SheetDto[]>("/api/timesheets");
        var sheet = Assert.Single(sheets!, t => t.Year == year && t.IsoWeek == week);
        Assert.Equal("submitted", sheet.Status);
        Assert.Equal(2, sheet.EntryCount);

        // Locked while submitted — edits bounce.
        var lockedPut = await Consultant.PutAsJsonAsync($"/api/time-entries/{e1.Id}", new
        {
            projectId = ids.ProjectId, date = "2027-10-11", actualHours = 5m,
        });
        Assert.Equal(HttpStatusCode.Conflict, lockedPut.StatusCode);

        var approve = await Admin.PostAsync($"/api/timesheets/{sheet.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var detail = await Admin.GetFromJsonAsync<JsonElement>($"/api/timesheets/{sheet.Id}");
        var entries = detail.GetProperty("entries").EnumerateArray().ToList();
        var d1 = entries.First(e => e.GetProperty("id").GetGuid() == e1.Id);
        var d2 = entries.First(e => e.GetProperty("id").GetGuid() == e2.Id);

        // Spec worked example: 6 h split 2/4 with an 8 h payable minimum →
        // 2.67 + 5.33 payable; billable stays actual (no minimum in terms).
        Assert.Equal(2.67m, d1.GetProperty("payableHours").GetDecimal());
        Assert.Equal(5.33m, d2.GetProperty("payableHours").GetDecimal());
        Assert.Equal(0.67m, d1.GetProperty("minimumAdjustmentHours").GetDecimal());
        Assert.Equal(2m, d1.GetProperty("billableHours").GetDecimal());
        Assert.Equal(4m, d2.GetProperty("billableHours").GetDecimal());
        Assert.Equal("approved", detail.GetProperty("sheet").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Return_ReleasesRows_AndResubmitReusesSheet()
    {
        var ids = await LinkedGraphAsync("return");
        var e = await PostEntryAsync(Consultant, ids.ProjectId, "2027-11-15", 8m);
        var (year, week) = IsoOf(new DateOnly(2027, 11, 15));
        await Consultant.PostAsJsonAsync("/api/timesheets/submit", new { year, isoWeek = week });
        var sheets0 = await Admin.GetFromJsonAsync<SheetDto[]>("/api/timesheets");
        var sheet = Assert.Single(sheets0!, t => t.Year == year && t.IsoWeek == week);

        var back = await Admin.PostAsJsonAsync(
            $"/api/timesheets/{sheet.Id}/return", new { note = "fix the hours" });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);

        // Row is a draft again — editable, no frozen quantities.
        var entry = await Admin.GetFromJsonAsync<EntryDto>($"/api/time-entries/{e.Id}");
        Assert.Null(entry!.TimesheetId);
        Assert.Null(entry.PayableHours);
        var edit = await Consultant.PutAsJsonAsync($"/api/time-entries/{e.Id}", new
        {
            projectId = ids.ProjectId, date = "2027-11-15", actualHours = 9m,
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        // Resubmission reuses the sheet row (unique resource+week).
        var resubmit = await Consultant.PostAsJsonAsync("/api/timesheets/submit",
            new { year, isoWeek = week });
        Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);
        var sheets = await Admin.GetFromJsonAsync<SheetDto[]>("/api/timesheets");
        Assert.Single(sheets!, t => t.Year == year && t.IsoWeek == week);
    }

    [Fact]
    public async Task Resource_CannotApprove_OwnOrOthers()
    {
        var ids = await LinkedGraphAsync("noapprove");
        await PostEntryAsync(Consultant, ids.ProjectId, "2027-12-13", 8m);
        var (year, week) = IsoOf(new DateOnly(2027, 12, 13));
        await Consultant.PostAsJsonAsync("/api/timesheets/submit", new { year, isoWeek = week });
        var sheets = await Admin.GetFromJsonAsync<SheetDto[]>("/api/timesheets");
        var sheet = Assert.Single(sheets!, t => t.Year == year && t.IsoWeek == week);

        var res = await Consultant.PostAsync($"/api/timesheets/{sheet.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Submit_EmptyWeek_BadRequest()
    {
        await LinkedGraphAsync("empty");
        var res = await Consultant.PostAsJsonAsync("/api/timesheets/submit",
            new { year = 2028, isoWeek = 30 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── Travel / expenses / allowances ──────────────────────────────────────

    [Fact]
    public async Task Travel_Crud_AndScopedRead()
    {
        var ids = await LinkedGraphAsync("travel");
        var res = await Consultant.PostAsJsonAsync("/api/travel-records", new
        {
            projectId = ids.ProjectId, date = "2027-09-20", mode = "car",
            fromPlace = "Home", toPlace = "Site", distanceKm = 42.5m, durationHours = 0.75m,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;

        var list = await Consultant.GetFromJsonAsync<JsonElement[]>(
            "/api/travel-records?from=2027-09-20&to=2027-09-20");
        var row = Assert.Single(list!);
        Assert.Equal(42.5m, row.GetProperty("distanceKm").GetDecimal());

        var del = await Consultant.DeleteAsync($"/api/travel-records/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Expense_IncludedInRate_RejectsReimburseFlags()
    {
        var ids = await LinkedGraphAsync("expflags");
        var res = await Consultant.PostAsJsonAsync("/api/expenses", new
        {
            projectId = ids.ProjectId, date = "2027-09-21", amount = 100m,
            currency = "sek", includedInRate = true, reimbursableToResource = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var ok = await Consultant.PostAsJsonAsync("/api/expenses", new
        {
            projectId = ids.ProjectId, date = "2027-09-21", amount = 1200m,
            currency = "sek", reimbursableToResource = true, rechargeableToCustomer = true,
            category = "material", description = "cable",
        });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var dto = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SEK", dto.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Allowance_DuplicateDay_Conflicts()
    {
        var ids = await LinkedGraphAsync("perdiem");
        var first = await Consultant.PostAsJsonAsync("/api/allowances", new
        {
            projectId = ids.ProjectId, date = "2027-09-22", amount = 5m, currency = "SEK",
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var dupe = await Consultant.PostAsJsonAsync("/api/allowances", new
        {
            projectId = ids.ProjectId, date = "2027-09-22", amount = 5m, currency = "SEK",
        });
        Assert.Equal(HttpStatusCode.Conflict, dupe.StatusCode);
    }

    // ── Mobilisation periods ────────────────────────────────────────────────

    [Fact]
    public async Task Mobilisation_SuggestConfirmSplit()
    {
        var ids = await LinkedGraphAsync("mob");
        var ownIds = ids with { ResourceId = LinkedId };
        // Two Mon–Fri attendance weeks via bookings → two suggested periods.
        foreach (var d in new[] { 8, 9, 10, 11, 12, 15, 16, 17, 18, 19 })
        {
            var res = await Admin.PostAsJsonAsync("/api/bookings", new
            {
                projectId = ownIds.ProjectId, resourceId = ownIds.ResourceId,
                date = $"2026-11-{d:00}", status = "requested",
            });
            Assert.True(res.IsSuccessStatusCode,
                $"booking {d}: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        }

        var suggest = await Admin.GetFromJsonAsync<JsonElement[]>(
            $"/api/mobilisations/suggest?resourceId={LinkedId}&from=2026-11-01&to=2026-11-30");
        Assert.Equal(2, suggest!.Length);
        Assert.Equal("2026-11-08", suggest[0].GetProperty("startDate").GetString());
        Assert.Equal("2026-11-12", suggest[0].GetProperty("endDate").GetString());

        // Travel inside the range links when the period is confirmed.
        var travel = await Consultant.PostAsJsonAsync("/api/travel-records", new
        {
            projectId = ids.ProjectId, date = "2026-11-08", mode = "car",
            distanceKm = 100m,
        });
        var travelId = (await travel.Content.ReadFromJsonAsync<IdRef>())!.Id;

        var confirm = await Admin.PostAsJsonAsync("/api/mobilisations", new
        {
            projectId = ids.ProjectId, resourceId = LinkedId,
            startDate = "2026-11-08", endDate = "2026-11-12",
        });
        Assert.True(confirm.StatusCode == HttpStatusCode.Created,
            $"confirm: {(int)confirm.StatusCode} {await confirm.Content.ReadAsStringAsync()}");
        var periodId = (await confirm.Content.ReadFromJsonAsync<IdRef>())!.Id;

        var linked = await Admin.GetFromJsonAsync<JsonElement[]>(
            "/api/travel-records?from=2026-11-08&to=2026-11-08");
        Assert.Equal(periodId, linked![0].GetProperty("mobilisationPeriodId").GetGuid());

        // Confirmed range no longer suggested; week 2 still is.
        var again = await Admin.GetFromJsonAsync<JsonElement[]>(
            $"/api/mobilisations/suggest?resourceId={LinkedId}&from=2026-11-01&to=2026-11-30");
        var againItem = Assert.Single(again!);
        Assert.Equal("2026-11-15", againItem.GetProperty("startDate").GetString());

        var split = await Admin.PostAsJsonAsync(
            $"/api/mobilisations/{periodId}/split", new { fromDate = "2026-11-10" });
        Assert.Equal(HttpStatusCode.OK, split.StatusCode);
        var body = await split.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-11-09", body.GetProperty("first").GetProperty("endDate").GetString());
        Assert.Equal("2026-11-10", body.GetProperty("second").GetProperty("startDate").GetString());
    }

    [Fact]
    public async Task Mobilisation_Confirm_AsResource_IsForbidden()
    {
        var ids = await LinkedGraphAsync("mobforbid");
        var res = await Consultant.PostAsJsonAsync("/api/mobilisations", new
        {
            projectId = ids.ProjectId, resourceId = LinkedId,
            startDate = "2027-01-11", endDate = "2027-01-15",
        });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
