using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// Foreign-currency rows convert at the document month's rate (design §9.3,
/// §11 Q4): expenses on the invoice specification (then marked up per
/// category), expenses and per diem on the statement. Missing rates keep rows
/// out and block finalizing; a rate that priced an issued spec is locked.
/// </summary>
public class ExchangeRateTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record IdRef(Guid Id);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Finance => factory.CreateUserClient(DevSeed.FinanceId);
    private HttpClient Manager => factory.CreateUserClient(DevSeed.ManagerId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    /// <summary>SEK hourly contract with a default and per-category mark-up.</summary>
    private async Task<Scaffold.Ids> GraphAsync(string tag, decimal defaultMarkup = 0m, object[]? markups = null)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        (await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 1000m, expenseMarkupPercent = defaultMarkup,
            expenseMarkups = markups ?? [],
        })).EnsureSuccessStatusCode();
        return ids;
    }

    private Task<HttpResponseMessage> RateAsync(HttpClient client, int year, int month,
        string from, string to, decimal rate) =>
        client.PostAsJsonAsync("/api/exchange-rates", new
        {
            year, month, fromCurrency = from, toCurrency = to, rate, note = "test",
        });

    private async Task ExpenseAsync(Scaffold.Ids ids, string date, decimal amount, string currency,
        string category = "material", bool recharge = true, bool reimburse = false)
    {
        var res = await Admin.PostAsJsonAsync("/api/expenses", new
        {
            resourceId = ids.ResourceId, projectId = ids.ProjectId, date, amount, currency, category,
            rechargeableToCustomer = recharge, reimbursableToResource = reimburse,
            confirmDuplicate = true,
        });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
    }

    private async Task ApproveWeekAsync(Guid resourceId, string date)
    {
        var d = DateOnly.Parse(date, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
        var submit = await Admin.PostAsJsonAsync("/api/timesheets/submit", new
        {
            resourceId, year = ISOWeek.GetYear(d), isoWeek = ISOWeek.GetWeekOfYear(d),
        });
        Assert.True(submit.IsSuccessStatusCode, await submit.Content.ReadAsStringAsync());
        var sheet = (await submit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sheet").GetProperty("id").GetGuid();
        var approve = await Admin.PostAsync($"/api/timesheets/{sheet}/approve", null);
        Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> InvoiceAsync(Guid projectId, int year, int month)
    {
        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate", new { projectId, year, month });
        Assert.True(gen.IsSuccessStatusCode, await gen.Content.ReadAsStringAsync());
        var id = (await gen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return await Admin.GetFromJsonAsync<JsonElement>($"/api/invoices/{id}");
    }

    private static List<JsonElement> Expenses(JsonElement detail) =>
        detail.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("kind").GetString() == "expense").ToList();

    private static decimal Dec(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    [Fact]
    public async Task ForeignExpense_ConvertsAtMonthRate_ThenMarkedUp_OriginalKept()
    {
        var ids = await GraphAsync("fx-eur", markups: [new { category = "material", percent = 10m }]);
        (await RateAsync(Finance, 2029, 1, "EUR", "SEK", 11.5m)).EnsureSuccessStatusCode();
        await ExpenseAsync(ids, "2029-01-15", 1200m, "EUR");
        await ApproveWeekAsync(ids.ResourceId, "2029-01-15");

        var spec = await InvoiceAsync(ids.ProjectId, 2029, 1);
        var line = Assert.Single(Expenses(spec));
        // 1 200 EUR × 11.5 = 13 800 SEK, + 10 % material mark-up = 15 180.
        Assert.Equal(15180m, Dec(line, "amount"));
        Assert.Equal(1200m, Dec(line, "originalAmount"));
        Assert.Equal("EUR", line.GetProperty("originalCurrency").GetString());
        Assert.Equal(11.5m, Dec(line, "exchangeRate"));
        var summary = Assert.Single(spec.GetProperty("invoice").GetProperty("currencySummary").EnumerateArray());
        Assert.Equal("EUR", summary.GetProperty("currency").GetString());
        Assert.Equal(1200m, Dec(summary, "originalTotal"));
        Assert.Equal(13800m, Dec(summary, "convertedTotal"));
        Assert.False(spec.GetProperty("invoice").GetProperty("missingExchangeRate").GetBoolean());
    }

    [Fact]
    public async Task InverseRate_IsUsed_WhenOnlyTheOtherDirectionExists()
    {
        var ids = await GraphAsync("fx-inverse");
        (await RateAsync(Finance, 2029, 2, "SEK", "EUR", 0.08m)).EnsureSuccessStatusCode();
        await ExpenseAsync(ids, "2029-02-12", 100m, "EUR");
        await ApproveWeekAsync(ids.ResourceId, "2029-02-12");

        var line = Assert.Single(Expenses(await InvoiceAsync(ids.ProjectId, 2029, 2)));
        Assert.Equal(1250m, Dec(line, "amount")); // 1 / 0.08 = 12.5
    }

    [Fact]
    public async Task MissingRate_RowLeftOut_FinalizeBlocked_UntilRateEnteredAndRegenerated()
    {
        var ids = await GraphAsync("fx-missing");
        await ExpenseAsync(ids, "2029-03-12", 100m, "NOK");
        await ExpenseAsync(ids, "2029-03-13", 500m, "SEK");
        await ApproveWeekAsync(ids.ResourceId, "2029-03-12");

        var spec = await InvoiceAsync(ids.ProjectId, 2029, 3);
        var inv = spec.GetProperty("invoice");
        Assert.True(inv.GetProperty("missingExchangeRate").GetBoolean());
        Assert.Equal(500m, Dec(inv, "totalAmount"));
        Assert.Contains(inv.GetProperty("warnings").EnumerateArray(),
            w => w.GetString()!.Contains("NOK→SEK") && w.GetString()!.Contains("2029-03"));

        var id = inv.GetProperty("id").GetGuid();
        (await Admin.PostAsync($"/api/invoices/{id}/review", null)).EnsureSuccessStatusCode();
        var blocked = await Admin.PostAsync($"/api/invoices/{id}/finalize", null);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("missingExchangeRate",
            (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        (await RateAsync(Finance, 2029, 3, "NOK", "SEK", 1.2m)).EnsureSuccessStatusCode();
        (await Admin.PostAsync($"/api/invoices/{id}/back-to-draft", null)).EnsureSuccessStatusCode();
        var again = (await InvoiceAsync(ids.ProjectId, 2029, 3)).GetProperty("invoice");
        Assert.False(again.GetProperty("missingExchangeRate").GetBoolean());
        Assert.Equal(620m, Dec(again, "totalAmount"));
        (await Admin.PostAsync($"/api/invoices/{id}/review", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await Admin.PostAsync($"/api/invoices/{id}/finalize", null)).StatusCode);
    }

    [Fact]
    public async Task RateUsedByIssuedSpec_IsLocked()
    {
        var ids = await GraphAsync("fx-locked");
        var created = await RateAsync(Finance, 2029, 4, "USD", "SEK", 10m);
        var rateId = (await created.Content.ReadFromJsonAsync<IdRef>())!.Id;
        await ExpenseAsync(ids, "2029-04-10", 10m, "USD");
        await ApproveWeekAsync(ids.ResourceId, "2029-04-10");
        var id = (await InvoiceAsync(ids.ProjectId, 2029, 4)).GetProperty("invoice").GetProperty("id").GetGuid();
        foreach (var step in new[] { "review", "finalize", "issue" })
            (await Admin.PostAsync($"/api/invoices/{id}/{step}", null)).EnsureSuccessStatusCode();

        var update = await Finance.PutAsJsonAsync($"/api/exchange-rates/{rateId}", new
        {
            year = 2029, month = 4, fromCurrency = "USD", toCurrency = "SEK", rate = 11m,
        });
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Finance.DeleteAsync($"/api/exchange-rates/{rateId}")).StatusCode);
        var listed = await Finance.GetFromJsonAsync<JsonElement[]>("/api/exchange-rates?year=2029&month=4");
        Assert.True(listed!.Single(r => r.GetProperty("id").GetGuid() == rateId).GetProperty("inUse").GetBoolean());
    }

    [Fact]
    public async Task Statement_ConvertsExpensesAndPerDiem_ToAgreementCurrency()
    {
        var ids = await GraphAsync("fx-statement");
        (await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 500m,
        })).EnsureSuccessStatusCode();
        (await RateAsync(Finance, 2029, 5, "EUR", "SEK", 11m)).EnsureSuccessStatusCode();
        await ExpenseAsync(ids, "2029-05-14", 50m, "EUR", recharge: false, reimburse: true);
        (await Admin.PostAsJsonAsync("/api/allowances", new
        {
            resourceId = ids.ResourceId, projectId = ids.ProjectId, date = "2029-05-15",
            amount = 40m, currency = "EUR",
        })).EnsureSuccessStatusCode();
        await ApproveWeekAsync(ids.ResourceId, "2029-05-14");

        var gen = await Admin.PostAsJsonAsync("/api/statements/generate",
            new { resourceId = ids.ResourceId, year = 2029, month = 5 });
        Assert.True(gen.IsSuccessStatusCode, await gen.Content.ReadAsStringAsync());
        var sid = (await gen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var detail = await Admin.GetFromJsonAsync<JsonElement>($"/api/statements/{sid}");
        var lines = detail.GetProperty("lines").EnumerateArray().ToList();
        var expense = lines.Single(l => l.GetProperty("kind").GetString() == "expense");
        var perDiem = lines.Single(l => l.GetProperty("kind").GetString() == "allowance");
        Assert.Equal(550m, Dec(expense, "amount"));   // 50 EUR × 11
        Assert.Equal(440m, Dec(perDiem, "amount"));   // 40 EUR × 11
        Assert.Equal("EUR", perDiem.GetProperty("originalCurrency").GetString());
        Assert.Equal(990m, Dec(detail.GetProperty("statement"), "totalAmount"));
    }

    [Fact]
    public async Task Statement_MissingRate_BlocksFinalize()
    {
        var ids = await GraphAsync("fx-statement-missing");
        (await Admin.PostAsJsonAsync($"/api/resources/{ids.ResourceId}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 500m,
        })).EnsureSuccessStatusCode();
        await ExpenseAsync(ids, "2029-06-12", 50m, "DKK", recharge: false, reimburse: true);
        await ApproveWeekAsync(ids.ResourceId, "2029-06-12");
        var gen = await Admin.PostAsJsonAsync("/api/statements/generate",
            new { resourceId = ids.ResourceId, year = 2029, month = 6 });
        var st = await gen.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(st.GetProperty("missingExchangeRate").GetBoolean());
        var fin = await Admin.PostAsync($"/api/statements/{st.GetProperty("id").GetGuid()}/finalize", null);
        Assert.Equal(HttpStatusCode.Conflict, fin.StatusCode);
    }

    [Fact]
    public async Task MarkupPerCategory_OverridesTheDefault()
    {
        var ids = await GraphAsync("fx-markup", defaultMarkup: 5m, markups:
        [
            new { category = "material", percent = 10m },
            new { category = "travel", percent = 0m },
        ]);
        await ExpenseAsync(ids, "2029-07-09", 1000m, "SEK", "material");
        await ExpenseAsync(ids, "2029-07-10", 1000m, "SEK", "travel");
        await ExpenseAsync(ids, "2029-07-11", 1000m, "SEK", "meal");
        await ApproveWeekAsync(ids.ResourceId, "2029-07-09");

        var amounts = Expenses(await InvoiceAsync(ids.ProjectId, 2029, 7))
            .OrderBy(l => l.GetProperty("date").GetString())
            .Select(l => Dec(l, "amount")).ToList();
        Assert.Equal([1100m, 1000m, 1050m], amounts);

        var terms = await Admin.GetFromJsonAsync<JsonElement[]>($"/api/contracts/{ids.ContractId}/terms");
        var latest = terms!.OrderByDescending(t => t.GetProperty("version").GetInt32()).First();
        Assert.Equal(2, latest.GetProperty("expenseMarkups").GetArrayLength());
    }

    [Fact]
    public async Task MarkupOutOfRange_OrDuplicateCategory_BadRequest()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "fx-markup-bad");
        var tooHigh = await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-02-01", currency = "SEK", holidayCalendar = "SE",
            expenseMarkups = new[] { new { category = "material", percent = 1001m } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);
        var duplicate = await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-02-01", currency = "SEK", holidayCalendar = "SE",
            expenseMarkups = new[] { new { category = "meal", percent = 1m }, new { category = "meal", percent = 2m } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task MyProjects_CarryTheContractCurrency_ForExpenseDefaults()
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, "fx-mine");
        (await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-02-01", currency = "EUR", holidayCalendar = "SE", timeBilling = "hourly",
        })).EnsureSuccessStatusCode();
        var mine = await Admin.GetFromJsonAsync<JsonElement[]>("/api/projects/mine");
        var row = mine!.Single(p => p.GetProperty("id").GetGuid() == ids.ProjectId);
        Assert.Equal("EUR", row.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task RateEndpoints_AdminAndFinanceWrite_ManagerReads_ResourceForbidden()
    {
        Assert.Equal(HttpStatusCode.Created, (await RateAsync(Admin, 2029, 8, "GBP", "SEK", 13m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RateAsync(Finance, 2029, 8, "CHF", "SEK", 12m)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RateAsync(Manager, 2029, 8, "PLN", "SEK", 2.6m)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RateAsync(Consultant, 2029, 8, "PLN", "SEK", 2.6m)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Manager.GetAsync("/api/exchange-rates?year=2029&month=8")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant.GetAsync("/api/exchange-rates")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await RateAsync(Finance, 2029, 8, "GBP", "SEK", 13.1m)).StatusCode);
    }
}
