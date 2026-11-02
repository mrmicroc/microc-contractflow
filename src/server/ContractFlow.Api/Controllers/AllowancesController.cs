using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>Per-diem allowance claims (design §6.2) — the agreement's policy
/// (none/statutory/custom) governs whether claims are honoured at statement
/// time; the claim rows record day + amount + currency.</summary>
[ApiController]
[Route("api/allowances")]
public class AllowancesController(AppDbContext db, ICurrentUser currentUser, PadService pad)
    : ReportingControllerBase(db, currentUser)
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? resourceId, CancellationToken ct)
    {
        var scoped = await ScopedResourceIdAsync(ct);
        var query = Db.AllowanceClaims
            .Include(x => x.Resource).Include(x => x.Project).AsQueryable();
        if (scoped.HasValue) query = query.Where(x => x.ResourceId == scoped.Value);
        else if (resourceId.HasValue) query = query.Where(x => x.ResourceId == resourceId.Value);
        if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Date <= to.Value);
        return Ok((await query.OrderBy(x => x.Date).ToListAsync(ct)).Select(ToDto));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Create(AllowanceClaimRequest req, CancellationToken ct)
    {
        var (resourceId, error) = await WritableResourceAsync(req.ResourceId, ct);
        if (error is not null) return error;
        if (await ValidateAsync(req, resourceId!.Value, null, ct) is { } invalid) return invalid;

        var me = await Me(ct);
        var row = new AllowanceClaim
        {
            ResourceId = resourceId!.Value,
            ProjectId = req.ProjectId,
            Date = req.Date,
            Amount = req.Amount,
            Currency = req.Currency.ToUpperInvariant(),
            Notes = req.Notes,
            CreatedByUserId = me?.Id,
        };
        Db.AllowanceClaims.Add(row);
        await Db.SaveChangesAsync(ct);
        return Created($"/api/allowances/{row.Id}", await DtoAsync(row.Id, ct));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Update(Guid id, AllowanceClaimRequest req, CancellationToken ct)
    {
        var row = await Db.AllowanceClaims.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        if (!await OwnsAsync(row.ResourceId, ct)) return ForbidResult();
        if (await LockedAsync(row.TimesheetId, ct)) return LockedError();
        if (await ClosedAsync(row.ProjectId, row.Date, ct)) return MonthClosedError();
        if (await ValidateAsync(req, row.ResourceId, row.Id, ct) is { } invalid) return invalid;

        row.ProjectId = req.ProjectId;
        row.Date = req.Date;
        row.Amount = req.Amount;
        row.Currency = req.Currency.ToUpperInvariant();
        row.Notes = req.Notes;
        await Db.SaveChangesAsync(ct);
        return Ok(await DtoAsync(id, ct));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var row = await Db.AllowanceClaims.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        if (!await OwnsAsync(row.ResourceId, ct)) return ForbidResult();
        if (await LockedAsync(row.TimesheetId, ct)) return LockedError();
        if (await ClosedAsync(row.ProjectId, row.Date, ct)) return MonthClosedError();
        Db.AllowanceClaims.Remove(row);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<IActionResult?> ValidateAsync(
        AllowanceClaimRequest req, Guid resourceId, Guid? excludeId, CancellationToken ct)
    {
        if (req.ProjectId.HasValue)
        {
            if (!await Db.Projects.AnyAsync(p => p.Id == req.ProjectId && p.IsActive, ct))
                return BadRequest(new { error = "Unknown or inactive project." });
            if (await ClosedAsync(req.ProjectId, req.Date, ct)) return MonthClosedError();
        }
        if (await WeekLockedAsync(resourceId, req.Date, ct)) return WeekLockedError();
        // One per-diem claim per resource and day, whatever state the existing
        // claim is in — an approved claim plus a fresh draft would pay twice.
        if (await Db.AllowanceClaims.AnyAsync(
                x => x.ResourceId == resourceId && x.Date == req.Date && x.Id != excludeId, ct))
            return Conflict(new { error = "duplicate", message = "A claim already exists for that day." });
        return null;
    }

    private async Task<bool> ClosedAsync(Guid? projectId, DateOnly date, CancellationToken ct) =>
        projectId.HasValue && await pad.MonthIsClosedAsync(projectId.Value, date.Year, date.Month, ct);

    private async Task<AllowanceClaimDto> DtoAsync(Guid id, CancellationToken ct) =>
        ToDto(await Db.AllowanceClaims
            .Include(x => x.Resource).Include(x => x.Project)
            .FirstAsync(x => x.Id == id, ct));

    private static AllowanceClaimDto ToDto(AllowanceClaim x) => new(
        x.Id, x.ResourceId, x.Resource.DisplayName, x.ProjectId, x.Project?.Name,
        x.Date, x.Amount, x.Currency, x.Notes, x.TimesheetId);
}
