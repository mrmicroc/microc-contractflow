using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>
/// One organisation register for both roles: an organisation can be the
/// party on a direct contract and the end client on a contract an
/// intermediary holds. Covers the contract wiring, the organisation detail,
/// delete protection, the invoice snapshot and margins per end client.
/// </summary>
public class OrganisationRolesTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record IdRef(Guid Id);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);
    private HttpClient Finance => factory.CreateUserClient(DevSeed.FinanceId);
    private HttpClient Consultant => factory.CreateUserClient(DevSeed.ResourceId);

    private async Task<Guid> OrganisationAsync(string name) =>
        (await (await Admin.PostAsJsonAsync("/api/customers", new { name }))
            .Content.ReadFromJsonAsync<IdRef>())!.Id;

    private async Task<HttpResponseMessage> ContractAsync(
        Guid customerId, Guid? endClientId, string name, string? endClientReference = null) =>
        await Admin.PostAsJsonAsync("/api/contracts", new
        {
            customerId, endClientId, endClientReference, name, startDate = "2026-01-01",
        });

    private async Task<Guid> ContractIdAsync(
        Guid customerId, Guid? endClientId, string name, string? endClientReference = null)
    {
        var res = await ContractAsync(customerId, endClientId, name, endClientReference);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<IdRef>())!.Id;
    }

    /// <summary>Hourly terms at 1 000/h + a project on the contract.</summary>
    private async Task<Guid> BillableProjectAsync(Guid contractId, string name)
    {
        (await Admin.PostAsJsonAsync($"/api/contracts/{contractId}/terms", new
        {
            effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
            timeBilling = "hourly", hourlyRate = 1000m,
        })).EnsureSuccessStatusCode();
        return (await (await Admin.PostAsJsonAsync("/api/projects", new { contractId, name }))
            .Content.ReadFromJsonAsync<IdRef>())!.Id;
    }

    private async Task<Guid> ResourceAsync(string name) =>
        (await (await Admin.PostAsJsonAsync("/api/resources",
            new { displayName = name, type = "subcontractor" }))
            .Content.ReadFromJsonAsync<IdRef>())!.Id;

    /// <summary>8 h on <paramref name="date"/>, submitted and approved.</summary>
    private async Task ApprovedDayAsync(Guid resourceId, Guid projectId, string date)
    {
        (await Admin.PostAsJsonAsync("/api/time-entries", new
        {
            resourceId, projectId, date, category = "work", actualHours = 8m,
        })).EnsureSuccessStatusCode();
        var d = DateOnly.Parse(date, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
        var submit = await Admin.PostAsJsonAsync("/api/timesheets/submit", new
        {
            resourceId, year = ISOWeek.GetYear(d), isoWeek = ISOWeek.GetWeekOfYear(d),
        });
        submit.EnsureSuccessStatusCode();
        var sheetId = (await submit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sheet").GetProperty("id").GetGuid();
        (await Admin.PostAsync($"/api/timesheets/{sheetId}/approve", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Organisation_IsDirectCustomerOnOne_AndEndClientOnAnother()
    {
        var grid = await OrganisationAsync("North Link Roles");
        var consultancy = await OrganisationAsync("Example Org Roles");
        var direct = await ContractIdAsync(grid, null, "Direct framework");
        var viaIntermediary = await ContractIdAsync(consultancy, grid, "Subcontract", "PO-4711");

        var detail = await Admin.GetFromJsonAsync<JsonElement>($"/api/customers/{grid}");
        Assert.Equal(direct, Assert.Single(detail.GetProperty("contracts").EnumerateArray())
            .GetProperty("id").GetGuid());
        var asEndClient = Assert.Single(detail.GetProperty("endClientContracts").EnumerateArray());
        Assert.Equal(viaIntermediary, asEndClient.GetProperty("id").GetGuid());
        Assert.Equal("Example Org Roles", asEndClient.GetProperty("customerName").GetString());
        Assert.Equal("PO-4711", asEndClient.GetProperty("endClientReference").GetString());

        var list = await Admin.GetFromJsonAsync<JsonElement[]>("/api/customers");
        var row = list!.Single(c => c.GetProperty("id").GetGuid() == grid);
        Assert.Equal(1, row.GetProperty("contractCount").GetInt32());
        Assert.Equal(1, row.GetProperty("endClientContractCount").GetInt32());

        var contract = await Admin.GetFromJsonAsync<JsonElement>($"/api/contracts/{viaIntermediary}");
        Assert.Equal(grid, contract.GetProperty("endClientId").GetGuid());
        Assert.Equal("North Link Roles", contract.GetProperty("endClientName").GetString());
        Assert.Equal("PO-4711", contract.GetProperty("endClientReference").GetString());
    }

    [Fact]
    public async Task EndClientSameAsParty_StoredAsDirectContract()
    {
        var org = await OrganisationAsync("Self Org");
        var id = await ContractIdAsync(org, org, "Self-referencing", "IGNORED-REF");

        var contract = await Admin.GetFromJsonAsync<JsonElement>($"/api/contracts/{id}");
        Assert.Equal(JsonValueKind.Null, contract.GetProperty("endClientId").ValueKind);
        Assert.Equal(JsonValueKind.Null, contract.GetProperty("endClientReference").ValueKind);
    }

    [Fact]
    public async Task UnknownEndClient_BadRequest()
    {
        var org = await OrganisationAsync("Lonely Org");
        var res = await ContractAsync(org, Guid.NewGuid(), "Bad end client");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Organisation_InUseAsEndClient_CannotBeDeleted()
    {
        var endClient = await OrganisationAsync("Protected End Client");
        var party = await OrganisationAsync("Protected Party");
        await ContractIdAsync(party, endClient, "Holds the end client");

        var res = await Admin.DeleteAsync($"/api/customers/{endClient}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        var unused = await OrganisationAsync("Unused Org");
        Assert.Equal(HttpStatusCode.NoContent, (await Admin.DeleteAsync($"/api/customers/{unused}")).StatusCode);
    }

    [Fact]
    public async Task OrganisationDetail_AllowedForFinance_ForbiddenForResource()
    {
        var org = await OrganisationAsync("Visibility Org");
        Assert.Equal(HttpStatusCode.OK, (await Finance.GetAsync($"/api/customers/{org}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Consultant.GetAsync($"/api/customers/{org}")).StatusCode);
    }

    [Fact]
    public async Task InvoiceSpec_CarriesEndClient_AndKeepsSnapshotAfterRename()
    {
        var endClient = await OrganisationAsync("Snapshot End Client");
        var party = await OrganisationAsync("Snapshot Party");
        var contract = await ContractIdAsync(party, endClient, "Snapshot contract", "REF-77");
        var project = await BillableProjectAsync(contract, "Snapshot project");
        var resource = await ResourceAsync("Snapshot Tester");
        await ApprovedDayAsync(resource, project, "2026-05-04");

        var gen = await Admin.PostAsJsonAsync("/api/invoices/generate", new { projectId = project, year = 2026, month = 5 });
        Assert.True(gen.IsSuccessStatusCode, await gen.Content.ReadAsStringAsync());
        var specId = (await gen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await Admin.PutAsJsonAsync($"/api/customers/{endClient}", new { name = "Renamed End Client" }))
            .EnsureSuccessStatusCode();

        var spec = (await Admin.GetFromJsonAsync<JsonElement>($"/api/invoices/{specId}")).GetProperty("invoice");
        Assert.Equal("Snapshot Party", spec.GetProperty("customerName").GetString());
        Assert.Equal("Snapshot End Client", spec.GetProperty("endClientName").GetString());
        Assert.Equal("REF-77", spec.GetProperty("endClientReference").GetString());
    }

    [Fact]
    public async Task Margins_GroupByEffectiveEndClient_DirectAndViaIntermediary()
    {
        var grid = await OrganisationAsync("Margin End Client");
        var consultancy = await OrganisationAsync("Margin Intermediary");
        var other = await OrganisationAsync("Margin Other");
        var directProject = await BillableProjectAsync(
            await ContractIdAsync(grid, null, "Margin direct"), "Margin direct project");
        var viaProject = await BillableProjectAsync(
            await ContractIdAsync(consultancy, grid, "Margin via"), "Margin via project");
        var otherProject = await BillableProjectAsync(
            await ContractIdAsync(other, null, "Margin unrelated"), "Margin unrelated project");
        var resource = await ResourceAsync("Margin Tester");
        await ApprovedDayAsync(resource, directProject, "2026-06-01");
        await ApprovedDayAsync(resource, viaProject, "2026-06-08");
        await ApprovedDayAsync(resource, otherProject, "2026-06-15");

        var rows = await Finance.GetFromJsonAsync<JsonElement[]>(
            $"/api/reports/margins?year=2026&month=6&endClientId={grid}");
        var projects = rows!.Select(r => r.GetProperty("projectId").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { directProject, viaProject }, projects);
        Assert.All(rows!, r => Assert.Equal(grid, r.GetProperty("endClientId").GetGuid()));
        var via = rows!.First(r => r.GetProperty("projectId").GetGuid() == viaProject);
        Assert.Equal("Margin Intermediary", via.GetProperty("customerName").GetString());
        Assert.Equal("Margin End Client", via.GetProperty("endClientName").GetString());
    }
}
