using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Mobilisation periods — continuous attendance groupings (design §6.2). The
/// suggest endpoint groups a resource's attendance days (bookings + time
/// entries + travel) into runs of consecutive days; admin/PM confirms,
/// optionally splitting or merging. Confirmed periods carry the contract's
/// mobilisation fee at billing time.
/// </summary>
[ApiController]
[Route("api/mobilisations")]
public class MobilisationsController(AppDbContext db, ICurrentUser currentUser)
    : ReportingControllerBase(db, currentUser)
{
    private static readonly HashSet<BookingStatus> Terminal =
        [BookingStatus.Completed, BookingStatus.Cancelled, BookingStatus.Rescheduled];

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? projectId, [FromQuery] Guid? resourceId, CancellationToken ct)
    {
        var scoped = await ScopedResourceIdAsync(ct);
        var query = Db.MobilisationPeriods
            .Include(x => x.Project).Include(x => x.Resource).AsQueryable();
        if (scoped.HasValue) query = query.Where(x => x.ResourceId == scoped.Value);
        else if (resourceId.HasValue) query = query.Where(x => x.ResourceId == resourceId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        return Ok((await query.OrderBy(x => x.StartDate).ToListAsync(ct))
            .Select(ReportingMapper.Mobilisation));
    }

    /// <summary>Suggested periods from attendance evidence — consecutive-day
    /// runs per project, excluding ranges already confirmed.</summary>
    [HttpGet("suggest")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Suggest(
        [FromQuery] Guid resourceId, [FromQuery] DateOnly from,
        [FromQuery] DateOnly to, CancellationToken ct)
    {
        var scoped = await ScopedResourceIdAsync(ct);
        if (scoped.HasValue && scoped.Value != resourceId) return ForbidResult();
        if (scoped == Guid.Empty) return Ok(Array.Empty<object>());

        var bookingDays = await Db.Bookings
            .Where(b => b.ResourceId == resourceId && b.Date >= from && b.Date <= to
                && !Terminal.Contains(b.Status))
            .Select(b => new { b.ProjectId, b.Date }).ToListAsync(ct);
        var entryDays = await Db.TimeEntries
            .Where(e => e.ResourceId == resourceId && e.Date >= from && e.Date <= to)
            .Select(e => new { e.ProjectId, e.Date }).ToListAsync(ct);
        var travelDays = await Db.TravelRecords
            .Where(t => t.ResourceId == resourceId && t.ProjectId != null
                && t.Date >= from && t.Date <= to)
            .Select(t => new { ProjectId = t.ProjectId!.Value, t.Date }).ToListAsync(ct);
        var confirmed = await Db.MobilisationPeriods
            .Where(p => p.ResourceId == resourceId)
            .Select(p => new { p.ProjectId, p.StartDate, p.EndDate }).ToListAsync(ct);

        var byProject = bookingDays.Concat(entryDays).Concat(travelDays)
            .GroupBy(x => x.ProjectId);
        var projectNames = await Db.Projects
            .Where(p => byProject.Select(g => g.Key).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var suggestions = byProject
            .SelectMany(g => TimeMath.SuggestPeriods(g.Select(x => x.Date))
                .Where(p => !confirmed.Any(c => c.ProjectId == g.Key
                    && c.StartDate <= p.End && c.EndDate >= p.Start))
                .Select(p => new MobilisationSuggestionDto(
                    g.Key, projectNames.GetValueOrDefault(g.Key) ?? "", p.Start, p.End)))
            .OrderBy(s => s.StartDate).ToList();
        return Ok(suggestions);
    }

    /// <summary>Confirm a period — links matching travel records inside it.</summary>
    [HttpPost]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Confirm(MobilisationConfirmRequest req, CancellationToken ct)
    {
        if (req.EndDate < req.StartDate)
            return BadRequest(new { error = "EndDate must be on or after StartDate." });
        if (!await Db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });
        if (!await Db.Resources.AnyAsync(r => r.Id == req.ResourceId, ct))
            return BadRequest(new { error = "Unknown resource." });
        if (await Db.MobilisationPeriods.AnyAsync(p => p.ResourceId == req.ResourceId
                && p.ProjectId == req.ProjectId
                && p.StartDate <= req.EndDate && p.EndDate >= req.StartDate, ct))
            return Conflict(new { error = "overlap", message = "Overlaps an existing confirmed period." });

        if (req.WorkPackageId.HasValue && !await Db.WorkPackages.AnyAsync(
                l => l.Id == req.WorkPackageId.Value && l.ProjectId == req.ProjectId, ct))
            return BadRequest(new { error = "WorkPackage does not belong to this project." });

        var me = await Me(ct);
        var period = new MobilisationPeriod
        {
            ProjectId = req.ProjectId,
            ResourceId = req.ResourceId,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            WorkPackageId = req.WorkPackageId,
            ConfirmedByUserId = me?.Id,
        };
        Db.MobilisationPeriods.Add(period);
        await Db.SaveChangesAsync(ct); // period row must exist before the FK link
        await LinkTravelAsync(period, ct);
        await Db.SaveChangesAsync(ct);
        return Created($"/api/mobilisations/{period.Id}",
            ReportingMapper.Mobilisation(await LoadAsync(period.Id, ct)));
    }

    /// <summary>Split at FromDate: original keeps [Start, From−1], a new period
    /// covers [From, End].</summary>
    [HttpPost("{id:guid}/split")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Split(Guid id, MobilisationSplitRequest req, CancellationToken ct)
    {
        var period = await Db.MobilisationPeriods.FindAsync([id], ct);
        if (period is null) return NotFound();
        if (req.FromDate <= period.StartDate || req.FromDate > period.EndDate)
            return BadRequest(new { error = "Split point must lie inside the period." });

        var me = await Me(ct);
        var second = new MobilisationPeriod
        {
            ProjectId = period.ProjectId,
            ResourceId = period.ResourceId,
            StartDate = req.FromDate,
            EndDate = period.EndDate,
            ConfirmedByUserId = me?.Id,
        };
        period.EndDate = req.FromDate.AddDays(-1);
        Db.MobilisationPeriods.Add(second);
        await Db.SaveChangesAsync(ct); // the new period must exist before relinking

        // Re-link travel records inside the new half.
        await Db.TravelRecords
            .Where(t => t.MobilisationPeriodId == period.Id && t.Date >= req.FromDate)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MobilisationPeriodId, second.Id), ct);
        await Db.SaveChangesAsync(ct);
        return Ok(new
        {
            first = ReportingMapper.Mobilisation(await LoadAsync(period.Id, ct)),
            second = ReportingMapper.Mobilisation(await LoadAsync(second.Id, ct)),
        });
    }

    /// <summary>Merge another period of the same resource+project into this
    /// one — the union range survives, the other row is removed.</summary>
    [HttpPost("{id:guid}/merge")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Merge(Guid id, MobilisationMergeRequest req, CancellationToken ct)
    {
        var period = await Db.MobilisationPeriods.FindAsync([id], ct);
        var other = await Db.MobilisationPeriods.FindAsync([req.OtherId], ct);
        if (period is null || other is null) return NotFound();
        if (other.ResourceId != period.ResourceId || other.ProjectId != period.ProjectId)
            return BadRequest(new { error = "Only periods of the same resource and project can merge." });

        period.StartDate = other.StartDate < period.StartDate ? other.StartDate : period.StartDate;
        period.EndDate = other.EndDate > period.EndDate ? other.EndDate : period.EndDate;
        await Db.TravelRecords
            .Where(t => t.MobilisationPeriodId == other.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MobilisationPeriodId, period.Id), ct);
        Db.MobilisationPeriods.Remove(other);
        await Db.SaveChangesAsync(ct);
        return Ok(ReportingMapper.Mobilisation(await LoadAsync(id, ct)));
    }

    /// <summary>Sets (or clears) which work package the period mobilised to — decides
    /// the package-specific mobilisation fee at billing.</summary>
    [HttpPatch("{id:guid}/work-package")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> SetSite(Guid id, MobilisationWorkPackageRequest req, CancellationToken ct)
    {
        var period = await Db.MobilisationPeriods.FindAsync([id], ct);
        if (period is null) return NotFound();
        if (req.WorkPackageId.HasValue && !await Db.WorkPackages.AnyAsync(
                l => l.Id == req.WorkPackageId.Value && l.ProjectId == period.ProjectId, ct))
            return BadRequest(new { error = "WorkPackage does not belong to this project." });
        period.WorkPackageId = req.WorkPackageId;
        await Db.SaveChangesAsync(ct);
        return Ok(ReportingMapper.Mobilisation(await LoadAsync(id, ct)));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var period = await Db.MobilisationPeriods.FindAsync([id], ct);
        if (period is null) return NotFound();
        // TravelRecord.MobilisationPeriodId is SetNull — links release on remove.
        Db.MobilisationPeriods.Remove(period);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task LinkTravelAsync(MobilisationPeriod period, CancellationToken ct) =>
        await Db.TravelRecords
            .Where(t => t.ResourceId == period.ResourceId && t.ProjectId == period.ProjectId
                && t.Date >= period.StartDate && t.Date <= period.EndDate
                && t.MobilisationPeriodId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MobilisationPeriodId, period.Id), ct);

    private Task<MobilisationPeriod> LoadAsync(Guid id, CancellationToken ct) =>
        Db.MobilisationPeriods
            .Include(x => x.Project).Include(x => x.Resource).Include(x => x.WorkPackage)
            .FirstAsync(x => x.Id == id, ct);
}
