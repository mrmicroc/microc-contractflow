using ContractFlow.Api.Data;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ContractFlow.Api.Tests.Infrastructure;

/// <summary>
/// Spins up the real ASP.NET pipeline with a SQLite in-memory database
/// (unique per factory instance → per test class). Identity is resolved through
/// the same X-Dev-User-Id header the client's dev role-switcher uses.
/// </summary>
public sealed class ContractFlowWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public ContractFlowWebApplicationFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var existing = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>))
                .ToList();
            foreach (var d in existing) services.Remove(d);

            services.AddDbContext<AppDbContext>(opts =>
                opts.UseSqlite(_connection).EnableServiceProviderCaching(false));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        DevSeed.SeedIfEmptyAsync(db).GetAwaiter().GetResult();
        return host;
    }

    /// <summary>
    /// Creates an HttpClient whose every request carries the given user id in
    /// X-Dev-User-Id — the same mechanism the dev role-switcher uses.
    /// </summary>
    public HttpClient CreateUserClient(Guid userId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(DevCurrentUser.UserIdHeader, userId.ToString());
        return client;
    }

    /// <summary>Opens a scope and seeds data via the provided action.</summary>
    public async Task SeedAsync(Func<AppDbContext, Task> seedAction)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await seedAction(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
