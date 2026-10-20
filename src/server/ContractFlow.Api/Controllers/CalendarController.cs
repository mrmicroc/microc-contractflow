using System.Security.Cryptography;
using System.Text;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Personal .ics calendar feeds (design §3). The feed URL itself is the
/// credential — an unguessable 256-bit token, standard for calendar
/// subscriptions — so the .ics endpoint intentionally allows anonymous access.
/// The token management endpoints are role-guarded.
/// </summary>
[ApiController]
[Route("api/calendar")]
public class CalendarController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Returns the feed URL for a resource, creating the token on first use.
    /// Admin/PM may fetch any resource's URL; a Resource user only their own
    /// (resourceId omitted → the caller's linked resource).
    /// </summary>
    [HttpGet("feed-url")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> FeedUrl([FromQuery] Guid? resourceId, CancellationToken ct)
    {
        var resource = await ResolveResourceAsync(resourceId, ct);
        if (resource is null) return ForbidResult();
        if (resource.FeedToken is null)
        {
            resource.FeedToken = NewToken();
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { url = $"/api/calendar/{resource.Id}/{resource.FeedToken}.ics" });
    }

    /// <summary>Mints a new token, invalidating the old URL immediately.</summary>
    [HttpPost("feed-url/rotate")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Rotate([FromQuery] Guid? resourceId, CancellationToken ct)
    {
        var resource = await ResolveResourceAsync(resourceId, ct);
        if (resource is null) return ForbidResult();
        resource.FeedToken = NewToken();
        await db.SaveChangesAsync(ct);
        return Ok(new { url = $"/api/calendar/{resource.Id}/{resource.FeedToken}.ics" });
    }

    /// <summary>
    /// The anonymous feed — token-gated. Only committed Booked Days appear
    /// (confirmed → completed, §7.2); planning items and cancellations don't.
    /// </summary>
    [HttpGet("{resourceId:guid}/{token}.ics")]
    public async Task<IActionResult> Feed(Guid resourceId, string token, CancellationToken ct)
    {
        var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == resourceId, ct);
        if (resource?.FeedToken is null || !TokenMatches(resource.FeedToken, token))
            return NotFound();

        var bookings = await db.Bookings
            .Include(b => b.Project)
            .Include(b => b.WorkPackage)
            .Where(b => b.ResourceId == resourceId && IcsBuilder.FeedStatuses.Contains(b.Status))
            .OrderBy(b => b.Date)
            .ToListAsync(ct);

        return Content(
            IcsBuilder.Feed(resource.DisplayName, bookings),
            "text/calendar; charset=utf-8");
    }

    private async Task<Resource?> ResolveResourceAsync(Guid? resourceId, CancellationToken ct)
    {
        var me = await currentUser.GetAsync(ct);
        if (me is null) return null;

        // Admin/PM can target any resource; a Resource user only their own.
        if (me.Role is AppRole.Administrator or AppRole.ProjectManager)
        {
            return resourceId.HasValue
                ? await db.Resources.FindAsync([resourceId.Value], ct)
                : null;
        }

        var own = await db.Resources.FirstOrDefaultAsync(r => r.UserId == me.Id, ct);
        if (own is null) return null;
        if (resourceId.HasValue && resourceId != own.Id) return null;
        return own;
    }

    private IActionResult ForbidResult() =>
        new ObjectResult(new { error = "Forbidden." }) { StatusCode = 403 };

    /// <summary>256 random bits, hex — URL-safe and unguessable (a GUID
    /// carries only 122 random bits).</summary>
    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>Constant-time comparison — response timing must not leak how
    /// much of a guessed token was right.</summary>
    private static bool TokenMatches(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
