using System.Globalization;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Timesheets — one per resource per ISO week (design §3). Submit gathers that
/// week's draft rows; approve freezes the computed payable/billable hours on
/// each entry (the §5 pipeline run twice — agreement rules for pay, contract
/// rules for billing); return/reject releases the rows back to draft.
/// </summary>
[ApiController]
[Route("api/timesheets")]
public class TimesheetsController(AppDbContext db, ICurrentUser currentUser, PadService pad)
    : ReportingControllerBase(db, currentUser)
{
    /// <summary>Categories that count toward payable/billable hours — Break
    /// rows are attendance truth only.</summary>
    private static readonly HashSet<TimeEntryCategory> Counted =
        [TimeEntryCategory.Work, TimeEntryCategory.Waiting, TimeEntryCategory.Standby];

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? resourceId, [FromQuery] TimesheetStatus? status, CancellationToken ct)
    {
        var scoped = await ScopedResourceIdAsync(ct);
        var query = Db.Timesheets
            .Include(t => t.Resource).Include(t => t.Entries)
            .Include(t => t.TravelRecords).Include(t => t.Expenses)
            .Include(t => t.AllowanceClaims)
            .AsQueryable();
        if (scoped.HasValue) query = query.Where(t => t.ResourceId == scoped.Value);
        else if (resourceId.HasValue) query = query.Where(t => t.ResourceId == resourceId.Value);
        if (status.HasValue) query = query.Where(t => t.Status == status.Value);

        var sheets = await query
            .OrderByDescending(t => t.Year).ThenByDescending(t => t.IsoWeek)
            .ToListAsync(ct);
        return Ok(sheets.Select(ReportingMapper.Sheet));
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var sheet = await LoadAsync(id, ct);
        if (sheet is null) return NotFound();
        var scoped = await ScopedResourceIdAsync(ct);
        if (scoped.HasValue && scoped.Value != sheet.ResourceId) return ForbidResult();
        return Ok(ToDetail(sheet));
    }

    /// <summary>Collect the week's draft rows into a submitted timesheet.</summary>
    [HttpPost("submit")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Submit(TimesheetSubmitRequest req, CancellationToken ct)
    {
        var (resourceId, error) = await WritableResourceAsync(req.ResourceId, ct);
        if (error is not null) return error;

        DateOnly monday;
        try { monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(req.Year, req.IsoWeek, DayOfWeek.Monday)); }
        catch { return BadRequest(new { error = "Invalid ISO week." }); }
        var sunday = monday.AddDays(6);

        var entries = await Db.TimeEntries
            .Where(x => x.ResourceId == resourceId && x.TimesheetId == null
                && x.Date >= monday && x.Date <= sunday).ToListAsync(ct);
        var travel = await Db.TravelRecords
            .Where(x => x.ResourceId == resourceId && x.TimesheetId == null
                && x.Date >= monday && x.Date <= sunday).ToListAsync(ct);
        var expenses = await Db.Expenses
            .Where(x => x.ResourceId == resourceId && x.TimesheetId == null
                && x.Date >= monday && x.Date <= sunday).ToListAsync(ct);
        var allowances = await Db.AllowanceClaims
            .Where(x => x.ResourceId == resourceId && x.TimesheetId == null
                && x.Date >= monday && x.Date <= sunday).ToListAsync(ct);

        if (entries.Count + travel.Count + expenses.Count + allowances.Count == 0)
            return BadRequest(new { error = "Nothing to submit — no draft rows in that week." });

        // The whole week is rejected if any row lands in a closed project-month.
        var projectDates = entries.Select(x => (x.ProjectId, x.Date))
            .Concat(travel.Where(x => x.ProjectId != null).Select(x => (x.ProjectId!.Value, x.Date)))
            .Concat(expenses.Where(x => x.ProjectId != null).Select(x => (x.ProjectId!.Value, x.Date)))
            .Concat(allowances.Where(x => x.ProjectId != null).Select(x => (x.ProjectId!.Value, x.Date)));
        foreach (var (projectId, date) in projectDates.Distinct())
            if (await pad.MonthIsClosedAsync(projectId, date.Year, date.Month, ct))
                return Conflict(new { error = "monthClosed", message = "A project month in that week is closed." });

        var existing = await Db.Timesheets.FirstOrDefaultAsync(
            t => t.ResourceId == resourceId && t.Year == req.Year && t.IsoWeek == req.IsoWeek, ct);
        if (existing?.Status is TimesheetStatus.Submitted or TimesheetStatus.Approved)
            return Conflict(new { error = "alreadySubmitted", message = "That week is already submitted." });

        var sheet = existing ?? new Timesheet
        {
            ResourceId = resourceId!.Value,
            Year = req.Year,
            IsoWeek = req.IsoWeek,
        };
        sheet.Status = TimesheetStatus.Submitted;
        sheet.SubmittedUtc = DateTime.UtcNow;
        sheet.ReviewedUtc = null;
        sheet.ReviewedByUserId = null;
        sheet.ReviewerNote = null;
        if (existing is null) Db.Timesheets.Add(sheet);

        foreach (var x in entries) x.TimesheetId = sheet.Id;
        foreach (var x in travel) x.TimesheetId = sheet.Id;
        foreach (var x in expenses) x.TimesheetId = sheet.Id;
        foreach (var x in allowances) x.TimesheetId = sheet.Id;
        await Db.SaveChangesAsync(ct);

        return Ok(ToDetail(await LoadAsync(sheet.Id, ct)));
    }

    /// <summary>Approve — runs the §5 pipeline twice per attendance day:
    /// agreement rules → payable hours; contract rules → billable hours. The
    /// results are frozen on each entry so the month reconciles later.</summary>
    [HttpPost("{id:guid}/approve")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var sheet = await LoadAsync(id, ct);
        if (sheet is null) return NotFound();
        if (sheet.Status != TimesheetStatus.Submitted)
            return BadRequest(new { error = "Only submitted timesheets can be approved." });

        var me = await Me(ct);
        var entryProjects = sheet.Entries.Select(e => e.ProjectId).Distinct().ToList();
        var projectContracts = await Db.Projects
            .Where(p => entryProjects.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.ContractId, ct);
        var agreements = await Db.CompensationAgreements
            .Where(a => a.ResourceId == sheet.ResourceId).ToListAsync(ct);
        var contractIds = projectContracts.Values.Distinct().ToList();
        var allTerms = await Db.ContractTerms
            .Where(t => contractIds.Contains(t.ContractId)).ToListAsync(ct);

        foreach (var day in sheet.Entries.GroupBy(e => e.Date))
        {
            var splits = day.Where(e => Counted.Contains(e.Category))
                .GroupBy(e => e.ProjectId)
                .Select(g => (ProjectId: g.Key, Hours: g.Sum(e => e.ActualHours)))
                .ToList();
            if (splits.Count == 0) continue;

            // OT/min parameters resolve from the dominant project's rule set —
            // single-project days (the common case) are exact.
            var dominant = splits.OrderByDescending(s => s.Hours).First().ProjectId;
            var payRates = PayableRateSet(agreements, projectContracts[dominant], day.Key);
            var billRates = BillableRateSet(allTerms, projectContracts[dominant], day.Key);

            var payDay = TimeMath.CalculateDay(day.Key, splits, payRates);
            var billDay = TimeMath.CalculateDay(day.Key, splits, billRates);

            foreach (var entry in day)
            {
                if (!Counted.Contains(entry.Category))
                {
                    entry.PayableHours = entry.BillableHours = 0m;
                    entry.MinimumAdjustmentHours = 0m;
                    continue;
                }
                var payLine = payDay.Lines.First(l => l.ProjectId == entry.ProjectId);
                var billLine = billDay.Lines.First(l => l.ProjectId == entry.ProjectId);
                entry.PayableHours = ShareOf(entry.ActualHours, payLine);
                entry.BillableHours = ShareOf(entry.ActualHours, billLine);
                entry.MinimumAdjustmentHours =
                    Math.Max(0m, entry.PayableHours.Value - entry.ActualHours);
            }
        }

        sheet.Status = TimesheetStatus.Approved;
        sheet.ReviewedUtc = DateTime.UtcNow;
        sheet.ReviewedByUserId = me?.Id;
        sheet.ReviewerNote = null;
        await Db.SaveChangesAsync(ct);
        return Ok(ReportingMapper.Sheet(sheet));
    }

    /// <summary>Return to draft — releases the rows for correction. The
    /// timesheet row itself stays as the record of the returned submission.</summary>
    [HttpPost("{id:guid}/return")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Return(Guid id, TimesheetReviewRequest req, CancellationToken ct)
        => await ReleaseAsync(id, TimesheetStatus.Returned, req.Note, ct);

    [HttpPost("{id:guid}/reject")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Reject(Guid id, TimesheetReviewRequest req, CancellationToken ct)
        => await ReleaseAsync(id, TimesheetStatus.Rejected, req.Note, ct);

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<IActionResult> ReleaseAsync(
        Guid id, TimesheetStatus status, string? note, CancellationToken ct)
    {
        var sheet = await LoadAsync(id, ct);
        if (sheet is null) return NotFound();
        if (sheet.Status != TimesheetStatus.Submitted)
            return BadRequest(new { error = "Only submitted timesheets can be reviewed." });

        var me = await Me(ct);
        foreach (var e in sheet.Entries)
        {
            e.TimesheetId = null;
            e.PayableHours = e.BillableHours = e.MinimumAdjustmentHours = null;
        }
        foreach (var x in sheet.TravelRecords) x.TimesheetId = null;
        foreach (var x in sheet.Expenses) x.TimesheetId = null;
        foreach (var x in sheet.AllowanceClaims) x.TimesheetId = null;

        sheet.Status = status;
        sheet.ReviewedUtc = DateTime.UtcNow;
        sheet.ReviewedByUserId = me?.Id;
        sheet.ReviewerNote = note;
        await Db.SaveChangesAsync(ct);
        return Ok(ReportingMapper.Sheet(sheet));
    }

    /// <summary>The entry's proportional share of its project line's effective
    /// hours — carries its slice of any minimum-hours adjustment.</summary>
    private static decimal ShareOf(decimal actual, DayLine line) =>
        line.ActualHours > 0
            ? TimeMath.Round2(actual * line.EffectiveHours / line.ActualHours)
            : actual;

    /// <summary>Rate set from the compensation agreement version in force on
    /// the day — contract-specific scope preferred over the default.</summary>
    private static DayRateSet PayableRateSet(
        List<CompensationAgreement> agreements, Guid contractId, DateOnly date)
    {
        var a = agreements
            .Where(x => (x.ContractId == contractId || x.ContractId == null)
                && x.EffectiveFrom <= date)
            .OrderByDescending(x => x.ContractId == contractId)
            .ThenByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version)
            .FirstOrDefault();
        return new DayRateSet(
            a?.HourlyRate, a?.SaturdayRate, a?.SundayRate, a?.HolidayRate,
            a?.OvertimeAfterHours ?? 8, a?.OvertimePercent ?? 150m,
            a?.MinPayableHoursPerDay);
    }

    private static DayRateSet BillableRateSet(
        List<ContractTerms> terms, Guid contractId, DateOnly date)
    {
        var t = terms
            .Where(x => x.ContractId == contractId && x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version)
            .FirstOrDefault();
        return new DayRateSet(
            t?.HourlyRate, t?.SaturdayRate, t?.SundayRate, t?.HolidayRate,
            t?.OvertimeAfterHours ?? 8, t?.OvertimePercent ?? 150m,
            t?.MinBillableHoursPerDay);
    }

    private static TimesheetDetailDto ToDetail(Timesheet? sheet) => new(
        ReportingMapper.Sheet(sheet!),
        sheet!.Entries.OrderBy(e => e.Date).Select(ReportingMapper.Entry).ToList(),
        sheet.TravelRecords.OrderBy(x => x.Date).Select(ReportingMapper.Travel).ToList(),
        sheet.Expenses.OrderBy(x => x.Date).Select(ReportingMapper.Expense).ToList(),
        sheet.AllowanceClaims.OrderBy(x => x.Date).Select(ReportingMapper.Allowance).ToList());

    private Task<Timesheet?> LoadAsync(Guid id, CancellationToken ct) =>
        Db.Timesheets
            .Include(t => t.Resource)
            .Include(t => t.Entries).ThenInclude(e => e.Project)
            .Include(t => t.Entries).ThenInclude(e => e.Resource)
            .Include(t => t.TravelRecords).ThenInclude(x => x.Resource)
            .Include(t => t.TravelRecords).ThenInclude(x => x.Project)
            .Include(t => t.Expenses).ThenInclude(x => x.Resource)
            .Include(t => t.Expenses).ThenInclude(x => x.Project)
            .Include(t => t.AllowanceClaims).ThenInclude(x => x.Resource)
            .Include(t => t.AllowanceClaims).ThenInclude(x => x.Project)
            // entry.Timesheet is populated by fix-up — no back-include needed.
            .FirstOrDefaultAsync(t => t.Id == id, ct);
}
