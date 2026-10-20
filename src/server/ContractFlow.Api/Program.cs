using ContractFlow.Api.Data;
using ContractFlow.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
// LocalDB copy for development (see AGENTS.md); Azure SQL in production.
// Tests replace this with SQLite in-memory via the WebApplicationFactory.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrWhiteSpace(connectionString) && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<AppDbContext>(opts => opts.UseSqlServer(connectionString));
}

builder.Services.AddHttpContextAccessor();
// The dev header identity exists only locally and in tests; any other
// environment has no sign-in until Entra arrives (§9.1) → 401 everywhere.
var devIdentity = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
if (devIdentity)
    builder.Services.AddScoped<ICurrentUser, DevCurrentUser>();
else
    builder.Services.AddScoped<ICurrentUser, NoSignInCurrentUser>();
builder.Services.AddScoped<PadService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddSingleton<IFileStorage>(sp => new LocalDiskFileStorage(
    builder.Configuration["FileStorage:Root"],
    sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(
                System.Text.Json.JsonNamingPolicy.CamelCase));
    });
builder.Services.AddOpenApi();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var localCors = !builder.Environment.IsProduction() && corsOrigins.Length > 0;
if (localCors)
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Return the exception message so the client can display it during
// development; elsewhere only a generic text (details go to the log).
app.UseExceptionHandler(c => c.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (ex is not null)
        ctx.RequestServices.GetRequiredService<ILogger<Program>>()
            .LogError(ex, "Unhandled exception on {Path}", ctx.Request.Path);
    ctx.Response.StatusCode = 500;
    ctx.Response.ContentType = "text/plain";
    await ctx.Response.WriteAsync(devIdentity
        ? ex?.Message ?? "Internal server error"
        : "Internal server error");
}));

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});

// ── Auto-migrate + seed on startup ────────────────────────────────────────────
string? startupError = null;
if (!string.IsNullOrWhiteSpace(connectionString) && !app.Environment.IsEnvironment("Testing"))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        await DevSeed.SeedIfEmptyAsync(db);
        await DevSeed.SeedCommercialExampleAsync(db);
    }
    catch (Exception ex)
    {
        startupError = ex.ToString();
        Console.Error.WriteLine($"[STARTUP ERROR] {startupError}");
    }
}
// Full startup diagnostics (server names, SQL details) only where the dev
// identity is on — never anonymously in a deployed environment.
if (devIdentity)
    app.MapGet("/api/startup-error", () => startupError is null
        ? Results.Ok("No startup error.")
        : Results.Problem(startupError, title: "Startup error", statusCode: 500));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (localCors) app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
