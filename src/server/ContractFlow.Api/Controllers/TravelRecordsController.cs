using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>Travel legs + mileage (design §3 Execution). Car legs carry
/// DistanceKm as the mileage basis; DurationHours feeds travel-time
/// compensation per the agreement.</summary>
[ApiController]
[Route("api/travel-records")]
public class TravelRecordsController(AppDbContext db, ICurrentUser currentUser, PadService pad)
    : ReportingControllerBase(db, currentUser)
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? resourceId, [FromQuery] Guid? projectId,
        CancellationToken ct)
    {
        var scoped = await ScopedResourceIdAsync(ct);
        var query = Db.TravelRecords
            .Include(x => x.Resource).Include(x => x.Project).AsQueryable();
        if (scoped.HasValue) query = query.Where(x => x.ResourceId == scoped.Value);
        else if (resourceId.HasValue) query = query.Where(x => x.ResourceId == resourceId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Date <= to.Value);
        return Ok((await query.OrderBy(x => x.Date).ToListAsync(ct)).Select(ToDto));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Create(TravelRecordRequest req, CancellationToken ct)
    {
        var (resourceId, error) = await WritableResourceAsync(req.ResourceId, ct);
        if (error is not null) return error;
        var projectError = await ValidateProjectAndBookingAsync(
            req.ProjectId, req.BookingId, resourceId!.Value, req.Date, ct);
        if (projectError is not null) return projectError;
        if (await WeekLockedAsync(resourceId.Value, req.Date, ct)) return WeekLockedError();

        var me = await Me(ct);
        var row = new TravelRecord
        {
            ResourceId = resourceId.Value,
            ProjectId = req.ProjectId,
            BookingId = req.BookingId,
            Date = req.Date,
            FromPlace = req.FromPlace,
            ToPlace = req.ToPlace,
            Mode = req.Mode,
            DistanceKm = req.DistanceKm,
            DurationHours = req.DurationHours,
            Notes = req.Notes,
            CreatedByUserId = me?.Id,
        };
        Db.TravelRecords.Add(row);
        await Db.SaveChangesAsync(ct);
        return Created($"/api/travel-records/{row.Id}", await DtoAsync(row.Id, ct));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Update(Guid id, TravelRecordRequest req, CancellationToken ct)
    {
        var row = await Db.TravelRecords.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        if (!await OwnsAsync(row.ResourceId, ct)) return ForbidResult();
        if (await LockedAsync(row.TimesheetId, ct)) return LockedError();
        var projectError = await ValidateProjectAndBookingAsync(
            req.ProjectId, req.BookingId, row.ResourceId, req.Date, ct);
        if (projectError is not null) return projectError;
        if (await ClosedAsync(row.ProjectId, row.Date, ct)) return MonthClosedError();
        if (await WeekLockedAsync(row.ResourceId, req.Date, ct)) return WeekLockedError();

        row.ProjectId = req.ProjectId;
        row.BookingId = req.BookingId;
        row.Date = req.Date;
        row.FromPlace = req.FromPlace;
        row.ToPlace = req.ToPlace;
        row.Mode = req.Mode;
        row.DistanceKm = req.DistanceKm;
        row.DurationHours = req.DurationHours;
        row.Notes = req.Notes;
        await Db.SaveChangesAsync(ct);
        return Ok(await DtoAsync(id, ct));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var row = await Db.TravelRecords.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        if (!await OwnsAsync(row.ResourceId, ct)) return ForbidResult();
        if (await LockedAsync(row.TimesheetId, ct)) return LockedError();
        if (await ClosedAsync(row.ProjectId, row.Date, ct)) return MonthClosedError();
        Db.TravelRecords.Remove(row);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<IActionResult?> ValidateProjectAndBookingAsync(
        Guid? projectId, Guid? bookingId, Guid resourceId, DateOnly date, CancellationToken ct)
    {
        if (projectId.HasValue)
        {
            if (!await Db.Projects.AnyAsync(p => p.Id == projectId && p.IsActive, ct))
                return BadRequest(new { error = "Unknown or inactive project." });
            if (await ClosedAsync(projectId, date, ct)) return MonthClosedError();
        }
        if (bookingId.HasValue && !await Db.Bookings.AnyAsync(
                b => b.Id == bookingId.Value && b.ResourceId == resourceId, ct))
            return BadRequest(new { error = "Booking does not belong to this resource." });
        return null;
    }

    private async Task<bool> ClosedAsync(Guid? projectId, DateOnly date, CancellationToken ct) =>
        projectId.HasValue && await pad.MonthIsClosedAsync(projectId.Value, date.Year, date.Month, ct);

    private async Task<TravelRecordDto> DtoAsync(Guid id, CancellationToken ct) =>
        ToDto(await Db.TravelRecords
            .Include(x => x.Resource).Include(x => x.Project)
            .FirstAsync(x => x.Id == id, ct));

    private static TravelRecordDto ToDto(TravelRecord x) => new(
        x.Id, x.ResourceId, x.Resource.DisplayName, x.ProjectId, x.Project?.Name,
        x.BookingId, x.Date, x.FromPlace, x.ToPlace, x.Mode, x.DistanceKm,
        x.DurationHours, x.MobilisationPeriodId, x.Notes, x.TimesheetId);
}
