using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

public record AuditEntryDto(
    Guid Id, DateTime OccurredUtc, Guid? UserId, string? UserDisplayName,
    string EntityType, Guid EntityId, AuditAction Action,
    IReadOnlyList<AuditChange> Changes, string? Reason);

/// <summary>Read-only change history (design §3/§9.3) — newest first. Admin and
/// Finance only: the rows carry rates and commercial terms.</summary>
[ApiController]
[Route("api/audit")]
public class AuditController(AppDbContext db) : ControllerBase
{
    private const int DefaultTake = 200;
    private const int MaxTake = 1000;

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> List(
        [FromQuery] string? entityType, [FromQuery] Guid? entityId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? take, CancellationToken ct)
    {
        var query = db.AuditEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(a => a.EntityType == entityType);
        if (entityId.HasValue) query = query.Where(a => a.EntityId == entityId.Value);
        if (from.HasValue) query = query.Where(a => a.OccurredUtc >= from.Value);
        if (to.HasValue) query = query.Where(a => a.OccurredUtc <= to.Value);

        var rows = await query
            .OrderByDescending(a => a.OccurredUtc)
            .Take(Math.Clamp(take ?? DefaultTake, 1, MaxTake))
            .ToListAsync(ct);
        return Ok(rows.Select(a => new AuditEntryDto(
            a.Id, a.OccurredUtc, a.UserId, a.UserDisplayName, a.EntityType, a.EntityId,
            a.Action, AuditTrail.Parse(a.Changes), a.Reason)));
    }
}
