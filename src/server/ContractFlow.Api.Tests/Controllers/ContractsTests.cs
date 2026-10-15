using System.Net;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class ContractsTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record ContractDto(Guid Id, Guid CustomerId, string Name, string Model,
        string StartDate, string? EndDate, bool IsActive);
    public record TermsDto(Guid Id, int Version, string EffectiveFrom,
        string Currency, decimal? PadRate, decimal? HourlyRate,
        decimal CarryOverMaxPercent, int CancellationDeadlineBusinessDays);

    private async Task<ContractDto> CreateContractAsync(HttpClient client)
    {
        var customer = await (await client.PostAsJsonAsync("/api/customers",
            new { name = "Contract Host Org" })).Content.ReadFromJsonAsync<ContractsTests.CustomerMarker>();
        var res = await client.PostAsJsonAsync("/api/contracts", new
        {
            customerId = customer!.Id,
            name = "Example Framework",
            model = "padBased",
            startDate = "2026-01-01",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ContractDto>())!;
    }

    // Minimal shape needed to mint a customer first
    public record CustomerMarker(Guid Id);

    [Fact]
    public async Task Create_ThenList_RoundTrips()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var contract = await CreateContractAsync(client);

        Assert.Equal("Example Framework", contract.Name);
        Assert.Equal("padBased", contract.Model);

        var list = await client.GetFromJsonAsync<ContractDto[]>(
            $"/api/contracts?customerId={contract.CustomerId}");
        Assert.Contains(list!, c => c.Id == contract.Id);
    }

    [Fact]
    public async Task Create_WithUnknownCustomer_Returns400()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var res = await client.PostAsJsonAsync("/api/contracts", new
        {
            customerId = Guid.NewGuid(),
            name = "Ghost Contract",
            model = "hourly",
            startDate = "2026-01-01",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Terms_PostTwice_AssignsIncrementingVersions_AndPreservesOld()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var contract = await CreateContractAsync(client);

        var v1 = await (await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/terms",
            new { effectiveFrom = "2026-01-01", currency = "sek", holidayCalendar = "SE",
                  padRate = 100m, carryOverMaxPercent = 20m }))
            .Content.ReadFromJsonAsync<TermsDto>();
        var v2 = await (await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/terms",
            new { effectiveFrom = "2026-06-01", currency = "SEK", holidayCalendar = "SE",
                  padRate = 110m, carryOverMaxPercent = 25m }))
            .Content.ReadFromJsonAsync<TermsDto>();

        Assert.Equal(1, v1!.Version);
        Assert.Equal(2, v2!.Version);
        Assert.Equal("SEK", v1.Currency); // normalized to upper-case

        var all = await client.GetFromJsonAsync<TermsDto[]>($"/api/contracts/{contract.Id}/terms");
        Assert.Equal(2, all!.Length);
        // The older version is untouched — versioning never rewrites history.
        Assert.Equal(100m, all.Single(t => t.Version == 1).PadRate);
        Assert.Equal(110m, all.Single(t => t.Version == 2).PadRate);
    }

    [Fact]
    public async Task Terms_Effective_ResolvesLatestVersionOnOrBeforeDate()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var contract = await CreateContractAsync(client);
        await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/terms",
            new { effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE", padRate = 100m });
        await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/terms",
            new { effectiveFrom = "2026-06-01", currency = "SEK", holidayCalendar = "SE", padRate = 110m });

        var march = await client.GetFromJsonAsync<TermsDto>(
            $"/api/contracts/{contract.Id}/terms/effective?on=2026-03-15");
        Assert.Equal(1, march!.Version);

        var july = await client.GetFromJsonAsync<TermsDto>(
            $"/api/contracts/{contract.Id}/terms/effective?on=2026-07-01");
        Assert.Equal(2, july!.Version);

        // Before any version takes effect → 404
        var res = await client.GetAsync(
            $"/api/contracts/{contract.Id}/terms/effective?on=2025-12-31");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Create_AsManager_IsForbidden()
    {
        var client = factory.CreateUserClient(DevSeed.ManagerId);
        var res = await client.PostAsJsonAsync("/api/contracts", new
        {
            customerId = Guid.NewGuid(), name = "Nope", model = "hourly", startDate = "2026-01-01",
        });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Terms_AsFinance_CanRead()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var contract = await CreateContractAsync(admin);

        var finance = factory.CreateUserClient(DevSeed.FinanceId);
        var terms = await finance.GetFromJsonAsync<TermsDto[]>(
            $"/api/contracts/{contract.Id}/terms");
        Assert.NotNull(terms);
    }

    [Fact]
    public async Task Get_AsResource_IsForbidden()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var contract = await CreateContractAsync(admin);

        var resource = factory.CreateUserClient(DevSeed.ResourceId);
        var res = await resource.GetAsync($"/api/contracts/{contract.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
