using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Money rules end-to-end (design §5/§6): one shared per-day pipeline for
/// approval, invoice and statement; one mobilisation fee per period; the
/// supplement / Invoiced leg; currency guards; billed history protected.
/// Every test owns its graph and reports for the graph's own resource
/// (admin on behalf), so tests never share weeks.
/// </summary>
public class MoneyRulesTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record IdRef(Guid Id);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Manager => factory.CreateUserClient(DevSeed.ManagerId);
    private HttpClient Finance => factory.CreateUserClient(DevSeed.FinanceId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    private async Task<Scaffold.Ids> GraphAsync(string tag, object? terms = null)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        if (terms is not null)
            (await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", terms))
                .EnsureSuccessStatusCode();
        return ids;
    }

    private static object HourlyTerms(decimal rate = 1000m, decimal? min = null, string calendar = "SE",
        string currency = "SEK", decimal? saturdayRate = null, decimal otPercent = 150m,
        decimal? mobilisationFee = null) => new
    {
        effectiveFrom = "2026-01-01", currency, holidayCalendar = calendar,
        timeBilling = "hourly", hourlyRate = rate, minBillableHoursPerDay = min,
        saturdayRate, overtimePercent = otPercent, mobilisationFee,
    };

    private async Task<Guid> PackageAsync(Guid projectId, string name) =>
        (await (await Admin.PostAsJsonAsync("/api/work-packages", new { projectId, name }))
            .Content.ReadFromJsonAsync<IdRef>())!.Id;

    private async Task<Guid> EntryAsync(Guid resourceId, Guid projectId, string date, decimal hours,
        Guid? workPackageId = null)
    {
        var res = await Admin.PostAsJsonAsync("/api/time-entries", new
        {
            resourceId, projectId, date, category = "work", actualHours = hours, workPackageId,
        });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;
    }

    private static (int Year, int Week) IsoOf(string date)
    {
        var d = DateOnly.Parse(date, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(d), ISOWeek.GetWeekOfYear(d));
    }

    private async Task<Guid> SubmitAsync(Guid resourceId, string date)
    {
        var (year, week) = IsoOf(date);
        var submit = await Admin.PostAsJsonAsync("/api/timesheets/submit",
            new { resourceId, year, isoWeek = week });
        Assert.True(submit.IsSuccessStatusCode, await submit.Content.ReadAsStringAsync());
        return (await submit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sheet").GetProperty("id").GetGuid();
    }

    private async Task<Guid> ApproveWeekAsync(Guid resourceId, string date)
    {
        var sheetId = await SubmitAsync(resourceId, date);
        var approve = await Admin.PostAsync($"/api/timesheets/{sheetId}/approve", null);
        Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
        return sheetId;
    }

    private async Task<JsonElement> InvoiceAsync(Guid projectId, int year, int month, bool supplement = false)
    {
        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId, year, month, supplement });
        Assert.True(gen.IsSuccessStatusCode, await gen.Content.ReadAsStringAsync());
        var id = (await gen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return await Admin.GetFromJsonAsync<JsonElement>($"/api/invoices/{id}");
    }

    private async Task<JsonElement> StatementAsync(Guid resourceId, int year, int month)
    {
        var gen = await Admin.PostAsJsonAsync("/api/statements/generate", new { resourceId, year, month });
        Assert.True(gen.IsSuccessStatusCode, await gen.Content.ReadAsStringAsync());
        var id = (await gen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return await Admin.GetFromJsonAsync<JsonElement>($"/api/statements/{id}");
    }

    private static List<JsonElement> Lines(JsonElement detail, string kind) =>
        detail.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("kind").GetString() == kind).ToList();

    private static decimal Total(JsonElement detail, string prop = "invoice") =>
        detail.GetProperty(prop).GetProperty("totalAmount").GetDecimal();

    private async Task IssueAsync(JsonElement detail)
    {
        var id = detail.GetProperty("invoice").GetProperty("id").GetGuid();
        foreach (var step in new[] { "review", "finalize", "issue" })
            (await Admin.PostAsync($"/api/invoices/{id}/{step}", null)).EnsureSuccessStatusCode();
    }

    private async Task<JsonElement> EntryDetailAsync(HttpClient client, Guid sheetId, Guid entryId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/timesheets/{sheetId}"))
            .GetProperty("entries").EnumerateArray()
            .First(e => e.GetProperty("id").GetGuid() == entryId);

    // ── Shared day pipeline ─────────────────────────────────────────────────

    [Fact]
    public async Task Minimum_IsShared_AcrossPackagesOnSameDay()
    {
        // §5: multiple activities the same day share ONE minimum — 2 h + 2 h
        // on two packages with an 8 h minimum bill 8 h in total, not 16.
        var ids = await GraphAsync("min-shared", HourlyTerms(min: 8m));
        var a = await PackageAsync(ids.ProjectId, "Package A");
        var b = await PackageAsync(ids.ProjectId, "Package B");
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-01-08", 2m, a);
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-01-08", 2m, b);
        await ApproveWeekAsync(ids.ResourceId, "2029-01-08");

        var inv = await InvoiceAsync(ids.ProjectId, 2029, 1);
        var hours = Lines(inv, "hours");
        Assert.Equal(2, hours.Count);
        Assert.Equal(8m, hours.Sum(l => l.GetProperty("quantity").GetDecimal()));
        Assert.Equal(8000m, Total(inv));
    }

    [Fact]
    public async Task Overtime_IsShared_AcrossPackagesOnSameDay()
    {
        // 6 h + 6 h on two packages = a 12 h day → 4 h overtime in total.
        var ids = await GraphAsync("ot-shared", HourlyTerms());
        var a = await PackageAsync(ids.ProjectId, "Package A");
        var b = await PackageAsync(ids.ProjectId, "Package B");
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-01-15", 6m, a);
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-01-15", 6m, b);
        await ApproveWeekAsync(ids.ResourceId, "2029-01-15");

        var inv = await InvoiceAsync(ids.ProjectId, 2029, 1);
        // 8 h × 1000 + 4 h × 1000 × 150 % = 14 000.
        Assert.Equal(14000m, Total(inv));
        Assert.All(Lines(inv, "hours"), l =>
            Assert.Contains("OT", l.GetProperty("explanation").GetString()));
    }

    [Fact]
    public async Task FrozenBillable_EqualsInvoiceQuantity_OnMultiContractDay()
    {
        // Contract A carries an 8 h minimum, contract B none. The day's 3 h + 3 h
        // never mix: A bills 8 h, B bills 3 h — frozen and invoiced alike (§1.1).
        var a = await GraphAsync("multi-a", HourlyTerms(min: 8m));
        var b = await GraphAsync("multi-b", HourlyTerms());
        var resource = a.ResourceId;
        var ea = await EntryAsync(resource, a.ProjectId, "2029-02-05", 3m);
        var eb = await EntryAsync(resource, b.ProjectId, "2029-02-05", 3m);
        var sheet = await ApproveWeekAsync(resource, "2029-02-05");

        var frozenA = await EntryDetailAsync(Admin, sheet, ea);
        var frozenB = await EntryDetailAsync(Admin, sheet, eb);
        Assert.Equal(8m, frozenA.GetProperty("billableHours").GetDecimal());
        Assert.Equal(5m, frozenA.GetProperty("billableMinimumAdjustmentHours").GetDecimal());
        Assert.Equal(3m, frozenB.GetProperty("billableHours").GetDecimal());

        var invA = await InvoiceAsync(a.ProjectId, 2029, 2);
        var invB = await InvoiceAsync(b.ProjectId, 2029, 2);
        Assert.Equal(8m, Assert.Single(Lines(invA, "hours")).GetProperty("quantity").GetDecimal());
        Assert.Equal(3m, Assert.Single(Lines(invB, "hours")).GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task DayRateContract_FrozenBillable_EqualsActual()
    {
        // The minimum never applies to a fixed day price.
        var ids = await GraphAsync("day-frozen", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "dailyRate", dayRate = 8000m, minBillableHoursPerDay = 8m,
        });
        var e = await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-02-12", 5m);
        var sheet = await ApproveWeekAsync(ids.ResourceId, "2029-02-12");

        var frozen = await EntryDetailAsync(Admin, sheet, e);
        Assert.Equal(5m, frozen.GetProperty("billableHours").GetDecimal());
        Assert.Equal(0m, frozen.GetProperty("billableMinimumAdjustmentHours").GetDecimal());
        var inv = await InvoiceAsync(ids.ProjectId, 2029, 2);
        Assert.Equal(8000m, Total(inv));
    }

    [Fact]
    public async Task Payable_And_Billable_Minimums_StoredSeparately()
    {
        // Contract minimum 8 (billable), agreement minimum 6 (payable): 3 h
        // actual → payable 6 (+3), billable 8 (+5), actual untouched.
        var ids = await GraphAsync("min-split", HourlyTerms(min: 8m));
        await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 600m,
            minPayableHoursPerDay = 6m,
        });
        var e = await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-02-19", 3m);
        var sheet = await ApproveWeekAsync(ids.ResourceId, "2029-02-19");

        var frozen = await EntryDetailAsync(Admin, sheet, e);
        Assert.Equal(3m, frozen.GetProperty("actualHours").GetDecimal());
        Assert.Equal(6m, frozen.GetProperty("payableHours").GetDecimal());
        Assert.Equal(3m, frozen.GetProperty("minimumAdjustmentHours").GetDecimal());
        Assert.Equal(8m, frozen.GetProperty("billableHours").GetDecimal());
        Assert.Equal(5m, frozen.GetProperty("billableMinimumAdjustmentHours").GetDecimal());
    }

    [Fact]
    public async Task Approve_ThreeActivitiesSameProject_SharesSumToMinimum()
    {
        // Rounding remainder lands on the last entry: 2.67 + 2.67 + 2.66 = 8.
        var ids = await GraphAsync("remainder", HourlyTerms());
        await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 600m,
            minPayableHoursPerDay = 8m,
        });
        var entries = new List<Guid>();
        for (var i = 0; i < 3; i++)
            entries.Add(await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-02-26", 1m));
        var sheet = await ApproveWeekAsync(ids.ResourceId, "2029-02-26");

        var detail = await Admin.GetFromJsonAsync<JsonElement>($"/api/timesheets/{sheet}");
        var payable = detail.GetProperty("entries").EnumerateArray()
            .Sum(x => x.GetProperty("payableHours").GetDecimal());
        Assert.Equal(8m, payable);
    }

    [Fact]
    public async Task Statement_PackageAgreement_PricesOnlyItsPackage()
    {
        // 4 h on package A (package-scoped agreement 700) + 4 h on package B
        // (default agreement 600) → 2 800 + 2 400 = 5 200.
        var ids = await GraphAsync("pay-split", HourlyTerms());
        var a = await PackageAsync(ids.ProjectId, "Package A");
        var b = await PackageAsync(ids.ProjectId, "Package B");
        await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 600m,
        });
        (await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            contractId = ids.ContractId, workPackageId = a,
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 700m,
        })).EnsureSuccessStatusCode();
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-03-05", 4m, a);
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-03-05", 4m, b);
        await ApproveWeekAsync(ids.ResourceId, "2029-03-05");

        var st = await StatementAsync(ids.ResourceId, 2029, 3);
        Assert.Equal(5200m, Total(st, "statement"));
    }

    [Fact]
    public async Task Statement_UsesContractHolidayCalendar()
    {
        // 2028-08-28 is the England & Wales summer bank holiday, an ordinary
        // weekday in Sweden: on a GB-ENG contract the holiday rate pays.
        var ids = await GraphAsync("gb-cal", HourlyTerms(calendar: "GB-ENG"));
        await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 600m, holidayRate = 1200m,
        });
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2028-08-28", 8m);
        await ApproveWeekAsync(ids.ResourceId, "2028-08-28");

        var st = await StatementAsync(ids.ResourceId, 2028, 8);
        Assert.Equal(9600m, Total(st, "statement"));
    }

    // ── §5 worked examples at invoice level ─────────────────────────────────

    [Fact]
    public async Task Invoice_Saturday10h_OrdinaryEight_Plus2hOvertime()
    {
        // §5: Saturday 10 h, ordinary 8 → 8 h × Sat rate + 2 h × Sat rate × 1.75.
        var ids = await GraphAsync("sat", HourlyTerms(saturdayRate: 1500m, otPercent: 175m));
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-03-10", 10m); // Saturday
        await ApproveWeekAsync(ids.ResourceId, "2029-03-10");

        var inv = await InvoiceAsync(ids.ProjectId, 2029, 3);
        Assert.Equal(12000m + 5250m, Total(inv));
        Assert.Contains("Saturday", Assert.Single(Lines(inv, "hours")).GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task Invoice_SixHoursAcrossProjects_MinEight_Splits267And533()
    {
        // §5: 6 h across projects A + B (same contract, 8 h billable minimum)
        // → A 2.67 h / B 5.33 h on the two projects' specs.
        var ids = await GraphAsync("six-ab", HourlyTerms(min: 8m));
        var p2 = (await (await Admin.PostAsJsonAsync("/api/projects", new
        {
            contractId = ids.ContractId, name = "Second Project six-ab",
        })).Content.ReadFromJsonAsync<IdRef>())!.Id;
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-03-12", 2m);
        await EntryAsync(ids.ResourceId, p2, "2029-03-12", 4m);
        await ApproveWeekAsync(ids.ResourceId, "2029-03-12");

        var a = await InvoiceAsync(ids.ProjectId, 2029, 3);
        var b = await InvoiceAsync(p2, 2029, 3);
        Assert.Equal(2.67m, Assert.Single(Lines(a, "hours")).GetProperty("quantity").GetDecimal());
        Assert.Equal(5.33m, Assert.Single(Lines(b, "hours")).GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task Invoice_TermsVersionMidMonth_EachDayAtItsVersion()
    {
        var ids = await GraphAsync("mid-month", HourlyTerms());
        (await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2029-04-15", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 1200m,
        })).EnsureSuccessStatusCode();
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-04-09", 8m);
        await ApproveWeekAsync(ids.ResourceId, "2029-04-09");
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-04-16", 8m);
        await ApproveWeekAsync(ids.ResourceId, "2029-04-16");

        var inv = await InvoiceAsync(ids.ProjectId, 2029, 4);
        var amounts = Lines(inv, "hours").Select(l => l.GetProperty("amount").GetDecimal()).Order().ToList();
        Assert.Equal([8000m, 9600m], amounts);
    }

    [Fact]
    public async Task ExpenseFlags_DecideWhichDocumentCarriesThem()
    {
        var ids = await GraphAsync("exp-flags", HourlyTerms());
        await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 600m,
        });
        async Task Expense(decimal amount, bool reimb, bool recharge, bool incl) =>
            (await Admin.PostAsJsonAsync("/api/expenses", new
            {
                resourceId = ids.ResourceId, projectId = ids.ProjectId, date = "2029-04-23",
                amount, currency = "SEK", reimbursableToResource = reimb,
                rechargeableToCustomer = recharge, includedInRate = incl,
            })).EnsureSuccessStatusCode();
        await Expense(100m, reimb: true, recharge: false, incl: false);
        await Expense(200m, reimb: false, recharge: true, incl: false);
        await Expense(400m, reimb: false, recharge: false, incl: true);
        await ApproveWeekAsync(ids.ResourceId, "2029-04-23");

        var inv = await InvoiceAsync(ids.ProjectId, 2029, 4);
        Assert.Equal(200m, Assert.Single(Lines(inv, "expense")).GetProperty("amount").GetDecimal());
        var st = await StatementAsync(ids.ResourceId, 2029, 4);
        Assert.Equal(100m, Assert.Single(Lines(st, "expense")).GetProperty("amount").GetDecimal());

        // Regenerating the draft never recharges the same expense twice.
        var again = await InvoiceAsync(ids.ProjectId, 2029, 4);
        Assert.Single(Lines(again, "expense"));
    }

    // ── Mobilisation ────────────────────────────────────────────────────────

    [Fact]
    public async Task Mobilisation_CrossingMonthEnd_BilledOnceInStartMonth()
    {
        var ids = await GraphAsync("mob-cross", HourlyTerms(mobilisationFee: 500m));
        (await Admin.PostAsJsonAsync("/api/mobilisations", new
        {
            projectId = ids.ProjectId, resourceId = ids.ResourceId,
            startDate = "2029-05-30", endDate = "2029-06-02",
        })).EnsureSuccessStatusCode();
        // Large travel costs never change the fixed fee.
        (await Admin.PostAsJsonAsync("/api/expenses", new
        {
            resourceId = ids.ResourceId, projectId = ids.ProjectId, date = "2029-05-30",
            amount = 9000m, currency = "SEK", reimbursableToResource = true,
        })).EnsureSuccessStatusCode();
        await ApproveWeekAsync(ids.ResourceId, "2029-05-30");

        var may = await InvoiceAsync(ids.ProjectId, 2029, 5);
        var fee = Assert.Single(Lines(may, "mobilisation"));
        Assert.Equal(500m, fee.GetProperty("amount").GetDecimal());
        var june = await InvoiceAsync(ids.ProjectId, 2029, 6);
        Assert.Empty(Lines(june, "mobilisation"));
    }

    // ── Supplement + Invoiced leg ───────────────────────────────────────────

    [Fact]
    public async Task LateApproval_AfterIssue_ShowsUnbilled_AndSupplementBillsOnce()
    {
        var ids = await GraphAsync("supplement", HourlyTerms());
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-07-02", 8m);
        var sheet1 = await ApproveWeekAsync(ids.ResourceId, "2029-07-02");
        var rev0 = await InvoiceAsync(ids.ProjectId, 2029, 7);
        await IssueAsync(rev0);

        // Issued spec covers the whole first week → the sheet is Invoiced.
        var s1 = await Admin.GetFromJsonAsync<JsonElement>($"/api/timesheets/{sheet1}");
        Assert.Equal("invoiced", s1.GetProperty("sheet").GetProperty("status").GetString());

        // A later week of July is approved after the issue.
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-07-09", 4m);
        await ApproveWeekAsync(ids.ResourceId, "2029-07-09");

        var unbilled = await Admin.GetFromJsonAsync<JsonElement[]>(
            $"/api/invoices/unbilled?year=2029&month=7&projectId={ids.ProjectId}");
        var row = Assert.Single(unbilled!);
        Assert.Equal(4m, row.GetProperty("hours").GetDecimal());
        Assert.True(row.GetProperty("hasFrozenSpec").GetBoolean());

        var frozen = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2029, month = 7 });
        Assert.Equal(HttpStatusCode.Conflict, frozen.StatusCode);

        var rev1 = await InvoiceAsync(ids.ProjectId, 2029, 7, supplement: true);
        Assert.Equal(1, rev1.GetProperty("invoice").GetProperty("revision").GetInt32());
        Assert.Equal(4000m, Total(rev1)); // only the late 4 h — never the issued 8 h again
        var after = await Admin.GetFromJsonAsync<JsonElement[]>(
            $"/api/invoices/unbilled?year=2029&month=7&projectId={ids.ProjectId}");
        Assert.Empty(after!);
    }

    [Fact]
    public async Task Supplement_WithNothingNew_Conflicts()
    {
        var ids = await GraphAsync("supp-empty", HourlyTerms());
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-07-16", 8m);
        await ApproveWeekAsync(ids.ResourceId, "2029-07-16");
        await IssueAsync(await InvoiceAsync(ids.ProjectId, 2029, 7));

        var res = await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2029, month = 7, supplement = true });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("nothingToBill", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CustomerReview_CanGoBackToDraft_AndIssueStampsUser()
    {
        var ids = await GraphAsync("back-draft", HourlyTerms());
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-07-23", 8m);
        await ApproveWeekAsync(ids.ResourceId, "2029-07-23");
        var inv = await InvoiceAsync(ids.ProjectId, 2029, 7);
        var id = inv.GetProperty("invoice").GetProperty("id").GetGuid();

        (await Finance.PostAsync($"/api/invoices/{id}/review", null)).EnsureSuccessStatusCode();
        var back = await Finance.PostAsync($"/api/invoices/{id}/back-to-draft", null);
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal("draft", (await back.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("status").GetString());

        foreach (var step in new[] { "review", "finalize", "issue" })
            (await Finance.PostAsync($"/api/invoices/{id}/{step}", null)).EnsureSuccessStatusCode();
        var paid = await Finance.PostAsync($"/api/invoices/{id}/pay", null);
        var dto = await paid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DevSeed.FinanceId, dto.GetProperty("issuedByUserId").GetGuid());
        Assert.Equal(DevSeed.FinanceId, dto.GetProperty("paidByUserId").GetGuid());
    }

    [Fact]
    public async Task Invoice_Lifecycle_ManagerForbidden()
    {
        var ids = await GraphAsync("pm-inv", HourlyTerms());
        var gen = await Manager.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2029, month = 8 });
        Assert.Equal(HttpStatusCode.Forbidden, gen.StatusCode);
        var stGen = await Manager.PostAsJsonAsync("/api/statements/generate",
            new { resourceId = ids.ResourceId, year = 2029, month = 8 });
        Assert.Equal(HttpStatusCode.Forbidden, stGen.StatusCode);

        var inv = await InvoiceAsync(ids.ProjectId, 2029, 8);
        var id = inv.GetProperty("invoice").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Manager.PostAsync($"/api/invoices/{id}/review", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Manager.GetAsync($"/api/invoices/{id}")).StatusCode);
    }

    // ── Rates visibility (H5) ───────────────────────────────────────────────

    [Fact]
    public async Task Statements_And_Margins_ManagerForbidden_FinanceAllowed()
    {
        var ids = await GraphAsync("pm-rates", HourlyTerms());
        await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 600m,
        });
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-08-06", 8m);
        await ApproveWeekAsync(ids.ResourceId, "2029-08-06");
        var st = await StatementAsync(ids.ResourceId, 2029, 8);
        var stId = st.GetProperty("statement").GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await Manager.GetAsync($"/api/statements/{stId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Manager.GetAsync("/api/statements")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Manager.GetAsync("/api/reports/margins?year=2029&month=8")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Finance.GetAsync($"/api/statements/{stId}")).StatusCode);

        var margins = await Finance.GetFromJsonAsync<JsonElement[]>("/api/reports/margins?year=2029&month=8");
        var row = Assert.Single(margins!, m => m.GetProperty("projectId").GetGuid() == ids.ProjectId);
        Assert.Equal("SEK", row.GetProperty("currency").GetString());
        Assert.Equal(4800m, row.GetProperty("cost").GetDecimal());
    }

    [Fact]
    public async Task Timesheet_ResourceSeesNoBillableHours_AdminDoes()
    {
        var ids = await GraphAsync("hide-bill", HourlyTerms(min: 8m));
        var linked = TimeReportingTests.LinkedId;
        await factory.SeedAsync(async db =>
        {
            if (!db.Resources.Any(r => r.Id == linked))
            {
                db.Resources.Add(new Resource
                {
                    Id = linked, DisplayName = "Linked Tester",
                    Type = ResourceType.Subcontractor, UserId = DevSeed.ResourceId,
                });
                await db.SaveChangesAsync();
            }
        });
        var e = await EntryAsync(linked, ids.ProjectId, "2029-08-13", 3m);
        var sheet = await ApproveWeekAsync(linked, "2029-08-13");

        var mine = await EntryDetailAsync(Consultant, sheet, e);
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("billableHours").ValueKind);
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("billableMinimumAdjustmentHours").ValueKind);
        Assert.Equal(3m, mine.GetProperty("payableHours").GetDecimal());
        var admin = await EntryDetailAsync(Admin, sheet, e);
        Assert.Equal(8m, admin.GetProperty("billableHours").GetDecimal());
    }

    // ── Currency guards (H6) ────────────────────────────────────────────────

    [Fact]
    public async Task ForeignCurrencyExpense_LeftOutOfTotal_WithWarning()
    {
        var ids = await GraphAsync("fx", HourlyTerms());
        (await Admin.PostAsJsonAsync("/api/expenses", new
        {
            resourceId = ids.ResourceId, projectId = ids.ProjectId, date = "2029-08-20",
            amount = 100m, currency = "EUR", rechargeableToCustomer = true,
        })).EnsureSuccessStatusCode();
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-08-20", 8m);
        await ApproveWeekAsync(ids.ResourceId, "2029-08-20");

        var inv = await InvoiceAsync(ids.ProjectId, 2029, 8);
        Assert.Empty(Lines(inv, "expense"));
        Assert.Equal(8000m, Total(inv));
        var warnings = inv.GetProperty("invoice").GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetString()).ToList();
        Assert.Contains(warnings, w => w!.Contains("EUR"));
    }

    // ── PAD (M3) ────────────────────────────────────────────────────────────

    private async Task UtiliseAsync(Guid projectId, int year, int month, decimal baseline, decimal utilised) =>
        await factory.SeedAsync(async db =>
        {
            db.PadTransactions.Add(new PadTransaction
            {
                Id = Guid.NewGuid(), ProjectId = projectId, Year = year, Month = month,
                Type = PadTransactionType.BaselineSet, Amount = baseline, Note = "test",
            });
            db.PadTransactions.Add(new PadTransaction
            {
                Id = Guid.NewGuid(), ProjectId = projectId, Year = year, Month = month,
                Type = PadTransactionType.Utilise, Amount = utilised, Note = "test",
            });
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task Pad_EnabledWithoutRate_Warns()
    {
        var ids = await GraphAsync("pad-norate", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 1000m, padEnabled = true,
        });
        await UtiliseAsync(ids.ProjectId, 2029, 9, 5m, 2m);
        var inv = await InvoiceAsync(ids.ProjectId, 2029, 9);
        Assert.Empty(Lines(inv, "pad"));
        Assert.NotEmpty(inv.GetProperty("invoice").GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task Pad_ApprovedAdditionalWithRate_BillsOverflowAtRequestRate()
    {
        var ids = await GraphAsync("pad-addrate", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", padEnabled = true, padRate = 800m,
        });
        var req = await (await Admin.PostAsJsonAsync("/api/pad-requests", new
        {
            projectId = ids.ProjectId, year = 2029, month = 9, quantity = 2m, rate = 1000m,
            reason = "extra",
        })).Content.ReadFromJsonAsync<IdRef>();
        (await Admin.PostAsync($"/api/pad-requests/{req!.Id}/approve", null)).EnsureSuccessStatusCode();
        // Baseline 3, utilised 5 → 2 PAD above the allocation at the request rate.
        await UtiliseAsync(ids.ProjectId, 2029, 9, 3m, 5m);

        var inv = await InvoiceAsync(ids.ProjectId, 2029, 9);
        var pads = Lines(inv, "pad");
        Assert.Equal(2, pads.Count);
        Assert.Equal(3m * 800m + 2m * 1000m, Total(inv));
    }

    // ── Billed history is never rewritten (M1/M2) ───────────────────────────

    [Fact]
    public async Task Terms_UsedByIssuedSpec_CannotBeDeletedOrBackdated()
    {
        var ids = await GraphAsync("terms-guard");
        var created = await (await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms",
            HourlyTerms())).Content.ReadFromJsonAsync<IdRef>();
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-10-01", 8m);
        await ApproveWeekAsync(ids.ResourceId, "2029-10-01");
        await IssueAsync(await InvoiceAsync(ids.ProjectId, 2029, 10));

        var del = await Admin.DeleteAsync($"/api/contracts/{ids.ContractId}/terms/{created!.Id}");
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
        var backdated = await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2029-10-15", currency = "SEK", holidayCalendar = "SE", hourlyRate = 1.0m,
        });
        Assert.Equal(HttpStatusCode.Conflict, backdated.StatusCode);
        var future = await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2029-11-01", currency = "SEK", holidayCalendar = "SE", hourlyRate = 1100m,
        });
        Assert.Equal(HttpStatusCode.OK, future.StatusCode);
    }

    [Fact]
    public async Task Agreement_UsedByFinalStatement_CannotBeDeleted()
    {
        var ids = await GraphAsync("agr-guard", HourlyTerms());
        var agr = await (await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 600m,
        })).Content.ReadFromJsonAsync<IdRef>();
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-10-08", 8m);
        await ApproveWeekAsync(ids.ResourceId, "2029-10-08");
        var st = await StatementAsync(ids.ResourceId, 2029, 10);
        (await Admin.PostAsync(
            $"/api/statements/{st.GetProperty("statement").GetProperty("id").GetGuid()}/finalize", null))
            .EnsureSuccessStatusCode();

        var del = await Admin.DeleteAsync($"/api/resources/{ids.ResourceId}/agreements/{agr!.Id}");
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
    }

    [Fact]
    public async Task WorkPackage_WithRates_CannotBeDeleted_NorMoved()
    {
        var ids = await GraphAsync("wp-guard", HourlyTerms());
        var wp = await PackageAsync(ids.ProjectId, "Guarded");
        (await Admin.PostAsJsonAsync($"/api/work-packages/{wp}/rates", new
        {
            effectiveFrom = "2026-01-01", hourlyRate = 1200m,
        })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await Admin.DeleteAsync($"/api/work-packages/{wp}")).StatusCode);
        var other = await Scaffold.CommercialGraphAsync(Admin, "wp-guard-2");
        var move = await Admin.PutAsJsonAsync($"/api/work-packages/{wp}",
            new { projectId = other.ProjectId, name = "Guarded" });
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);
    }

    [Fact]
    public async Task Rates_WritePaths_ForbiddenForFinanceAndResource()
    {
        var ids = await GraphAsync("rate-authz", HourlyTerms());
        var wp = await PackageAsync(ids.ProjectId, "Authz");
        var terms = await Finance.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", HourlyTerms());
        Assert.Equal(HttpStatusCode.Forbidden, terms.StatusCode);
        var agr = await Finance.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements",
            new { effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 1m });
        Assert.Equal(HttpStatusCode.Forbidden, agr.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant.GetAsync($"/api/work-packages/{wp}/rates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Finance.GetAsync($"/api/work-packages/{wp}/rates")).StatusCode);
    }

    // ── Supplier invoices (M5) ──────────────────────────────────────────────

    private async Task<Guid> FinalStatementAsync(Scaffold.Ids ids, string monday, int year, int month)
    {
        await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 600m,
        });
        await EntryAsync(ids.ResourceId, ids.ProjectId, monday, 8m);
        await ApproveWeekAsync(ids.ResourceId, monday);
        var st = await StatementAsync(ids.ResourceId, year, month);
        var id = st.GetProperty("statement").GetProperty("id").GetGuid();
        (await Admin.PostAsync($"/api/statements/{id}/finalize", null)).EnsureSuccessStatusCode();
        return id;
    }

    private async Task<Guid> RegisterAsync(Guid resourceId, string number, decimal total, string currency = "SEK")
    {
        var res = await Finance.PostAsJsonAsync("/api/supplier-invoices", new
        {
            resourceId, number, invoiceDate = "2030-02-01", currency, totalAmount = total,
            lines = new[] { new { kind = "hours", description = "Hours", amount = total } },
        });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task SupplierInvoice_SecondMatchOnSameStatement_Conflicts()
    {
        var ids = await GraphAsync("si-double", HourlyTerms());
        var st = await FinalStatementAsync(ids, "2030-01-07", 2030, 1);
        var first = await RegisterAsync(ids.ResourceId, "A-1", 4800m);
        var second = await RegisterAsync(ids.ResourceId, "A-2", 4800m);

        Assert.Equal(HttpStatusCode.OK, (await Finance.PostAsJsonAsync(
            $"/api/supplier-invoices/{first}/match", new { statementId = st })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Finance.PostAsJsonAsync(
            $"/api/supplier-invoices/{second}/match", new { statementId = st })).StatusCode);
    }

    [Fact]
    public async Task SupplierInvoice_DiscrepancyApproval_RequiresNote_CurrencyMustMatch()
    {
        var ids = await GraphAsync("si-note", HourlyTerms());
        var st = await FinalStatementAsync(ids, "2030-01-14", 2030, 1);

        var eur = await RegisterAsync(ids.ResourceId, "E-1", 4800m, "EUR");
        Assert.Equal(HttpStatusCode.BadRequest, (await Finance.PostAsJsonAsync(
            $"/api/supplier-invoices/{eur}/match", new { statementId = st })).StatusCode);
        (await Finance.DeleteAsync($"/api/supplier-invoices/{eur}")).EnsureSuccessStatusCode();

        var off = await RegisterAsync(ids.ResourceId, "B-1", 5000m);
        var match = await Finance.PostAsJsonAsync($"/api/supplier-invoices/{off}/match", new { statementId = st });
        Assert.Equal("discrepancy", (await match.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("invoice").GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Finance.PostAsync($"/api/supplier-invoices/{off}/approve", null)).StatusCode);
        var ok = await Finance.PostAsJsonAsync($"/api/supplier-invoices/{off}/approve",
            new { note = "Agreed extra 200 for parking" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Agreed extra 200 for parking", (await ok.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("approvalNote").GetString());
    }

    // ── Timesheet review flow (time H5/L6) ──────────────────────────────────

    [Fact]
    public async Task Return_WithoutNote_BadRequest_AndHistoryKeepsEveryStep()
    {
        var ids = await GraphAsync("review-note", HourlyTerms());
        await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-11-05", 8m);
        var sheet = await SubmitAsync(ids.ResourceId, "2029-11-05");

        var noNote = await Admin.PostAsJsonAsync($"/api/timesheets/{sheet}/return", new { note = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);
        var reject = await Admin.PostAsJsonAsync($"/api/timesheets/{sheet}/reject", new { });
        Assert.Equal(HttpStatusCode.BadRequest, reject.StatusCode);

        (await Admin.PostAsJsonAsync($"/api/timesheets/{sheet}/return", new { note = "Split the day" }))
            .EnsureSuccessStatusCode();
        await SubmitAsync(ids.ResourceId, "2029-11-05");

        var detail = await Admin.GetFromJsonAsync<JsonElement>($"/api/timesheets/{sheet}");
        var steps = detail.GetProperty("reviews").EnumerateArray()
            .Select(r => r.GetProperty("action").GetString()).ToList();
        Assert.Equal(["submitted", "returned", "submitted"], steps);
        Assert.Equal("Split the day", detail.GetProperty("reviews")[1].GetProperty("note").GetString());
    }

    [Fact]
    public async Task Reopen_AdminOnly_ReturnsRowsToDraft()
    {
        var ids = await GraphAsync("reopen", HourlyTerms());
        var e = await EntryAsync(ids.ResourceId, ids.ProjectId, "2029-11-12", 8m);
        var sheet = await ApproveWeekAsync(ids.ResourceId, "2029-11-12");

        Assert.Equal(HttpStatusCode.Forbidden, (await Manager.PostAsJsonAsync(
            $"/api/timesheets/{sheet}/reopen", new { note = "fix" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant.PostAsJsonAsync(
            $"/api/timesheets/{sheet}/reopen", new { note = "fix" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync(
            $"/api/timesheets/{sheet}/reopen", new { })).StatusCode);

        var ok = await Admin.PostAsJsonAsync($"/api/timesheets/{sheet}/reopen", new { note = "Wrong project" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var entry = await Admin.GetFromJsonAsync<JsonElement>($"/api/time-entries/{e}");
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("timesheetId").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("payableHours").ValueKind);
        var detail = await Admin.GetFromJsonAsync<JsonElement>($"/api/timesheets/{sheet}");
        Assert.Equal("returned", detail.GetProperty("sheet").GetProperty("status").GetString());
        Assert.Contains(detail.GetProperty("reviews").EnumerateArray(),
            r => r.GetProperty("action").GetString() == "reopened");
    }

    [Fact]
    public async Task Reopen_BlockedWhenMonthClosed_OrInvoiced()
    {
        var closed = await GraphAsync("reopen-closed", HourlyTerms());
        await EntryAsync(closed.ResourceId, closed.ProjectId, "2029-12-03", 8m);
        var s1 = await ApproveWeekAsync(closed.ResourceId, "2029-12-03");
        await Scaffold.SetBaselineAsync(Admin, closed, 2029, 12, 1m);
        factory.Clock.Set(new DateOnly(2030, 1, 2)); // a month closes only once it has ended
        try
        {
            (await Admin.PostAsync($"/api/projects/{closed.ProjectId}/pad/2029/12/close", null))
                .EnsureSuccessStatusCode();
        }
        finally { factory.Clock.Reset(); }
        var r1 = await Admin.PostAsJsonAsync($"/api/timesheets/{s1}/reopen", new { note = "fix" });
        Assert.Equal(HttpStatusCode.Conflict, r1.StatusCode);

        var billed = await GraphAsync("reopen-billed", HourlyTerms());
        await EntryAsync(billed.ResourceId, billed.ProjectId, "2029-12-10", 8m);
        var s2 = await ApproveWeekAsync(billed.ResourceId, "2029-12-10");
        await IssueAsync(await InvoiceAsync(billed.ProjectId, 2029, 12));
        var detail = await Admin.GetFromJsonAsync<JsonElement>($"/api/timesheets/{s2}");
        Assert.Equal("invoiced", detail.GetProperty("sheet").GetProperty("status").GetString());
        var r2 = await Admin.PostAsJsonAsync($"/api/timesheets/{s2}/reopen", new { note = "fix" });
        Assert.NotEqual(HttpStatusCode.OK, r2.StatusCode);
    }
}
