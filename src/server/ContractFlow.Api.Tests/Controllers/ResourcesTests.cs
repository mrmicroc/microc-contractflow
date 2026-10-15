using System.Net;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class ResourcesTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record ResourceDto(Guid Id, string DisplayName, string Type, bool IsActive, Guid? UserId);
    public record LeaveDto(Guid Id, string From, string To, string Type);
    public record ResourceDetail(Guid Id, string DisplayName, LeaveDto[] Leave);
    public record AgreementDto(Guid Id, Guid? ContractId, int Version, string EffectiveFrom,
        string Currency, decimal? HourlyRate, decimal? TravelTimePercent, string PerDiem);

    private async Task<ResourceDto> CreateResourceAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/resources", new
        {
            displayName = "Casey Example", type = "subcontractor",
            email = "casey@example.invalid",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ResourceDto>())!;
    }

    [Fact]
    public async Task Create_ThenGet_RoundTrips()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var resource = await CreateResourceAsync(client);

        Assert.Equal("Casey Example", resource.DisplayName);
        Assert.Equal("subcontractor", resource.Type);

        var detail = await client.GetFromJsonAsync<ResourceDetail>($"/api/resources/{resource.Id}");
        Assert.NotNull(detail);
        Assert.Empty(detail.Leave);
    }

    [Fact]
    public async Task Agreements_Versioning_ScopedPerContract()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var resource = await CreateResourceAsync(client);

        // Two versions in the default scope (contractId null) → 1, 2
        var v1 = await (await client.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/agreements",
            new { effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 10m }))
            .Content.ReadFromJsonAsync<AgreementDto>();
        var v2 = await (await client.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/agreements",
            new { effectiveFrom = "2026-06-01", currency = "SEK", hourlyRate = 11m }))
            .Content.ReadFromJsonAsync<AgreementDto>();

        Assert.Equal(1, v1!.Version);
        Assert.Equal(2, v2!.Version);
        Assert.Null(v1.ContractId);

        var all = await client.GetFromJsonAsync<AgreementDto[]>(
            $"/api/resources/{resource.Id}/agreements");
        Assert.Equal(2, all!.Length);
        Assert.Equal(10m, all.Single(a => a.Version == 1).HourlyRate);
        Assert.Equal(11m, all.Single(a => a.Version == 2).HourlyRate);
    }

    [Fact]
    public async Task Agreements_ManagerAndResource_CannotSeeRates_FinanceCan()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var resource = await CreateResourceAsync(admin);
        await admin.PostAsJsonAsync($"/api/resources/{resource.Id}/agreements",
            new { effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 10m });

        var manager = factory.CreateUserClient(DevSeed.ManagerId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync($"/api/resources/{resource.Id}/agreements")).StatusCode);

        var consultant = factory.CreateUserClient(DevSeed.ResourceId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await consultant.GetAsync($"/api/resources/{resource.Id}/agreements")).StatusCode);

        var finance = factory.CreateUserClient(DevSeed.FinanceId);
        var ok = await finance.GetAsync($"/api/resources/{resource.Id}/agreements");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Leave_CreateListDelete_FlowWorks()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var resource = await CreateResourceAsync(client);

        var created = await client.PostAsJsonAsync($"/api/resources/{resource.Id}/leave",
            new { from = "2026-07-01", to = "2026-07-15", type = "vacation" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var leave = await created.Content.ReadFromJsonAsync<LeaveDto>();
        Assert.NotNull(leave);

        var detail = await client.GetFromJsonAsync<ResourceDetail>($"/api/resources/{resource.Id}");
        Assert.Single(detail!.Leave);

        var del = await client.DeleteAsync($"/api/resources/{resource.Id}/leave/{leave.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Leave_EndBeforeStart_Returns400()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var resource = await CreateResourceAsync(client);
        var res = await client.PostAsJsonAsync($"/api/resources/{resource.Id}/leave",
            new { from = "2026-07-15", to = "2026-07-01", type = "vacation" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task List_AsResource_IsForbidden()
    {
        var client = factory.CreateUserClient(DevSeed.ResourceId);
        var res = await client.GetAsync("/api/resources");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Create_AsManager_IsForbidden()
    {
        var client = factory.CreateUserClient(DevSeed.ManagerId);
        var res = await client.PostAsJsonAsync("/api/resources",
            new { displayName = "Nope Person", type = "employee" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
