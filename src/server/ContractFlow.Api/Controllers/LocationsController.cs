using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List([FromQuery] Guid? projectId, CancellationToken ct)
    {
        var query = db.Locations.AsQueryable();
        if (projectId.HasValue)
            query = query.Where(l => l.ProjectId == projectId.Value);

        var locations = await query
            .OrderBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.ProjectId, l.Name, l.Address, l.Notes))
            .ToListAsync(ct);
        return Ok(locations);
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(LocationRequest req, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });

        var location = new Location
        {
            ProjectId = req.ProjectId,
            Name = req.Name.Trim(),
            Address = req.Address,
            Notes = req.Notes,
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        return Ok(new LocationDto(location.Id, location.ProjectId, location.Name,
            location.Address, location.Notes));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, LocationRequest req, CancellationToken ct)
    {
        var location = await db.Locations.FindAsync([id], ct);
        if (location is null) return NotFound();
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });

        location.ProjectId = req.ProjectId;
        location.Name = req.Name.Trim();
        location.Address = req.Address;
        location.Notes = req.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(new LocationDto(location.Id, location.ProjectId, location.Name,
            location.Address, location.Notes));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var location = await db.Locations.FindAsync([id], ct);
        if (location is null) return NotFound();

        db.Locations.Remove(location);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Versioned site-rate overrides ────────────────────────────────────────

    /// <summary>All rate-override versions for a location, newest first.</summary>
    [HttpGet("{id:guid}/rates")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> ListRates(Guid id, CancellationToken ct)
    {
        if (!await db.Locations.AnyAsync(l => l.Id == id, ct)) return NotFound();
        var rates = await db.LocationRates
            .Where(r => r.LocationId == id)
            .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.CreatedUtc)
            .ToListAsync(ct);
        return Ok(rates.Select(ToRateDto));
    }

    /// <summary>Adds a new override version — fields left null inherit the
    /// contract terms; set fields win when bookings target this location.</summary>
    [HttpPost("{id:guid}/rates")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> CreateRate(Guid id, LocationRateRequest req, CancellationToken ct)
    {
        if (!await db.Locations.AnyAsync(l => l.Id == id, ct)) return NotFound();
        var rate = new LocationRate
        {
            LocationId = id,
            EffectiveFrom = req.EffectiveFrom,
            HourlyRate = req.HourlyRate, SaturdayRate = req.SaturdayRate,
            SundayRate = req.SundayRate, HolidayRate = req.HolidayRate,
            DayRate = req.DayRate, SaturdayDayRate = req.SaturdayDayRate,
            SundayDayRate = req.SundayDayRate, HolidayDayRate = req.HolidayDayRate,
            HalfDayRate = req.HalfDayRate, HalfDayMaxHours = req.HalfDayMaxHours,
            MobilisationFee = req.MobilisationFee, Notes = req.Notes,
        };
        db.LocationRates.Add(rate);
        await db.SaveChangesAsync(ct);
        return Ok(ToRateDto(rate));
    }

    /// <summary>Removes one override version (e.g. entered by mistake).</summary>
    [HttpDelete("{id:guid}/rates/{rateId:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> DeleteRate(Guid id, Guid rateId, CancellationToken ct)
    {
        var rate = await db.LocationRates
            .FirstOrDefaultAsync(r => r.Id == rateId && r.LocationId == id, ct);
        if (rate is null) return NotFound();
        db.LocationRates.Remove(rate);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static LocationRateDto ToRateDto(LocationRate r) => new(
        r.Id, r.LocationId, r.EffectiveFrom.ToString("yyyy-MM-dd"),
        r.HourlyRate, r.SaturdayRate, r.SundayRate, r.HolidayRate,
        r.DayRate, r.SaturdayDayRate, r.SundayDayRate, r.HolidayDayRate,
        r.HalfDayRate, r.HalfDayMaxHours, r.MobilisationFee, r.Notes);
}
