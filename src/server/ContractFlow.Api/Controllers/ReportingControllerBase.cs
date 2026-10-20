using System.Globalization;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Shared rules for the execution-reporting controllers (time entries, travel,
/// expenses, allowances): a Resource user is bound to their own linked
/// resource; admin/PM may act for any resource; rows inside a submitted or
/// approved timesheet are locked until returned/rejected (design §3).
/// </summary>
public abstract class ReportingControllerBase(AppDbContext db, ICurrentUser currentUser)
    : ControllerBase
{
    protected readonly AppDbContext Db = db;

    /// <summary>The resolved caller.</summary>
    protected Task<AppUser?> Me(CancellationToken ct) => currentUser.GetAsync(ct);

    /// <summary>The caller's linked Resource row, when the user is one.</summary>
    protected async Task<Resource?> OwnResourceAsync(CancellationToken ct)
    {
        var me = await Me(ct);
        return me?.Role == AppRole.Resource
            ? await Db.Resources.FirstOrDefaultAsync(r => r.UserId == me.Id, ct)
            : null;
    }

    /// <summary>
    /// Resolves which resource a write targets. Resource users can only write
    /// their own rows; admin/PM must supply a resourceId explicitly.
    /// </summary>
    protected async Task<(Guid? ResourceId, IActionResult? Error)> WritableResourceAsync(
        Guid? requestedId, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me!.Role == AppRole.Resource)
        {
            var own = await OwnResourceAsync(ct);
            if (own is null)
                return (null, BadRequest(new { error = "No resource is linked to your user." }));
            if (requestedId.HasValue && requestedId != own.Id)
                return (null, ForbidResult());
            return (own.Id, null);
        }
        if (!requestedId.HasValue)
            return (null, BadRequest(new { error = "resourceId is required." }));
        if (!await Db.Resources.AnyAsync(r => r.Id == requestedId && r.IsActive, ct))
            return (null, BadRequest(new { error = "Unknown or inactive resource." }));
        return (requestedId, null);
    }

    /// <summary>True when the caller may edit a row owned by
    /// <paramref name="resourceId"/> — admin/PM anyone's, a resource only its own.</summary>
    protected async Task<bool> OwnsAsync(Guid resourceId, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me!.Role is AppRole.Administrator or AppRole.ProjectManager) return true;
        var own = await OwnResourceAsync(ct);
        return own?.Id == resourceId;
    }

    /// <summary>
    /// Read scoping: returns the resource id the caller is restricted to, or
    /// null for unrestricted roles (admin/PM/finance).
    /// </summary>
    protected async Task<Guid?> ScopedResourceIdAsync(CancellationToken ct)
    {
        var me = await Me(ct);
        if (me!.Role != AppRole.Resource) return null;
        var own = await OwnResourceAsync(ct);
        return own?.Id ?? Guid.Empty;
    }

    /// <summary>True when the row sits in a submitted/approved timesheet —
    /// locked until the review returns or rejects it.</summary>
    protected async Task<bool> LockedAsync(Guid? timesheetId, CancellationToken ct)
    {
        if (!timesheetId.HasValue) return false;
        var status = await Db.Timesheets
            .Where(t => t.Id == timesheetId)
            .Select(t => (TimesheetStatus?)t.Status)
            .FirstOrDefaultAsync(ct);
        return status is TimesheetStatus.Submitted or TimesheetStatus.Approved;
    }

    protected IActionResult LockedError() => Conflict(new
    {
        error = "timesheetLocked",
        message = "The row belongs to a submitted timesheet — return it to draft first.",
    });

    /// <summary>True when the resource's timesheet for the ISO week containing
    /// <paramref name="date"/> is submitted or approved — a new or moved row
    /// would be stranded outside it (never submittable, never priced).</summary>
    protected async Task<bool> WeekLockedAsync(Guid resourceId, DateOnly date, CancellationToken ct)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        var year = ISOWeek.GetYear(dt);
        var week = ISOWeek.GetWeekOfYear(dt);
        return await Db.Timesheets.AnyAsync(t =>
            t.ResourceId == resourceId && t.Year == year && t.IsoWeek == week
            && (t.Status == TimesheetStatus.Submitted || t.Status == TimesheetStatus.Approved), ct);
    }

    protected IActionResult WeekLockedError() => Conflict(new
    {
        error = "weekLocked",
        message = "That week's timesheet is already submitted or approved — return it to draft first.",
    });

    protected IActionResult MonthClosedError() =>
        Conflict(new { error = "monthClosed", message = "The project month is closed." });

    protected IActionResult ForbidResult() =>
        new ObjectResult(new { error = "Forbidden." }) { StatusCode = 403 };
}
