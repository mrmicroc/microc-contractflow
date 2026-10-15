using System.Net;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class ProjectsTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record IdRef(Guid Id);
    public record ProjectDto(Guid Id, Guid ContractId, string Name, string? Code, bool IsActive);
    public record LocationDto(Guid Id, string Name, string? Address);
    public record QuantityDto(Guid Id, string Label, decimal Quantity, string Unit);
    public record ProjectDetail(Guid Id, string Name, string ContractName, string CustomerName,
        LocationDto[] Locations, QuantityDto[] EstimatedQuantities);

    private async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var customer = await (await client.PostAsJsonAsync("/api/customers",
            new { name = "Project Host Org" })).Content.ReadFromJsonAsync<IdRef>();
        var contract = await (await client.PostAsJsonAsync("/api/contracts", new
        {
            customerId = customer!.Id, name = "Host Contract",
            model = "hourly", startDate = "2026-01-01",
        })).Content.ReadFromJsonAsync<IdRef>();
        var res = await client.PostAsJsonAsync("/api/projects", new
        {
            contractId = contract!.Id, name = "North Works", code = "NW-1",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    [Fact]
    public async Task Create_ThenGet_IncludesContractAndCustomerNames()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var project = await CreateProjectAsync(client);

        var detail = await client.GetFromJsonAsync<ProjectDetail>($"/api/projects/{project.Id}");
        Assert.NotNull(detail);
        Assert.Equal("North Works", detail.Name);
        Assert.Equal("Host Contract", detail.ContractName);
        Assert.Equal("Project Host Org", detail.CustomerName);
        Assert.Empty(detail.Locations);
        Assert.Empty(detail.EstimatedQuantities);
    }

    [Fact]
    public async Task Locations_AddUpdateDelete_FlowWorks()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var project = await CreateProjectAsync(client);

        var created = await client.PostAsJsonAsync("/api/locations",
            new { projectId = project.Id, name = "Site Alpha", address = "1 Road" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var location = await created.Content.ReadFromJsonAsync<LocationDto>();
        Assert.NotNull(location);

        await client.PutAsJsonAsync($"/api/locations/{location.Id}",
            new { projectId = project.Id, name = "Site Alpha West" });

        var detail = await client.GetFromJsonAsync<ProjectDetail>($"/api/projects/{project.Id}");
        Assert.Equal("Site Alpha West", detail!.Locations.Single().Name);

        var del = await client.DeleteAsync($"/api/locations/{location.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Quantities_AddUpdateDelete_FlowWorks()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var project = await CreateProjectAsync(client);

        var created = await client.PostAsJsonAsync("/api/quantities",
            new { projectId = project.Id, label = "Inspection days", quantity = 120m, unit = "days" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var quantity = await created.Content.ReadFromJsonAsync<QuantityDto>();
        Assert.Equal(120m, quantity!.Quantity);

        await client.PutAsJsonAsync($"/api/quantities/{quantity.Id}",
            new { projectId = project.Id, label = "Inspection days", quantity = 140m, unit = "days" });

        var detail = await client.GetFromJsonAsync<ProjectDetail>($"/api/projects/{project.Id}");
        Assert.Equal(140m, detail!.EstimatedQuantities.Single().Quantity);

        var del = await client.DeleteAsync($"/api/quantities/{quantity.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Create_WithUnknownContract_Returns400()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var res = await client.PostAsJsonAsync("/api/projects",
            new { contractId = Guid.NewGuid(), name = "Ghost" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Create_AsFinance_IsForbidden_List_IsAllowed()
    {
        var client = factory.CreateUserClient(DevSeed.FinanceId);
        var res = await client.PostAsJsonAsync("/api/projects",
            new { contractId = Guid.NewGuid(), name = "Nope" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var list = await client.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }
}
