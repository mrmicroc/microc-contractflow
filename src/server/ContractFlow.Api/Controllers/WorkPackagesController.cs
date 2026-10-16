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
            .OrderBy(l => l.Name)
            .Select(l => new WorkPackageDto(l.Id, l.ProjectId, l.Name, l.Address, l.Notes))
            .ToListAsync(ct);
        return Ok(workPackages);
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
            Address = req.Address,
            Notes = req.Notes,
        };
        db.WorkPackages.Add(workPackage);
        await db.SaveChangesAsync(ct);
        return Ok(new WorkPackageDto(workPackage.Id, workPackage.ProjectId, workPackage.Name,
            workPackage.Address, workPackage.Notes));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, WorkPackageRequest req, CancellationToken ct)
    {
        var workPackage = await db.WorkPackages.FindAsync([id], ct);
        if (workPackage is null) return NotFound();
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });

        workPackage.ProjectId = req.ProjectId;
        workPackage.Name = req.Name.Trim();
        workPackage.Address = req.Address;
        workPackage.Notes = req.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(new WorkPackageDto(workPackage.Id, workPackage.ProjectId, workPackage.Name,
            workPackage.Address, workPackage.Notes));
    }

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

    /// <summary>All rate-override versions for a workPackage, newest first.</summary>
    [HttpGet("{id:guid}/rates")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> ListRates(Guid id, CancellationToken ct)
    {
        if (!await db.WorkPackages.AnyAsync(l => l.Id == id, ct)) return NotFound();
        var rates = await db.WorkPackageRates
            .Where(r => r.WorkPackageId == id)
            .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.CreatedUtc)
            .ToListAsync(ct);
        return Ok(rates.Select(ToRateDto));
    }

    /// <summary>Adds a new override version — fields left null inherit the
    /// contract terms; set fields win when bookings target this workPackage.</summary>
    [HttpPost("{id:guid}/rates")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> CreateRate(Guid id, WorkPackageRateRequest req, CancellationToken ct)
    {
        if (!await db.WorkPackages.AnyAsync(l => l.Id == id, ct)) return NotFound();
        var rate = new WorkPackageRate
        {
            WorkPackageId = id,
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
        r.HourlyRate, r.SaturdayRate, r.SundayRate, r.HolidayRate,
        r.DayRate, r.SaturdayDayRate, r.SundayDayRate, r.HolidayDayRate,
        r.HalfDayRate, r.HalfDayMaxHours, r.MobilisationFee, r.Notes);
}
