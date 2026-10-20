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
            .Include(x => x.Resource).Include(x => x.Project).Include(x => x.WorkPackage)
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
        return Ok(rows.Select(r => ForCaller(ToDto(r), scoped.HasValue)));
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entry = await Db.TimeEntries
            .Include(x => x.Resource).Include(x => x.Project).Include(x => x.WorkPackage)
            .Include(x => x.Timesheet)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entry is null) return NotFound();
        var scoped = await ScopedResourceIdAsync(ct);
        if (scoped.HasValue && scoped.Value != entry.ResourceId) return ForbidResult();
        return Ok(ForCaller(ToDto(entry), scoped.HasValue));
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
        if (req.BookingId.HasValue && req.WorkPackageId.HasValue)
            return BadRequest(new { error = "workPackageId applies only to unbooked rows — booked rows inherit the package." });
        if (req.WorkPackageId.HasValue && !await Db.WorkPackages.AnyAsync(
                w => w.Id == req.WorkPackageId.Value && w.ProjectId == req.ProjectId, ct))
            return BadRequest(new { error = "Work package does not belong to this project." });
        if (await pad.MonthIsClosedAsync(req.ProjectId, req.Date.Year, req.Date.Month, ct))
            return MonthClosedError();
        if (await WeekLockedAsync(resourceId!.Value, req.Date, ct)) return WeekLockedError();
        if (await AccessDeniedAsync(req.ProjectId, resourceId!.Value, ct) is { } denied)
            return denied;
        if (await DayOverflowAsync(resourceId!.Value, req.Date, req.ActualHours, null, ct))
            return DayOverflowError();

        var me = await Me(ct);
        var entry = new TimeEntry
        {
            ResourceId = resourceId!.Value,
            ProjectId = req.ProjectId,
            BookingId = req.BookingId,
            WorkPackageId = req.WorkPackageId,
            Date = req.Date,
            Category = req.Category,
            ActualHours = req.ActualHours,
            Billing = req.Billing,
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
        if (req.BookingId.HasValue && req.WorkPackageId.HasValue)
            return BadRequest(new { error = "workPackageId applies only to unbooked rows — booked rows inherit the package." });
        if (req.WorkPackageId.HasValue && !await Db.WorkPackages.AnyAsync(
                w => w.Id == req.WorkPackageId.Value && w.ProjectId == req.ProjectId, ct))
            return BadRequest(new { error = "Work package does not belong to this project." });
        // Both the row's current and target project-month must be open — a
        // row can neither move into nor out of a closed month.
        if (await pad.MonthIsClosedAsync(req.ProjectId, req.Date.Year, req.Date.Month, ct)
            || await pad.MonthIsClosedAsync(entry.ProjectId, entry.Date.Year, entry.Date.Month, ct))
            return MonthClosedError();
        if (await WeekLockedAsync(entry.ResourceId, req.Date, ct)) return WeekLockedError();
        if (await AccessDeniedAsync(req.ProjectId, entry.ResourceId, ct) is { } err) return err;
        if (await DayOverflowAsync(entry.ResourceId, req.Date, req.ActualHours, entry.Id, ct))
            return DayOverflowError();

        entry.ProjectId = req.ProjectId;
        entry.BookingId = req.BookingId;
        entry.WorkPackageId = req.WorkPackageId;
        entry.Date = req.Date;
        entry.Category = req.Category;
        entry.ActualHours = req.ActualHours;
        entry.Billing = req.Billing;
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
        if (await pad.MonthIsClosedAsync(entry.ProjectId, entry.Date.Year, entry.Date.Month, ct))
            return MonthClosedError();

        Db.TimeEntries.Remove(entry);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Self-reporting gate: only applies when the caller IS the
    /// resource (admin/PM may write for anyone — they can grant access anyway).
    /// Once a project carries an access list, only listed resources — or
    /// anyone holding a non-terminal booking on it — may self-report. An
    /// empty list keeps the project open to all resources.</summary>
    private async Task<IActionResult?> AccessDeniedAsync(
        Guid projectId, Guid resourceId, CancellationToken ct)
    {
        var scoped = await ScopedResourceIdAsync(ct);
        if (scoped is null) return null; // admin/PM reporting on behalf
        if (!await Db.ProjectAccesses.AnyAsync(a => a.ProjectId == projectId, ct))
            return null; // no list → open
        var allowed = await Db.ProjectAccesses.AnyAsync(
            a => a.ProjectId == projectId && a.ResourceId == resourceId, ct);
        if (!allowed)
            allowed = await Db.Bookings.AnyAsync(b =>
                b.ProjectId == projectId && b.ResourceId == resourceId
                && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Rescheduled, ct);
        return allowed ? null : ForbidResult();
    }

    /// <summary>A resource's reported rows for one date can never exceed 24 h
    /// in total (each row alone is only range-checked).</summary>
    private async Task<bool> DayOverflowAsync(
        Guid resourceId, DateOnly date, decimal hours, Guid? excludeId, CancellationToken ct)
    {
        var others = await Db.TimeEntries
            .Where(x => x.ResourceId == resourceId && x.Date == date && x.Id != excludeId)
            .Select(x => x.ActualHours).ToListAsync(ct);
        return others.Sum() + hours > 24m;
    }

    private IActionResult DayOverflowError() => BadRequest(new
    {
        error = "dayOverflow",
        message = "The day's reported hours would exceed 24.",
    });

    private async Task<TimeEntryDto> DtoAsync(Guid id, CancellationToken ct) =>
        ForCaller(ToDto(await Db.TimeEntries
            .Include(x => x.Resource).Include(x => x.Project).Include(x => x.WorkPackage)
            .Include(x => x.Timesheet)
            .FirstAsync(x => x.Id == id, ct)), (await ScopedResourceIdAsync(ct)).HasValue);

    /// <summary>Billable quantities are customer-side (§1.1) — a Resource
    /// caller sees actual and payable hours only.</summary>
    private static TimeEntryDto ForCaller(TimeEntryDto dto, bool isResource) =>
        isResource ? dto with { BillableHours = null } : dto;

    private static TimeEntryDto ToDto(TimeEntry x) => new(
        x.Id, x.ResourceId, x.Resource.DisplayName, x.ProjectId, x.Project.Name,
        x.BookingId, x.WorkPackageId, x.WorkPackage?.Name, x.Date, x.Category,
        x.ActualHours,
        x.Billing, x.Description, x.PayableHours, x.BillableHours,
        x.MinimumAdjustmentHours, x.TimesheetId, x.Timesheet?.Status);
}
