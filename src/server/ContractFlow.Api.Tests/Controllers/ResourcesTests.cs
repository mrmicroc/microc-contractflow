using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Tests.Controllers;

public class ResourcesTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record ResourceDto(Guid Id, string DisplayName, string Type, bool IsActive, Guid? UserId);
    public record LeaveDto(Guid Id, string From, string To, string Type);
    public record ResourceDetail(Guid Id, string DisplayName, LeaveDto[] Leave);
    public record AgreementDto(Guid Id, Guid? ContractId, int Version, string EffectiveFrom,
        string Currency, decimal? HourlyRate, decimal? TravelTimePercent, string PerDiem,
        Guid? WorkPackageId, string? WorkPackageName);

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

    /// <summary>Creates a work package under a fresh project's contract.</summary>
    private async Task<(Guid contractId, Guid wpId)> ContractWithPackageAsync(
        HttpClient admin, string tag)
    {
        var ids = await Scaffold.CommercialGraphAsync(admin, tag);
        var wp = await admin.PostAsJsonAsync("/api/work-packages",
            new { projectId = ids.ProjectId, name = $"Pkg {tag}" });
        wp.EnsureSuccessStatusCode();
        var wpId = (await wp.Content.ReadFromJsonAsync<Scaffold.IdRef>())!.Id;
        return (ids.ContractId, wpId);
    }

    [Fact]
    public async Task Agreements_WorkPackageScope_Requires_OwningContract()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var resource = await CreateResourceAsync(admin);
        var (contractId, wpId) = await ContractWithPackageAsync(admin, "wp-scope");
        var other = await ContractWithPackageAsync(admin, "wp-other");

        // Package scope without its contract → 400.
        var noContract = await admin.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/agreements",
            new { workPackageId = wpId, effectiveFrom = "2026-01-01", currency = "SEK" });
        Assert.Equal(HttpStatusCode.BadRequest, noContract.StatusCode);

        // Package under a different contract → 400.
        var mismatch = await admin.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/agreements",
            new
            {
                contractId = other.contractId, workPackageId = wpId,
                effectiveFrom = "2026-01-01", currency = "SEK",
            });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        // Unknown package → 400.
        var unknown = await admin.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/agreements",
            new
            {
                contractId, workPackageId = Guid.NewGuid(),
                effectiveFrom = "2026-01-01", currency = "SEK",
            });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        // Consistent scope → accepted with the package names carried back.
        var ok = await admin.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/agreements",
            new
            {
                contractId, workPackageId = wpId,
                effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 750m,
            });
        ok.EnsureSuccessStatusCode();
        var dto = await ok.Content.ReadFromJsonAsync<AgreementDto>();
        Assert.Equal(wpId, dto!.WorkPackageId);
        Assert.Equal("Pkg wp-scope", dto.WorkPackageName);
    }

    [Fact]
    public async Task Agreements_WorkPackageScope_Versions_Separately()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var resource = await CreateResourceAsync(admin);
        var (contractId, wpId) = await ContractWithPackageAsync(admin, "wp-ver");

        // Contract scope already at v2 → the package scope still starts at v1.
        await admin.PostAsJsonAsync($"/api/resources/{resource.Id}/agreements",
            new { contractId, effectiveFrom = "2026-01-01", currency = "SEK" });
        await admin.PostAsJsonAsync($"/api/resources/{resource.Id}/agreements",
            new { contractId, effectiveFrom = "2026-06-01", currency = "SEK" });
        var scoped = await (await admin.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/agreements",
            new
            {
                contractId, workPackageId = wpId,
                effectiveFrom = "2026-01-01", currency = "SEK",
            })).Content.ReadFromJsonAsync<AgreementDto>();
        Assert.Equal(1, scoped!.Version);
    }

    [Fact]
    public async Task ContractWorkPackages_List_Returns_Project_Labelled_Rows()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var (contractId, wpId) = await ContractWithPackageAsync(admin, "wp-list");

        var rows = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/contracts/{contractId}/work-packages");
        var row = Assert.Single(rows.EnumerateArray());
        Assert.Equal(wpId, row.GetProperty("id").GetGuid());
        Assert.Equal("Pkg wp-list", row.GetProperty("name").GetString());
        Assert.Contains("Project wp-list", row.GetProperty("projectName").GetString()!);
    }

    [Fact]
    public async Task Agreements_WorkPackageScope_Wins_In_Statement()
    {
        var admin = factory.CreateUserClient(DevSeed.AdminId);
        var consultant = factory.CreateUserClient(DevSeed.ResourceId);
        var ids = await Scaffold.CommercialGraphAsync(admin, "wp-stmt");
        var wpRes = await admin.PostAsJsonAsync("/api/work-packages",
            new { projectId = ids.ProjectId, name = "Priced Pkg" });
        wpRes.EnsureSuccessStatusCode();
        var wpId = (await wpRes.Content.ReadFromJsonAsync<Scaffold.IdRef>())!.Id;

        var linked = TimeReportingTests.LinkedId;
        await factory.SeedAsync(async db =>
        {
            if (!await db.Resources.AnyAsync(r => r.Id == linked))
            {
                db.Resources.Add(new Resource
                {
                    Id = linked, DisplayName = "Linked Tester",
                    Type = ResourceType.Subcontractor,
                    UserId = DevSeed.ResourceId,
                });
                await db.SaveChangesAsync();
            }
        });

        // Default 600 + package-scoped 750.
        (await admin.PostAsJsonAsync($"/api/resources/{linked}/agreements",
            new { effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 600m }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/resources/{linked}/agreements",
            new
            {
                contractId = ids.ContractId, workPackageId = wpId,
                effectiveFrom = "2026-01-01", currency = "SEK", hourlyRate = 750m,
            })).EnsureSuccessStatusCode();

        // Monday on the package (750 expected), Tuesday unpackaged (600).
        foreach (var (date, wp) in new[] { ("2028-06-05", wpId), ("2028-06-06", (Guid?)null) })
        {
            var entry = await consultant.PostAsJsonAsync("/api/time-entries", new
            {
                projectId = ids.ProjectId, date, category = "work",
                actualHours = 8m, workPackageId = wp,
            });
            entry.EnsureSuccessStatusCode();
        }
        var submit = await consultant.PostAsJsonAsync("/api/timesheets/submit",
            new { year = 2028, isoWeek = 23 });
        submit.EnsureSuccessStatusCode();
        var sheetId = (await submit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sheet").GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/timesheets/{sheetId}/approve", null))
            .EnsureSuccessStatusCode();

        var gen = await admin.PostAsJsonAsync("/api/statements/generate",
            new { resourceId = linked, year = 2028, month = 6 });
        gen.EnsureSuccessStatusCode();
        var stId = (await gen.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/statements/{stId}");

        var hoursLines = detail.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("kind").GetString() == "hours").ToList();
        Assert.Equal(2, hoursLines.Count);
        Assert.Equal(750m, hoursLines.Single(l =>
            l.GetProperty("date").GetString()!.StartsWith("2028-06-05"))
            .GetProperty("unitPrice").GetDecimal());
        Assert.Equal(600m, hoursLines.Single(l =>
            l.GetProperty("date").GetString()!.StartsWith("2028-06-06"))
            .GetProperty("unitPrice").GetDecimal());
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
