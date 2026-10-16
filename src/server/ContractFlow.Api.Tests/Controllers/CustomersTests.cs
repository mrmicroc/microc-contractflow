using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class CustomersTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record CustomerSummary(Guid Id, string Name, string? ExternalReference,
        string? VatNumber, string? Country, bool IsActive, int ContractCount);
    public record ContractRef(Guid Id, string Name);
    public record ContactDto(string Name, string? Role, string? Email, string? Phone);
    public record CustomerDetail(Guid Id, string Name, string? ExternalReference,
        string? VatNumber, string? AddressLine, string? PostalCode, string? City,
        string? Country, ContactDto[] Contacts, string? Notes, bool IsActive,
        ContractRef[] Contracts);

    [Fact]
    public async Task Create_ThenGet_RoundTrips()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);

        var created = await client.PostAsJsonAsync("/api/customers",
            new { name = "Example Org", externalReference = "EX-9", notes = "n" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var summary = await created.Content.ReadFromJsonAsync<CustomerSummary>();
        Assert.NotNull(summary);
        Assert.Equal("Example Org", summary.Name);
        Assert.Equal(0, summary.ContractCount);

        var detail = await client.GetFromJsonAsync<CustomerDetail>($"/api/customers/{summary.Id}");
        Assert.NotNull(detail);
        Assert.Equal("EX-9", detail.ExternalReference);
        Assert.Empty(detail.Contracts);
    }

    [Fact]
    public async Task Update_ChangesFieldsAndActiveFlag()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var created = await (await client.PostAsJsonAsync("/api/customers",
            new { name = "Before Rename" })).Content.ReadFromJsonAsync<CustomerSummary>();
        Assert.NotNull(created);

        var updated = await client.PutAsJsonAsync($"/api/customers/{created.Id}",
            new { name = "After Rename", isActive = false });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var summary = await updated.Content.ReadFromJsonAsync<CustomerSummary>();
        Assert.Equal("After Rename", summary!.Name);
        Assert.False(summary.IsActive);
    }

    [Fact]
    public async Task Create_WithoutName_Returns400()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var res = await client.PostAsJsonAsync("/api/customers", new { name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Create_VatAddressContacts_RoundTrip()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var created = await (await client.PostAsJsonAsync("/api/customers", new
        {
            name = "Detail Org",
            vatNumber = "SE556677889901",
            addressLine = "Industrigatan 12",
            postalCode = "411 03",
            city = "Göteborg",
            country = "se",
            contacts = new[]
            {
                new { name = "Anna Order", role = "Orderer", email = "a.order@example.com", phone = "070-1" },
                new { name = "Bill Invoice", role = "Invoice recipient", email = "b.invoice@example.com", phone = (string?)null },
            },
        })).Content.ReadFromJsonAsync<CustomerSummary>();
        Assert.Equal("SE556677889901", created!.VatNumber);
        Assert.Equal("SE", created.Country); // normalised to upper case

        var detail = await client.GetFromJsonAsync<CustomerDetail>($"/api/customers/{created.Id}");
        Assert.Equal("Industrigatan 12", detail!.AddressLine);
        Assert.Equal("411 03", detail.PostalCode);
        Assert.Equal("Göteborg", detail.City);
        Assert.Equal(2, detail.Contacts.Length);
        Assert.Equal("a.order@example.com", detail.Contacts[0].Email);
        Assert.Equal("Invoice recipient", detail.Contacts[1].Role);

        // Contacts are fully replaced on update (row-local JSON list).
        await client.PutAsJsonAsync($"/api/customers/{created.Id}", new
        {
            name = "Detail Org", vatNumber = "SE556677889901",
            contacts = new[] { new { name = "Only One", role = "Orderer", email = (string?)null, phone = (string?)null } },
        });
        var after = await client.GetFromJsonAsync<CustomerDetail>($"/api/customers/{created.Id}");
        Assert.Single(after!.Contacts);
        Assert.Equal("Only One", after.Contacts[0].Name);
    }

    [Fact]
    public async Task Meta_Calendars_ListsSupportedCountries()
    {
        var client = factory.CreateUserClient(DevSeed.ManagerId);
        var calendars = await client.GetFromJsonAsync<JsonElement[]>("/api/meta/calendars");
        var codes = calendars!.Select(c => c.GetProperty("code").GetString()).ToList();
        Assert.Contains("SE", codes);
        Assert.Contains("NO", codes);
        Assert.Contains("NONE", codes);
    }

    [Fact]
    public async Task List_AsManager_IsAllowed()
    {
        var client = factory.CreateUserClient(DevSeed.ManagerId);
        var customers = await client.GetFromJsonAsync<CustomerSummary[]>("/api/customers");
        Assert.NotNull(customers);
    }

    [Fact]
    public async Task Create_AsManager_IsForbidden()
    {
        var client = factory.CreateUserClient(DevSeed.ManagerId);
        var res = await client.PostAsJsonAsync("/api/customers", new { name = "Nope Org" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task List_AsResource_IsForbidden()
    {
        var client = factory.CreateUserClient(DevSeed.ResourceId);
        var res = await client.GetAsync("/api/customers");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
