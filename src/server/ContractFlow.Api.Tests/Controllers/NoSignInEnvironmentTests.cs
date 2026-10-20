using System.Net;
using ContractFlow.Api.Data;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContractFlow.Api.Tests.Controllers;

/// <summary>Hosts the API as a deployed (Production) environment: the dev
/// identity header and its Administrator fallback must not exist there.</summary>
public sealed class NoSignInFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        // No SQL Server registration / startup migration in the test host.
        builder.UseSetting("ConnectionStrings:DefaultConnection", "");
        builder.ConfigureServices(services =>
        {
            _connection.Open();
            services.AddDbContext<AppDbContext>(opts => opts.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

public class NoSignInEnvironmentTests(NoSignInFactory factory) : IClassFixture<NoSignInFactory>
{
    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/customers")]
    [InlineData("/api/time-entries")]
    public async Task GuardedEndpoints_Return401_EvenWithDevHeader(string url)
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);

        var forged = factory.CreateClient();
        forged.DefaultRequestHeaders.Add(DevCurrentUser.UserIdHeader, DevSeed.AdminId.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task DevOnlyEndpoints_AreAbsent()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/startup-error")).StatusCode);
    }
}
