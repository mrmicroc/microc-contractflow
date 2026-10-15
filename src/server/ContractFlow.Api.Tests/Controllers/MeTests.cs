using System.Net;
using System.Net.Http.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Tests.Infrastructure;

namespace ContractFlow.Api.Tests.Controllers;

public class MeTests(ContractFlowWebApplicationFactory factory)
    : IClassFixture<ContractFlowWebApplicationFactory>
{
    public record MeDto(Guid Id, string DisplayName, string? Email, string Role);

    [Fact]
    public async Task GetMe_ReturnsUserSelectedByDevHeader()
    {
        var client = factory.CreateUserClient(DevSeed.ResourceId);

        var me = await client.GetFromJsonAsync<MeDto>("/api/me");

        Assert.NotNull(me);
        Assert.Equal(DevSeed.ResourceId, me.Id);
        Assert.Equal("Dev Consultant", me.DisplayName);
        Assert.Equal("Resource", me.Role);
    }

    [Fact]
    public async Task GetMe_WithoutHeader_FallsBackToAdmin()
    {
        var client = factory.CreateClient();

        var me = await client.GetFromJsonAsync<MeDto>("/api/me");

        Assert.NotNull(me);
        Assert.Equal(DevSeed.AdminId, me.Id);
        Assert.Equal("Administrator", me.Role);
    }

    [Fact]
    public async Task ListUsers_ReturnsSeededDevUsers()
    {
        var client = factory.CreateClient();

        var users = await client.GetFromJsonAsync<MeDto[]>("/api/users");

        Assert.NotNull(users);
        Assert.Equal(4, users.Length);
        Assert.Contains(users, u => u.Role == "ProjectManager");
        Assert.Contains(users, u => u.Role == "Finance");
    }
}
