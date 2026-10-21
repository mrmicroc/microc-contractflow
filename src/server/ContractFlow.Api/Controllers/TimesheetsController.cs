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
/// each entry through <see cref="DayBilling"/> — the very pipeline the invoice
/// and statement use, so frozen quantities equal billed/paid ones;
/// return/reject (with a mandatory comment) releases the rows back to draft;
/// an admin may reopen an approved week until it is billed, paid or closed.
/// Every step lands in the review history.
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
        return Ok(await ToDetailAsync(sheet, scoped.HasValue, ct));
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
        if (existing?.Status is TimesheetStatus.Submitted or TimesheetStatus.Approved
            or TimesheetStatus.Invoiced)
            return Conflict(new { error = "alreadySubmitted", message = "That week is already submitted." });

        var me = await Me(ct);
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
        Db.TimesheetReviews.Add(new TimesheetReview
        {
            TimesheetId = sheet.Id, Action = TimesheetReviewAction.Submitted, ByUserId = me?.Id,
        });

        foreach (var x in entries) x.TimesheetId = sheet.Id;
        foreach (var x in travel) x.TimesheetId = sheet.Id;
        foreach (var x in expenses) x.TimesheetId = sheet.Id;
        foreach (var x in allowances) x.TimesheetId = sheet.Id;
        await Db.SaveChangesAsync(ct);

        return Ok(await ToDetailAsync((await LoadAsync(sheet.Id, ct))!, me?.Role == AppRole.Resource, ct));
    }

    /// <summary>Approve — prices every attendance day twice through
    /// <see cref="DayBilling"/>: agreement rules → payable hours; contract
    /// rules → billable hours (each contract with its own minimum and
    /// calendar). The results are frozen on each entry.</summary>
    [HttpPost("{id:guid}/approve")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var sheet = await LoadAsync(id, ct);
        if (sheet is null) return NotFound();
        if (sheet.Status != TimesheetStatus.Submitted)
            return BadRequest(new { error = "Only submitted timesheets can be approved." });
        // A month closed after submission can no longer take new approved rows.
        foreach (var (projectId, date) in ProjectDates(sheet))
            if (await pad.MonthIsClosedAsync(projectId, date.Year, date.Month, ct))
                return Conflict(new { error = "monthClosed", message = "A project month in that week is closed." });

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
        var bookingIds = sheet.Entries.Where(e => e.BookingId != null)
            .Select(e => e.BookingId!.Value).Distinct().ToList();
        var bookingPackages = await Db.Bookings.Where(b => bookingIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.WorkPackageId, ct);
        var packageIds = sheet.Entries.Select(e => e.WorkPackageId)
            .Concat(bookingPackages.Values).OfType<Guid>().Distinct().ToList();
        var outsidePackages = (await Db.WorkPackages
                .Where(w => packageIds.Contains(w.Id) && w.PricingMode == WorkPackagePricing.OutsideContract)
                .Select(w => w.Id).ToListAsync(ct))
            .ToHashSet();

        foreach (var day in sheet.Entries.GroupBy(e => e.Date))
        {
            var date = day.Key;
            var rows = day.Where(e => Counted.Contains(e.Category))
                .Select(e =>
                {
                    var wp = e.BookingId is { } bid ? bookingPackages.GetValueOrDefault(bid) : e.WorkPackageId;
                    return new DayRow(
                        e.Id, e.ProjectId, projectContracts[e.ProjectId], wp,
                        e.Billing, e.BookingId != null, e.ActualHours,
                        wp is { } w && outsidePackages.Contains(w));
                })
                .ToList();

            // Hour quantities depend only on the contract's rules, never on the
            // rate overrides — no override lookup needed here.
            var bill = DayBilling.BillDay(date, rows,
                    c => DayBilling.TermsAt(allTerms, c, date), _ => (null, null))
                .SelectMany(p => p.Entries).ToDictionary(s => s.EntryId);
            var pay = DayBilling.PayDay(date, rows,
                    (c, wp) => DayBilling.AgreementAt(agreements, c, wp, date),
                    c => DayBilling.TermsAt(allTerms, c, date)?.HolidayCalendar)
                .SelectMany(p => p.Entries).ToDictionary(s => s.EntryId);

            foreach (var entry in day)
            {
                if (!Counted.Contains(entry.Category))
                {
                    entry.PayableHours = entry.BillableHours = 0m;
                    entry.MinimumAdjustmentHours = entry.BillableMinimumAdjustmentHours = 0m;
                    continue;
                }
                entry.PayableHours = pay[entry.Id].Hours;
                entry.MinimumAdjustmentHours = pay[entry.Id].MinimumAdjustment;
                entry.BillableHours = bill[entry.Id].Hours;
                entry.BillableMinimumAdjustmentHours = bill[entry.Id].MinimumAdjustment;
            }
        }

        sheet.Status = TimesheetStatus.Approved;
        sheet.ReviewedUtc = DateTime.UtcNow;
        sheet.ReviewedByUserId = me?.Id;
        sheet.ReviewerNote = null;
        Db.TimesheetReviews.Add(new TimesheetReview
        {
            TimesheetId = sheet.Id, Action = TimesheetReviewAction.Approved, ByUserId = me?.Id,
        });
        await Db.SaveChangesAsync(ct);
        return Ok(ReportingMapper.Sheet(sheet));
    }

    /// <summary>Return to draft — releases the rows for correction. The
    /// timesheet row itself stays as the record of the returned submission.
    /// A comment telling the resource what to fix is required.</summary>
    [HttpPost("{id:guid}/return")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Return(Guid id, TimesheetReviewRequest req, CancellationToken ct)
        => await ReleaseAsync(id, TimesheetStatus.Returned, req.Note, ct);

    [HttpPost("{id:guid}/reject")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Reject(Guid id, TimesheetReviewRequest req, CancellationToken ct)
        => await ReleaseAsync(id, TimesheetStatus.Rejected, req.Note, ct);

    /// <summary>
    /// Audited correction path for an APPROVED week (design §3): an admin
    /// returns it to draft with a mandatory reason — allowed only while none
    /// of its project-months is closed and no non-draft invoice specification
    /// or final statement covers its rows. Draft specs that included the rows
    /// lose them (regenerate them afterwards).
    /// </summary>
    [HttpPost("{id:guid}/reopen")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Reopen(Guid id, TimesheetReviewRequest req, CancellationToken ct)
    {
        var reason = req.Note?.Trim();
        if (string.IsNullOrEmpty(reason))
            return BadRequest(new { error = "noteRequired", message = "A reason is required to reopen an approved timesheet." });
        var sheet = await LoadAsync(id, ct);
        if (sheet is null) return NotFound();
        if (sheet.Status != TimesheetStatus.Approved)
            return BadRequest(new { error = "Only approved timesheets can be reopened." });

        foreach (var (projectId, date) in ProjectDates(sheet))
            if (await pad.MonthIsClosedAsync(projectId, date.Year, date.Month, ct))
                return Conflict(new { error = "monthClosed", message = "A project month in that week is closed." });

        var entryIds = sheet.Entries.Select(e => e.Id).ToList();
        var specIds = sheet.Entries.Where(e => e.InvoiceSpecificationId != null)
            .Select(e => e.InvoiceSpecificationId!.Value).Distinct().ToList();
        var sourceIds = sheet.Expenses.Select(x => x.Id).ToList();
        var invoiced = await Db.InvoiceSpecifications.AnyAsync(s =>
                specIds.Contains(s.Id) && s.Status != InvoiceStatus.Draft, ct)
            || await Db.InvoiceLines.AnyAsync(l =>
                l.SourceId != null && sourceIds.Contains(l.SourceId.Value)
                && l.Invoice.Status != InvoiceStatus.Draft, ct);
        if (invoiced)
            return Conflict(new { error = "invoiced", message = "Rows of this week are on a non-draft invoice specification." });

        var months = sheet.Entries.Select(e => e.Date)
            .Concat(sheet.TravelRecords.Select(x => x.Date))
            .Concat(sheet.Expenses.Select(x => x.Date))
            .Concat(sheet.AllowanceClaims.Select(x => x.Date))
            .Select(d => (d.Year, d.Month)).Distinct().ToList();
        foreach (var (year, month) in months)
            if (await Db.SubcontractorStatements.AnyAsync(s => s.ResourceId == sheet.ResourceId
                    && s.Year == year && s.Month == month && s.Status != StatementStatus.Draft, ct))
                return Conflict(new { error = "statementFinal", message = "A final statement covers this week." });

        foreach (var e in sheet.Entries) e.InvoiceSpecificationId = null;
        return await ReleaseAsync(id, TimesheetStatus.Returned, reason, ct,
            TimesheetReviewAction.Reopened, sheet);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<IActionResult> ReleaseAsync(
        Guid id, TimesheetStatus status, string? note, CancellationToken ct,
        TimesheetReviewAction? action = null, Timesheet? loaded = null)
    {
        note = note?.Trim();
        if (string.IsNullOrEmpty(note))
            return BadRequest(new { error = "noteRequired", message = "A comment for the resource is required." });
        var sheet = loaded ?? await LoadAsync(id, ct);
        if (sheet is null) return NotFound();
        if (loaded is null && sheet.Status != TimesheetStatus.Submitted)
            return BadRequest(new { error = "Only submitted timesheets can be reviewed." });

        var me = await Me(ct);
        foreach (var e in sheet.Entries)
        {
            e.TimesheetId = null;
            e.PayableHours = e.BillableHours = e.MinimumAdjustmentHours = null;
            e.BillableMinimumAdjustmentHours = null;
        }
        foreach (var x in sheet.TravelRecords) x.TimesheetId = null;
        foreach (var x in sheet.Expenses) x.TimesheetId = null;
        foreach (var x in sheet.AllowanceClaims) x.TimesheetId = null;

        sheet.Status = status;
        sheet.ReviewedUtc = DateTime.UtcNow;
        sheet.ReviewedByUserId = me?.Id;
        sheet.ReviewerNote = note;
        Db.TimesheetReviews.Add(new TimesheetReview
        {
            TimesheetId = sheet.Id, Note = note, ByUserId = me?.Id,
            Action = action ?? (status == TimesheetStatus.Rejected
                ? TimesheetReviewAction.Rejected : TimesheetReviewAction.Returned),
        });
        await Db.SaveChangesAsync(ct);
        return Ok(ReportingMapper.Sheet(sheet));
    }

    private static IEnumerable<(Guid ProjectId, DateOnly Date)> ProjectDates(Timesheet sheet) =>
        sheet.Entries.Select(x => (x.ProjectId, x.Date))
            .Concat(sheet.TravelRecords.Where(x => x.ProjectId != null).Select(x => (x.ProjectId!.Value, x.Date)))
            .Concat(sheet.Expenses.Where(x => x.ProjectId != null).Select(x => (x.ProjectId!.Value, x.Date)))
            .Concat(sheet.AllowanceClaims.Where(x => x.ProjectId != null).Select(x => (x.ProjectId!.Value, x.Date)))
            .Distinct();

    private async Task<TimesheetDetailDto> ToDetailAsync(Timesheet sheet, bool hideBillable, CancellationToken ct)
    {
        var reviewerIds = sheet.Reviews.Where(r => r.ByUserId != null).Select(r => r.ByUserId!.Value)
            .Distinct().ToList();
        var names = await Db.Users.Where(u => reviewerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return new TimesheetDetailDto(
            ReportingMapper.Sheet(sheet),
            sheet.Entries.OrderBy(e => e.Date).Select(e => ReportingMapper.Entry(e, hideBillable)).ToList(),
            sheet.TravelRecords.OrderBy(x => x.Date).Select(ReportingMapper.Travel).ToList(),
            sheet.Expenses.OrderBy(x => x.Date).Select(ReportingMapper.Expense).ToList(),
            sheet.AllowanceClaims.OrderBy(x => x.Date).Select(ReportingMapper.Allowance).ToList(),
            sheet.Reviews.OrderBy(r => r.AtUtc).Select(r => new TimesheetReviewDto(
                r.Action, r.Note, r.ByUserId,
                r.ByUserId is { } uid ? names.GetValueOrDefault(uid) : null, r.AtUtc)).ToList());
    }

    private Task<Timesheet?> LoadAsync(Guid id, CancellationToken ct) =>
        Db.Timesheets
            .Include(t => t.Resource)
            .Include(t => t.Reviews)
            .Include(t => t.Entries).ThenInclude(e => e.Project)
            .Include(t => t.Entries).ThenInclude(e => e.Resource)
            .Include(t => t.TravelRecords).ThenInclude(x => x.Resource)
            .Include(t => t.TravelRecords).ThenInclude(x => x.Project)
            .Include(t => t.Expenses).ThenInclude(x => x.Resource)
            .Include(t => t.Expenses).ThenInclude(x => x.Project)
            .Include(t => t.AllowanceClaims).ThenInclude(x => x.Resource)
            .Include(t => t.AllowanceClaims).ThenInclude(x => x.Project)
            .AsSplitQuery()
            // entry.Timesheet is populated by fix-up — no back-include needed.
            .FirstOrDefaultAsync(t => t.Id == id, ct);
}
