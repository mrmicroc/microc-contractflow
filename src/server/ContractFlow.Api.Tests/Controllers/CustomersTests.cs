using System.Net;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class CustomersTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record CustomerSummary(Guid Id, string Name, string? ExternalReference,
        bool IsActive, int ContractCount);
    public record ContractRef(Guid Id, string Name);
    public record CustomerDetail(Guid Id, string Name, string? ExternalReference,
        string? Notes, bool IsActive, ContractRef[] Contracts);

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
