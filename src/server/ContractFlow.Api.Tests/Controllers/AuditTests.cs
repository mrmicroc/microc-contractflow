using System.Net;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>Audit trail (design §3/§9.3): commercially relevant saves write
/// who/when/old/new rows; the history is readable by Admin + Finance only.</summary>
public class AuditTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record IdRef(Guid Id);
    public record ChangeDto(string Field, string? Old, string? New);
    public record AuditDto(Guid Id, Guid? UserId, string? UserDisplayName, string EntityType,
        Guid EntityId, string Action, ChangeDto[] Changes, string? Reason);

    private HttpClient Admin => factory.CreateUserClient(DevSeed.AdminId);

    private async Task<(Guid CustomerId, Guid ContractId)> ContractAsync(string name)
    {
        var customer = await (await Admin.PostAsJsonAsync("/api/customers",
            new { name = "Audit Host Org" })).Content.ReadFromJsonAsync<IdRef>();
        var res = await Admin.PostAsJsonAsync("/api/contracts", new
        {
            customerId = customer!.Id, name, startDate = "2026-01-01",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (customer.Id, (await res.Content.ReadFromJsonAsync<IdRef>())!.Id);
    }

    private Task<AuditDto[]?> HistoryAsync(HttpClient client, string type, Guid id) =>
        client.GetFromJsonAsync<AuditDto[]>($"/api/audit?entityType={type}&entityId={id}");

    [Fact]
    public async Task ContractUpdate_WritesOldAndNewValues_WithActingUser()
    {
        var (customerId, contractId) = await ContractAsync("Before Name");
        var put = await Admin.PutAsJsonAsync($"/api/contracts/{contractId}", new
        {
            customerId, name = "After Name", startDate = "2026-01-01", isActive = true,
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var rows = await HistoryAsync(Admin, "Contract", contractId);
        Assert.Equal(2, rows!.Length);
        var update = rows[0]; // newest first
        Assert.Equal("updated", update.Action);
        Assert.Equal(DevSeed.AdminId, update.UserId);
        Assert.False(string.IsNullOrEmpty(update.UserDisplayName));
        var change = Assert.Single(update.Changes);
        Assert.Equal("Name", change.Field);
        Assert.Equal("Before Name", change.Old);
        Assert.Equal("After Name", change.New);
        Assert.Equal("created", rows[1].Action);
    }

    [Fact]
    public async Task TermsCreateAndDelete_WriteCreatedAndDeleted()
    {
        var (_, contractId) = await ContractAsync("Terms Audit");
        var terms = await (await Admin.PostAsJsonAsync($"/api/contracts/{contractId}/terms",
            new { effectiveFrom = "2026-01-01", currency = "SEK", holidayCalendar = "SE",
                  hourlyRate = 1000m })).Content.ReadFromJsonAsync<IdRef>();

        var created = Assert.Single((await HistoryAsync(Admin, "ContractTerms", terms!.Id))!);
        Assert.Equal("created", created.Action);
        Assert.Contains(created.Changes, c => c.Field == "HourlyRate" && c.New == "1000");

        var del = await Admin.DeleteAsync($"/api/contracts/{contractId}/terms/{terms.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var rows = await HistoryAsync(Admin, "ContractTerms", terms.Id);
        Assert.Equal(2, rows!.Length);
        Assert.Equal("deleted", rows[0].Action);
        Assert.Contains(rows[0].Changes, c => c.Field == "HourlyRate" && c.Old == "1000");
    }

    [Fact]
    public async Task UnchangedSave_WritesNoUpdateRow()
    {
        var (customerId, contractId) = await ContractAsync("Steady Name");
        await Admin.PutAsJsonAsync($"/api/contracts/{contractId}", new
        {
            customerId, name = "Steady Name", startDate = "2026-01-01", isActive = true,
        });
        var row = Assert.Single((await HistoryAsync(Admin, "Contract", contractId))!);
        Assert.Equal("created", row.Action);
    }

    [Fact]
    public async Task Filters_ByEntityTypeAndTake()
    {
        var (_, contractId) = await ContractAsync("Filter Audit");
        var byType = await Admin.GetFromJsonAsync<AuditDto[]>("/api/audit?entityType=Contract&take=500");
        Assert.All(byType!, r => Assert.Equal("Contract", r.EntityType));
        Assert.Contains(byType!, r => r.EntityId == contractId);

        var one = await Admin.GetFromJsonAsync<AuditDto[]>("/api/audit?take=1");
        Assert.Single(one!);

        var future = await Admin.GetFromJsonAsync<AuditDto[]>(
            $"/api/audit?entityId={contractId}&from=2099-01-01T00:00:00Z");
        Assert.Empty(future!);
    }

    [Theory]
    [InlineData("00000000-0000-4000-8000-000000000001", HttpStatusCode.OK)]        // admin
    [InlineData("00000000-0000-4000-8000-000000000004", HttpStatusCode.OK)]        // finance
    [InlineData("00000000-0000-4000-8000-000000000002", HttpStatusCode.Forbidden)] // PM
    [InlineData("00000000-0000-4000-8000-000000000003", HttpStatusCode.Forbidden)] // resource
    public async Task Read_AllowedForAdminAndFinanceOnly(string userId, HttpStatusCode expected)
    {
        var res = await factory.CreateUserClient(Guid.Parse(userId)).GetAsync("/api/audit");
        Assert.Equal(expected, res.StatusCode);
    }
}
