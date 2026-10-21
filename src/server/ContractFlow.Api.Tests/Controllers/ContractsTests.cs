using System.Net;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class ContractsTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record ContractDto(Guid Id, Guid CustomerId, string Name, string[] Components,
        string StartDate, string? EndDate, bool IsActive);
    public record TermsDto(Guid Id, int Version, string EffectiveFrom,
        string Currency, string HolidayCalendar, decimal? PadRate, decimal? HourlyRate,
        decimal CarryOverMaxPercent, int CancellationDeadlineBusinessDays);

    private async Task<ContractDto> CreateContractAsync(HttpClient client)
    {
        var customer = await (await client.PostAsJsonAsync("/api/customers",
            new { name = "Contract Host Org" })).Content.ReadFromJsonAsync<ContractsTests.CustomerMarker>();
        var res = await client.PostAsJsonAsync("/api/contracts", new
        {
            customerId = customer!.Id,
            name = "Example Framework",
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
        Assert.Empty(contract.Components);

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
    public async Task Terms_UnknownCalendar_IsRejected()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var contract = await CreateContractAsync(client);

        var res = await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/terms",
            new { effectiveFrom = "2026-01-01", currency = "SEK",
                  holidayCalendar = "MARS" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        // A supported regional variant is accepted and normalised.
        var ok = await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/terms",
            new { effectiveFrom = "2026-01-01", currency = "EUR",
                  holidayCalendar = "gb-sct" });
        ok.EnsureSuccessStatusCode();
        var dto = await ok.Content.ReadFromJsonAsync<TermsDto>();
        Assert.Equal("GB-SCT", dto!.HolidayCalendar);
    }

    [Fact]
    public async Task Create_AsManager_IsForbidden()
    {
        var client = factory.CreateUserClient(DevSeed.ManagerId);
        var res = await client.PostAsJsonAsync("/api/contracts", new
        {
            customerId = Guid.NewGuid(), name = "Nope", startDate = "2026-01-01",
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
    public async Task Components_Derive_FromTermsVersionInForce()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var contract = await CreateContractAsync(client);

        // Hourly + PAD + one-off scheduled charges can all be on at once (§4 components).
        await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/terms",
            new { effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
                  timeBilling = "hourly", padEnabled = true, padRate = 100m });
        (await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/charges",
            new { date = "2026-02-01", amount = 5000m, description = "Set-up fee" }))
            .EnsureSuccessStatusCode();

        var detail = await client.GetFromJsonAsync<ContractDto>(
            $"/api/contracts/{contract.Id}");
        Assert.Equal(new[] { "hourly", "pad", "scheduledCharges" }, detail!.Components);

        // A later version flipping to daily-rate without PAD re-labels it.
        await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/terms",
            new { effectiveFrom = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"),
                  currency = "SEK", holidayCalendar = "SE", timeBilling = "dailyRate",
                  dayRate = 8000m, halfDayRate = 4000m });
        var after = await client.GetFromJsonAsync<ContractDto>(
            $"/api/contracts/{contract.Id}");
        Assert.Equal(["dailyRate", "scheduledCharges"], after!.Components); // charges are contract-level, not per version
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
