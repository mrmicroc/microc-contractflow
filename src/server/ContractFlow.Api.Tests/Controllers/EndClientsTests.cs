using System.Net;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class EndClientsTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record EndClientDto(Guid Id, string Name, string? Country,
        string? AddressLine, string? PostalCode, string? City, string? Notes);

    [Fact]
    public async Task Create_CountryAndAddress_RoundTrip()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var created = await (await admin.PostAsJsonAsync("/api/endclients", new
        {
            name = "Northern Utility",
            country = "no",
            addressLine = "Fjordveien 5",
            postalCode = "0150",
            city = "Oslo",
            notes = "Direct customer of the invoicing partner",
        })).Content.ReadFromJsonAsync<EndClientDto>();

        Assert.Equal("Northern Utility", created!.Name);
        Assert.Equal("NO", created.Country); // normalised to upper case
        Assert.Equal("Oslo", created.City);

        var list = await admin.GetFromJsonAsync<EndClientDto[]>("/api/endclients");
        var row = Assert.Single(list!, e => e.Id == created.Id);
        Assert.Equal("Fjordveien 5", row.AddressLine);
        Assert.Equal("0150", row.PostalCode);
    }

    [Fact]
    public async Task Create_AsManager_IsForbidden()
    {
        var manager = factory.CreateUserClient(DevSeed.ManagerId);
        var res = await manager.PostAsJsonAsync("/api/endclients", new { name = "Nope" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
