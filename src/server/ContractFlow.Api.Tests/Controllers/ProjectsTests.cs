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
    public record InfoDto(Guid Id, string Type, string Description);
    public record WorkPackageDto(Guid Id, string Name, string? Notes, InfoDto[] InfoItems);
    public record QuantityDto(Guid Id, string Label, decimal Quantity, string Unit);
    public record ProjectDetail(Guid Id, string Name, string ContractName, string CustomerName,
        WorkPackageDto[] WorkPackages, QuantityDto[] EstimatedQuantities);

    private async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var customer = await (await client.PostAsJsonAsync("/api/customers",
            new { name = "Project Host Org" })).Content.ReadFromJsonAsync<IdRef>();
        var contract = await (await client.PostAsJsonAsync("/api/contracts", new
        {
            customerId = customer!.Id, name = "Host Contract",
            startDate = "2026-01-01",
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
        Assert.Empty(detail.WorkPackages);
        Assert.Empty(detail.EstimatedQuantities);
    }

    [Fact]
    public async Task WorkPackages_AddUpdateDelete_FlowWorks()
    {
        var client = factory.CreateUserClient(DevSeed.AdminId);
        var project = await CreateProjectAsync(client);

        var created = await client.PostAsJsonAsync("/api/work-packages",
            new
            {
                projectId = project.Id, name = "Site Alpha",
                infoItems = new[]
                {
                    new { type = "Address", description = "1 Road" },
                    new { type = "Access", description = "Gate code 4521" },
                },
            });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var workPackage = await created.Content.ReadFromJsonAsync<WorkPackageDto>();
        Assert.NotNull(workPackage);
        Assert.Equal(2, workPackage.InfoItems.Length);
        Assert.Equal("Access", workPackage.InfoItems[1].Type);

        // Update replaces the info list wholesale.
        await client.PutAsJsonAsync($"/api/work-packages/{workPackage.Id}",
            new
            {
                projectId = project.Id, name = "Site Alpha West",
                infoItems = new[] { new { type = "Contact", description = "Site office" } },
            });

        var detail = await client.GetFromJsonAsync<ProjectDetail>($"/api/projects/{project.Id}");
        var wp = detail!.WorkPackages.Single();
        Assert.Equal("Site Alpha West", wp.Name);
        var info = Assert.Single(wp.InfoItems);
        Assert.Equal("Contact", info.Type);

        var del = await client.DeleteAsync($"/api/work-packages/{workPackage.Id}");
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
