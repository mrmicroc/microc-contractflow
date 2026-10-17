using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/work-packages")]
public class WorkPackagesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List([FromQuery] Guid? projectId, CancellationToken ct)
    {
        var query = db.WorkPackages.AsQueryable();
        if (projectId.HasValue)
            query = query.Where(l => l.ProjectId == projectId.Value);

        var workPackages = await query
            .Include(l => l.InfoItems)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);
        return Ok(workPackages.Select(ToDto));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(WorkPackageRequest req, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });

        var workPackage = new WorkPackage
        {
            ProjectId = req.ProjectId,
            Name = req.Name.Trim(),
            Notes = req.Notes,
            InfoItems = (req.InfoItems ?? []).Select(i => new WorkPackageInfo
            {
                Type = i.Type.Trim(), Description = i.Description.Trim(),
            }).ToList(),
        };
        db.WorkPackages.Add(workPackage);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(workPackage));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, WorkPackageRequest req, CancellationToken ct)
    {
        var workPackage = await db.WorkPackages
            .Include(w => w.InfoItems)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workPackage is null) return NotFound();
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });

        workPackage.ProjectId = req.ProjectId;
        workPackage.Name = req.Name.Trim();
        workPackage.Notes = req.Notes;
        if (req.InfoItems is not null)
        {
            db.WorkPackageInfos.RemoveRange(workPackage.InfoItems);
            workPackage.InfoItems = req.InfoItems.Select(i => new WorkPackageInfo
            {
                Type = i.Type.Trim(), Description = i.Description.Trim(),
            }).ToList();
        }
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(workPackage));
    }

    private static WorkPackageDto ToDto(WorkPackage w) => new(
        w.Id, w.ProjectId, w.Name, w.Notes,
        w.InfoItems.Select(i => new WorkPackageInfoDto(i.Id, i.Type, i.Description)).ToList());

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var workPackage = await db.WorkPackages.FindAsync([id], ct);
        if (workPackage is null) return NotFound();

        db.WorkPackages.Remove(workPackage);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Versioned package-rate overrides ────────────────────────────────────────

    /// <summary>All rate-override versions for a workPackage, newest first —
    /// both package-wide rows and resource-specific matrix cells.</summary>
    [HttpGet("{id:guid}/rates")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> ListRates(Guid id, CancellationToken ct)
    {
        if (!await db.WorkPackages.AnyAsync(l => l.Id == id, ct)) return NotFound();
        var rates = await db.WorkPackageRates
            .Include(r => r.Resource)
            .Where(r => r.WorkPackageId == id)
            .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.CreatedUtc)
            .ToListAsync(ct);
        return Ok(rates.Select(ToRateDto));
    }

    /// <summary>Adds a new override version — fields left null inherit the
    /// contract terms; set fields win when bookings target this workPackage.
    /// ResourceId scopes the row to one project member (a matrix cell).</summary>
    [HttpPost("{id:guid}/rates")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> CreateRate(Guid id, WorkPackageRateRequest req, CancellationToken ct)
    {
        var wp = await db.WorkPackages.FindAsync([id], ct);
        if (wp is null) return NotFound();
        if (req.ResourceId is { } resId && !await db.ProjectResources.AnyAsync(
                m => m.ProjectId == wp.ProjectId && m.ResourceId == resId, ct))
            return BadRequest(new { error = "Resource is not added to this project." });
        if (await db.WorkPackageRates.AnyAsync(r => r.WorkPackageId == id
                && r.ResourceId == req.ResourceId && r.EffectiveFrom == req.EffectiveFrom, ct))
            return Conflict(new { error = "A version already exists for this scope and date." });

        var rate = new WorkPackageRate
        {
            WorkPackageId = id,
            ResourceId = req.ResourceId,
            EffectiveFrom = req.EffectiveFrom,
            HourlyRate = req.HourlyRate, SaturdayRate = req.SaturdayRate,
            SundayRate = req.SundayRate, HolidayRate = req.HolidayRate,
            DayRate = req.DayRate, SaturdayDayRate = req.SaturdayDayRate,
            SundayDayRate = req.SundayDayRate, HolidayDayRate = req.HolidayDayRate,
            HalfDayRate = req.HalfDayRate, HalfDayMaxHours = req.HalfDayMaxHours,
            MobilisationFee = req.MobilisationFee, Notes = req.Notes,
        };
        db.WorkPackageRates.Add(rate);
        await db.SaveChangesAsync(ct);
        await db.Entry(rate).Reference(r => r.Resource).LoadAsync(ct);
        return Ok(ToRateDto(rate));
    }

    /// <summary>Clears the review flag on an inherited matrix cell — the admin
    /// confirms the resource's rates on this package match the intent.</summary>
    [HttpPost("{id:guid}/rates/{rateId:guid}/review")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> MarkReviewed(Guid id, Guid rateId, CancellationToken ct)
    {
        var rate = await db.WorkPackageRates.Include(r => r.Resource)
            .FirstOrDefaultAsync(r => r.Id == rateId && r.WorkPackageId == id, ct);
        if (rate is null) return NotFound();
        if (rate.ResourceId is null)
            return BadRequest(new { error = "Only resource-specific rates can be reviewed." });
        rate.NeedsReview = false;
        await db.SaveChangesAsync(ct);
        return Ok(ToRateDto(rate));
    }

    /// <summary>Removes one override version (e.g. entered by mistake).</summary>
    [HttpDelete("{id:guid}/rates/{rateId:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> DeleteRate(Guid id, Guid rateId, CancellationToken ct)
    {
        var rate = await db.WorkPackageRates
            .FirstOrDefaultAsync(r => r.Id == rateId && r.WorkPackageId == id, ct);
        if (rate is null) return NotFound();
        db.WorkPackageRates.Remove(rate);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static WorkPackageRateDto ToRateDto(WorkPackageRate r) => new(
        r.Id, r.WorkPackageId, r.EffectiveFrom.ToString("yyyy-MM-dd"),
        r.ResourceId, r.Resource?.DisplayName, r.NeedsReview,
        r.HourlyRate, r.SaturdayRate, r.SundayRate, r.HolidayRate,
        r.DayRate, r.SaturdayDayRate, r.SundayDayRate, r.HolidayDayRate,
        r.HalfDayRate, r.HalfDayMaxHours, r.MobilisationFee, r.Notes);
}
