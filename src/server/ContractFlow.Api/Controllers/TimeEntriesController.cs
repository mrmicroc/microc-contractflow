using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Time entries — the execution ledger (design §3). Resources report their own
/// days (optionally linked to a booking); admin/PM may report on behalf of a
/// resource. Rows lock while inside a submitted/approved timesheet, and the
/// entry's project-month cannot be closed (§6.4).
/// </summary>
[ApiController]
[Route("api/time-entries")]
public class TimeEntriesController(AppDbContext db, ICurrentUser currentUser, PadService pad)
    : ReportingControllerBase(db, currentUser)
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? resourceId, [FromQuery] Guid? projectId,
        [FromQuery] Guid? timesheetId, CancellationToken ct)
    {
        var scoped = await ScopedResourceIdAsync(ct);
        var query = Db.TimeEntries
            .Include(x => x.Resource).Include(x => x.Project)
            .Include(x => x.Timesheet)
            .AsQueryable();

        if (scoped.HasValue)
            query = query.Where(x => x.ResourceId == scoped.Value);
        else if (resourceId.HasValue)
            query = query.Where(x => x.ResourceId == resourceId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Date <= to.Value);
        if (timesheetId.HasValue) query = query.Where(x => x.TimesheetId == timesheetId.Value);

        var rows = await query.OrderBy(x => x.Date).ThenBy(x => x.CreatedUtc).ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entry = await Db.TimeEntries
            .Include(x => x.Resource).Include(x => x.Project).Include(x => x.Timesheet)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entry is null) return NotFound();
        var scoped = await ScopedResourceIdAsync(ct);
        if (scoped.HasValue && scoped.Value != entry.ResourceId) return ForbidResult();
        return Ok(ToDto(entry));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Create(TimeEntryRequest req, CancellationToken ct)
    {
        var (resourceId, error) = await WritableResourceAsync(req.ResourceId, ct);
        if (error is not null) return error;
        if (!await Db.Projects.AnyAsync(p => p.Id == req.ProjectId && p.IsActive, ct))
            return BadRequest(new { error = "Unknown or inactive project." });
        if (req.BookingId.HasValue && !await Db.Bookings.AnyAsync(
                b => b.Id == req.BookingId.Value && b.ResourceId == resourceId
                    && b.ProjectId == req.ProjectId, ct))
            return BadRequest(new { error = "Booking does not belong to this resource on this project." });
        if (await pad.MonthIsClosedAsync(req.ProjectId, req.Date.Year, req.Date.Month, ct))
            return Conflict(new { error = "monthClosed", message = "The project month is closed." });

        var me = await Me(ct);
        var entry = new TimeEntry
        {
            ResourceId = resourceId!.Value,
            ProjectId = req.ProjectId,
            BookingId = req.BookingId,
            Date = req.Date,
            Category = req.Category,
            ActualHours = req.ActualHours,
            Description = req.Description,
            CreatedByUserId = me?.Id,
        };
        Db.TimeEntries.Add(entry);
        await Db.SaveChangesAsync(ct);
        return Created($"/api/time-entries/{entry.Id}", await DtoAsync(entry.Id, ct));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Update(Guid id, TimeEntryRequest req, CancellationToken ct)
    {
        var entry = await Db.TimeEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entry is null) return NotFound();
        if (!await OwnsAsync(entry.ResourceId, ct)) return ForbidResult();
        if (await LockedAsync(entry.TimesheetId, ct)) return LockedError();
        if (!await Db.Projects.AnyAsync(p => p.Id == req.ProjectId && p.IsActive, ct))
            return BadRequest(new { error = "Unknown or inactive project." });
        if (req.BookingId.HasValue && !await Db.Bookings.AnyAsync(
                b => b.Id == req.BookingId.Value && b.ResourceId == entry.ResourceId
                    && b.ProjectId == req.ProjectId, ct))
            return BadRequest(new { error = "Booking does not belong to this resource on this project." });
        if (await pad.MonthIsClosedAsync(req.ProjectId, req.Date.Year, req.Date.Month, ct))
            return Conflict(new { error = "monthClosed", message = "The project month is closed." });

        entry.ProjectId = req.ProjectId;
        entry.BookingId = req.BookingId;
        entry.Date = req.Date;
        entry.Category = req.Category;
        entry.ActualHours = req.ActualHours;
        entry.Description = req.Description;
        // Moving a draft row invalidates previously computed quantities.
        entry.PayableHours = entry.BillableHours = entry.MinimumAdjustmentHours = null;
        await Db.SaveChangesAsync(ct);
        return Ok(await DtoAsync(id, ct));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entry = await Db.TimeEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entry is null) return NotFound();
        if (!await OwnsAsync(entry.ResourceId, ct)) return ForbidResult();
        if (await LockedAsync(entry.TimesheetId, ct)) return LockedError();

        Db.TimeEntries.Remove(entry);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<TimeEntryDto> DtoAsync(Guid id, CancellationToken ct) =>
        ToDto(await Db.TimeEntries
            .Include(x => x.Resource).Include(x => x.Project).Include(x => x.Timesheet)
            .FirstAsync(x => x.Id == id, ct));

    private static TimeEntryDto ToDto(TimeEntry x) => new(
        x.Id, x.ResourceId, x.Resource.DisplayName, x.ProjectId, x.Project.Name,
        x.BookingId, x.Date, x.Category, x.ActualHours, x.Description,
        x.PayableHours, x.BillableHours, x.MinimumAdjustmentHours,
        x.TimesheetId, x.Timesheet?.Status);
}
