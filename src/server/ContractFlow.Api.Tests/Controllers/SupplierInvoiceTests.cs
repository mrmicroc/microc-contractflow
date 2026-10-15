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
/// Supplier-invoice reconciliation (design §6.2) + project margins (§8). The
/// statement is the authority; the subcontractor's invoice is checked against
/// it per kind and the diffs stay visible.
/// </summary>
public class SupplierInvoiceTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record IdRef(Guid Id);
    public record InvDto(Guid Id, string Status, decimal TotalAmount, Guid? StatementId);
    public record DiffDto(string Kind, decimal Invoiced, decimal Statement, decimal Diff);
    public record InvDetail(InvDto Invoice, DiffDto[]? Diffs);
    public record StDto(Guid Id, string Status);
    public record MarginRowDto(Guid ProjectId, string ProjectName,
        decimal Billed, decimal Cost, decimal Margin, decimal? MarginPercent);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);
    private HttpClient Finance => factory.CreateUserClient(DevSeed.FinanceId);

    /// <summary>Graph + rated terms/agreement + an approved week + a finalized
    /// statement for the linked resource. Returns (project, statement).</summary>
    private async Task<(Scaffold.Ids ids, Guid statementId)> SetupAsync(string tag, string monday)
    {
        var ids = await Scaffold.CommercialGraphAsync(Admin, tag);
        var linked = TimeReportingTests.LinkedId;
        await factory.SeedAsync(async db =>
        {
            if (!await db.Resources.AnyAsync(r => r.Id == linked))
            {
                db.Resources.Add(new Resource
                {
                    Id = linked, DisplayName = "Linked Tester",
                    Type = ResourceType.Subcontractor, UserId = DevSeed.ResourceId,
                });
                await db.SaveChangesAsync();
            }
        });
        await Admin.PostAsJsonAsync($"/api/contracts/{ids.ContractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            hourlyRate = 1000m, expenseMarkupPercent = 10m,
        });
        await Admin.PostAsJsonAsync($"/api/resources/{linked}/agreements", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK",
            hourlyRate = 600m, mileageRate = 25m, travelTimePercent = 50m,
        });

        async Task Post(string url, object body) =>
            (await Consultant.PostAsJsonAsync(url, body)).EnsureSuccessStatusCode();
        await Post("/api/time-entries", new
        {
            projectId = ids.ProjectId, date = monday, category = "work", actualHours = 8m,
        });
        await Post("/api/expenses", new
        {
            projectId = ids.ProjectId, date = monday, category = "material",
            amount = 1200m, currency = "SEK",
            reimbursableToResource = true, rechargeableToCustomer = true, includedInRate = false,
        });
        await Post("/api/travel-records", new
        {
            projectId = ids.ProjectId, date = monday, mode = "car",
            distanceKm = 100m, durationHours = 2m,
        });
        await Post("/api/allowances", new
        {
            projectId = ids.ProjectId, date = monday, amount = 350m, currency = "SEK",
        });

        var d = DateOnly.Parse(monday);
        var submit = await Consultant.PostAsJsonAsync("/api/timesheets/submit", new
        {
            year = ISOWeek.GetYear(d.ToDateTime(TimeOnly.MinValue)),
            isoWeek = ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue)),
        });
        submit.EnsureSuccessStatusCode();
        var sheetId = (await submit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sheet").GetProperty("id").GetGuid();
        (await Admin.PostAsync($"/api/timesheets/{sheetId}/approve", null))
            .EnsureSuccessStatusCode();

        var st = await Admin.PostAsJsonAsync("/api/statements/generate",
            new { resourceId = linked, year = d.Year, month = d.Month });
        st.EnsureSuccessStatusCode();
        var stDto = await st.Content.ReadFromJsonAsync<StDto>();
        (await Admin.PostAsync($"/api/statements/{stDto!.Id}/finalize", null))
            .EnsureSuccessStatusCode();
        return (ids, stDto.Id);
    }

    // The approved week above produces statement kinds/totals:
    // hours 4800 · expense 1200 · mileage 2500 · travelTime 600 · allowance 350 = 9450.
    private static List<object> Lines(params (string kind, decimal amount)[] xs) =>
        xs.Select(x => (object)new { kind = x.kind, description = x.kind, amount = x.amount })
          .ToList();

    private static List<object> MatchingLines() => Lines(
        ("hours", 4800m), ("expense", 1200m), ("mileage", 2500m),
        ("travelTime", 600m), ("allowance", 350m));

    [Fact]
    public async Task Match_Equal_Lines_Lands_On_Matched()
    {
        var (_, stId) = await SetupAsync("match-ok", "2028-07-03");
        var reg = await Admin.PostAsJsonAsync("/api/supplier-invoices", new
        {
            resourceId = TimeReportingTests.LinkedId, number = "UE-1001",
            invoiceDate = "2028-07-31", currency = "SEK", totalAmount = 9450m,
            lines = MatchingLines(),
        });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var inv = await reg.Content.ReadFromJsonAsync<InvDto>();

        var match = await Admin.PostAsJsonAsync($"/api/supplier-invoices/{inv!.Id}/match",
            new { statementId = stId });
        Assert.Equal(HttpStatusCode.OK, match.StatusCode);
        var detail = await match.Content.ReadFromJsonAsync<InvDetail>();
        Assert.Equal("matched", detail!.Invoice.Status);
        Assert.All(detail.Diffs!, d => Assert.Equal(0m, d.Diff));
    }

    [Fact]
    public async Task Match_Missing_Kind_Shows_Discrepancy()
    {
        var (_, stId) = await SetupAsync("match-diff", "2028-08-07");
        var reg = await Admin.PostAsJsonAsync("/api/supplier-invoices", new
        {
            resourceId = TimeReportingTests.LinkedId, number = "UE-1002",
            invoiceDate = "2028-08-31", currency = "SEK", totalAmount = 9100m,
            // allowance (350) forgotten by the subcontractor.
            lines = Lines(("hours", 4800m), ("expense", 1200m), ("mileage", 2500m),
                ("travelTime", 600m)),
        });
        var inv = await reg.Content.ReadFromJsonAsync<InvDto>();

        var match = await Admin.PostAsJsonAsync($"/api/supplier-invoices/{inv!.Id}/match",
            new { statementId = stId });
        var detail = await match.Content.ReadFromJsonAsync<InvDetail>();
        Assert.Equal("discrepancy", detail!.Invoice.Status);
        var d = Assert.Single(detail.Diffs!, x => x.Kind == "allowance");
        Assert.Equal(0m, d.Invoiced);
        Assert.Equal(350m, d.Statement);
        Assert.Equal(-350m, d.Diff);
    }

    [Fact]
    public async Task Register_Rejects_Total_Mismatch_And_Duplicate()
    {
        var (ids, _) = await SetupAsync("match-val", "2028-09-04");
        var bad = await Admin.PostAsJsonAsync("/api/supplier-invoices", new
        {
            resourceId = TimeReportingTests.LinkedId, number = "UE-1003",
            invoiceDate = "2028-09-30", currency = "SEK", totalAmount = 9999m,
            lines = MatchingLines(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await Admin.PostAsJsonAsync("/api/supplier-invoices", new
        {
            resourceId = TimeReportingTests.LinkedId, number = "UE-1004",
            invoiceDate = "2028-09-30", currency = "SEK", totalAmount = 9450m,
            lines = MatchingLines(),
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var dupe = await Admin.PostAsJsonAsync("/api/supplier-invoices", new
        {
            resourceId = TimeReportingTests.LinkedId, number = "UE-1004",
            invoiceDate = "2028-09-30", currency = "SEK", totalAmount = 9450m,
        });
        Assert.Equal(HttpStatusCode.Conflict, dupe.StatusCode);
    }

    [Fact]
    public async Task Approve_Pay_Flow_And_Authz()
    {
        var (_, stId) = await SetupAsync("match-flow", "2028-10-02");
        var reg = await Admin.PostAsJsonAsync("/api/supplier-invoices", new
        {
            resourceId = TimeReportingTests.LinkedId, number = "UE-1005",
            invoiceDate = "2028-10-31", currency = "SEK", totalAmount = 9450m,
            lines = MatchingLines(),
        });
        var inv = await reg.Content.ReadFromJsonAsync<InvDto>();

        // Can't approve before matching.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PostAsync($"/api/supplier-invoices/{inv!.Id}/approve", null)).StatusCode);
        // Resource can't register but can read its own list.
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant
            .PostAsJsonAsync("/api/supplier-invoices", new
            {
                resourceId = TimeReportingTests.LinkedId, number = "UE-X",
                invoiceDate = "2028-10-31", currency = "SEK", totalAmount = 1m,
            })).StatusCode);
        var ownList = await Consultant.GetAsync("/api/supplier-invoices");
        Assert.True(ownList.StatusCode == HttpStatusCode.OK,
            await ownList.Content.ReadAsStringAsync());

        await Admin.PostAsJsonAsync($"/api/supplier-invoices/{inv.Id}/match",
            new { statementId = stId });
        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsync($"/api/supplier-invoices/{inv.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.DeleteAsync($"/api/supplier-invoices/{inv.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsync($"/api/supplier-invoices/{inv.Id}/pay", null)).StatusCode);
    }

    // ── Margins (design §8) ─────────────────────────────────────────────────

    [Fact]
    public async Task Margins_Report_Revenue_Cost_Margin()
    {
        var (ids, _) = await SetupAsync("margins", "2028-11-06");
        await Admin.PostAsJsonAsync("/api/invoices/generate",
            new { projectId = ids.ProjectId, year = 2028, month = 11 });

        var rows = await Admin.GetFromJsonAsync<MarginRowDto[]>(
            "/api/reports/margins?year=2028&month=11");
        var row = Assert.Single(rows!, r => r.ProjectId == ids.ProjectId);
        // Billed: 8 h × 1000 + 1200×1.10 = 9320. Cost: statement-side 9450.
        // Margin −130 — the report shows it honestly.
        Assert.Equal(9320m, row.Billed);
        Assert.Equal(9450m, row.Cost);
        Assert.Equal(-130m, row.Margin);
        Assert.Equal(-1.4m, row.MarginPercent);
    }

    [Fact]
    public async Task Margins_Resource_Has_No_Access()
    {
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Consultant.GetAsync("/api/reports/margins?year=2028&month=11")).StatusCode);
    }
}
