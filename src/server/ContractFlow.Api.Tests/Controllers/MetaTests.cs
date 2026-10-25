using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class MetaTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record Entry(string Code, string Name);

    [Fact]
    public async Task Calendars_ListsCountriesAndRegionalVariants()
    {
        var client = factory.CreateUserClient(DevSeed.ResourceId);
        var list = await client.GetFromJsonAsync<Entry[]>("/api/meta/calendars");

        Assert.Contains(list!, e => e.Code == "SE");
        Assert.Contains(list!, e => e.Code == "GB-ENG");
        Assert.Contains(list!, e => e.Code == "US");
        Assert.Contains(list!, e => e.Code == "NONE");
    }

    [Fact]
    public async Task Countries_CoverIsoWorld()
    {
        var client = factory.CreateUserClient(DevSeed.ResourceId);
        var list = await client.GetFromJsonAsync<Entry[]>("/api/meta/countries");

        Assert.True(list!.Length > 150); // roughly all ISO 3166-1 regions
        Assert.Contains(list, e => e.Code == "SE");
        Assert.Contains(list, e => e.Code == "GB");
        Assert.Contains(list, e => e.Code == "US");
        Assert.All(list!, e => Assert.Equal(2, e.Code.Length));
    }

    [Fact]
    public async Task Currencies_CoverIso4217()
    {
        var client = factory.CreateUserClient(DevSeed.ResourceId);
        var list = await client.GetFromJsonAsync<Entry[]>("/api/meta/currencies");

        Assert.Contains(list!, e => e.Code == "SEK");
        Assert.Contains(list, e => e.Code == "EUR");
        Assert.Contains(list, e => e.Code == "USD");
        Assert.Contains(list, e => e.Code == "GBP");
        Assert.DoesNotContain(list, e => e.Code == "XXX"); // no-currency pseudo-code
        Assert.All(list!, e => Assert.Equal(3, e.Code.Length));
    }

    [Fact]
    public async Task Features_PadOffByDefault()
    {
        var client = factory.CreateUserClient(DevSeed.ResourceId);
        var features = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/meta/features");
        Assert.False(features.GetProperty("pad").GetBoolean());
    }
}
