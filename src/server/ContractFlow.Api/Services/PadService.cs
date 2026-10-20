using System.Data;
using System.Globalization;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Services;

/// <summary>
/// PAD ledger operations (design §4): appends transactions, derives monthly
/// accounts, and performs month-end close (carry-over + expiry). The ledger is
/// append-only — corrections are new rows.
/// </summary>
public class PadService(AppDbContext db, TimeProvider clock)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Booking statuses holding a live commitment — unresolved at close.</summary>
    private static readonly HashSet<BookingStatus> Live =
        [BookingStatus.Confirmed, BookingStatus.Acknowledged, BookingStatus.InProgress];

    private static readonly HashSet<BookingStatus> Planning =
        [BookingStatus.Forecast, BookingStatus.Tentative, BookingStatus.Requested];

    /// <summary>Business-local today (Europe/Stockholm) from the injected clock.</summary>
    public DateOnly Today => clock.Today();

    /// <summary>Terms version in force for the project's contract on a date.</summary>
    public async Task<ContractTerms?> TermsForAsync(
        Guid projectId, DateOnly date, CancellationToken ct)
    {
        var contractId = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => p.ContractId)
            .FirstOrDefaultAsync(ct);
        if (contractId == Guid.Empty) return null;

        return await db.ContractTerms
            .Where(t => t.ContractId == contractId && t.EffectiveFrom <= date)
            .OrderByDescending(t => t.EffectiveFrom)
            .ThenByDescending(t => t.Version)
            .FirstOrDefaultAsync(ct);
    }

    public Task<List<PadTransaction>> MonthAsync(
        Guid projectId, int year, int month, CancellationToken ct) =>
        db.PadTransactions
            .Where(t => t.ProjectId == projectId && t.Year == year && t.Month == month)
            .OrderBy(t => t.CreatedUtc)
            .ToListAsync(ct);

    public async Task<bool> MonthIsClosedAsync(Guid projectId, int year, int month, CancellationToken ct) =>
        await db.ProjectMonthCloses
            .AnyAsync(c => c.ProjectId == projectId && c.Year == year && c.Month == month, ct);

    /// <summary>
    /// Runs <paramref name="work"/> inside one serializable transaction — the
    /// read-free-then-write sequences (commit, approve, close) must not
    /// interleave. Joins an already-open transaction instead of nesting.
    /// </summary>
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return await work();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<PadTransaction> AppendAsync(
        Guid projectId, DateOnly month, PadTransactionType type, decimal amount,
        Guid? userId, CancellationToken ct,
        Guid? bookingId = null, Guid? additionalPadRequestId = null, string? note = null,
        (int Year, int Month)? origin = null)
    {
        var tx = new PadTransaction
        {
            ProjectId = projectId,
            Year = month.Year,
            Month = month.Month,
            Type = type,
            Amount = PadMath.Round4(amount),
            BookingId = bookingId,
            AdditionalPadRequestId = additionalPadRequestId,
            OriginYear = origin?.Year,
            OriginMonth = origin?.Month,
            Note = note,
            CreatedByUserId = userId,
        };
        db.PadTransactions.Add(tx);
        await db.SaveChangesAsync(ct);
        return tx;
    }

    /// <summary>Open commitment for one booking: commits − releases − utilises.</summary>
    public async Task<decimal> OpenCommitmentAsync(Guid bookingId, CancellationToken ct)
    {
        var txs = await db.PadTransactions
            .Where(t => t.BookingId == bookingId)
            .ToListAsync(ct);
        return txs.Where(t => t.Type == PadTransactionType.Commit).Sum(t => t.Amount)
             - txs.Where(t => t.Type == PadTransactionType.Release).Sum(t => t.Amount)
             - txs.Where(t => t.Type == PadTransactionType.Utilise).Sum(t => t.Amount);
    }

    /// <summary>
    /// Tries to reserve PAD for a booking being confirmed. Succeeds only when
    /// the month's free PAD covers the commitment — approved Additional PAD
    /// already counts into free, so partial availability works automatically
    /// (design §4.3).
    /// </summary>
    public async Task<CommitResult> TryCommitAsync(
        Booking booking, Guid? userId, CancellationToken ct)
    {
        var terms = await TermsForAsync(booking.ProjectId, booking.Date, ct);
        // No PAD component on the in-force terms → confirming is a pure status
        // change; the ledger is untouched (hourly/fixed-price contracts).
        if (terms?.PadEnabled != true)
            return new CommitResult(true, 0m, 0m, MonthClosed: false);

        var amount = PadMath.CommitAmount(
            booking.PlannedHours,
            terms?.PadHoursPerDay ?? 8m,
            terms?.MinPadPerBookedDay ?? 1m);

        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        if (await MonthIsClosedAsync(booking.ProjectId, month.Year, month.Month, ct))
            return new CommitResult(false, 0m, amount, MonthClosed: true);

        return await InTransactionAsync(async () =>
        {
            var sums = PadMath.Sum(await MonthAsync(booking.ProjectId, month.Year, month.Month, ct));
            if (sums.Free < amount)
                return new CommitResult(false, sums.Free, amount, MonthClosed: false);

            await AppendAsync(booking.ProjectId, month, PadTransactionType.Commit, amount,
                userId, ct, bookingId: booking.Id);
            return new CommitResult(true, sums.Free - amount, amount, MonthClosed: false);
        }, ct);
    }

    /// <summary>Writes a Release or Utilise for the booking's open commitment.</summary>
    public async Task<decimal> SettleCommitmentAsync(
        Booking booking, PadTransactionType type, Guid? userId, string? note, CancellationToken ct)
    {
        var open = await OpenCommitmentAsync(booking.Id, ct);
        if (open <= 0) return 0m;
        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        await AppendAsync(booking.ProjectId, month, type, open, userId, ct,
            bookingId: booking.Id, note: note);
        return open;
    }

    /// <summary>
    /// Partial-charge cancellation settlement: utilises
    /// <paramref name="utilisePad"/> PAD of the open commitment and releases
    /// the remainder (stepped cancellation policy, design §4.3).
    /// </summary>
    public async Task<(decimal Utilised, decimal Released)> SettlePartialAsync(
        Booking booking, decimal utilisePad, Guid? userId, CancellationToken ct)
    {
        var open = await OpenCommitmentAsync(booking.Id, ct);
        if (open <= 0) return (0m, 0m);
        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        var used = PadMath.Round4(Math.Min(open, Math.Max(0m, utilisePad)));
        if (used > 0)
            await AppendAsync(booking.ProjectId, month, PadTransactionType.Utilise,
                used, userId, ct, bookingId: booking.Id,
                note: "Cancellation — chargeable portion");
        var rest = open - used;
        if (rest > 0)
            await AppendAsync(booking.ProjectId, month, PadTransactionType.Release,
                rest, userId, ct, bookingId: booking.Id,
                note: "Cancellation — released remainder");
        return (used, rest);
    }

    /// <summary>
    /// Continuation beyond commitment (§4.3): the booking's actual PAD exceeds
    /// its commitment by <paramref name="extraPad"/>. Allocates ① current free
    /// → ② approved Additional → ③ next-month baseline (BorrowNextMonth row in
    /// the lending month) → ④ auto-incurred Additional, and writes the ledger
    /// rows. Returns the split for display.
    /// </summary>
    public async Task<OverrunSplit> AllocateOverrunAsync(
        Booking booking, decimal extraPad, Guid? userId, CancellationToken ct)
    {
        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        var next = month.AddMonths(1);
        var currentTxs = await MonthAsync(booking.ProjectId, month.Year, month.Month, ct);
        var current = PadMath.Sum(currentTxs);
        PadMonthSums? nextSums = await MonthIsClosedAsync(booking.ProjectId, next.Year, next.Month, ct)
            ? null
            : PadMath.Sum(await MonthAsync(booking.ProjectId, next.Year, next.Month, ct));

        // Unused approved Additional PAD: what utilisation beyond the
        // month's own pool (baseline + carry + adjustments) hasn't eaten yet.
        var ownPool = current.Baseline + current.CarryOverIn + current.Adjustments;
        var additionalUsed = Math.Max(0m, current.Utilised + current.CommittedOpen - ownPool);
        var additionalAvailable = Math.Max(0m, current.AdditionalApproved - additionalUsed);

        var split = PadMath.AllocateOverrun(extraPad, current, nextSums, additionalAvailable);
        var inMonth = split.FromCurrentFree + split.FromAdditional + split.AutoAdditional;
        if (split.AutoAdditional > 0)
            await AppendAsync(booking.ProjectId, month, PadTransactionType.AutoAdditional,
                split.AutoAdditional, userId, ct, bookingId: booking.Id,
                note: "Continuation overflow — auto-incurred Additional PAD");
        if (inMonth > 0)
            await AppendAsync(booking.ProjectId, month, PadTransactionType.Utilise,
                inMonth, userId, ct, bookingId: booking.Id,
                note: string.Create(Inv,
                    $"Continuation beyond commitment: {split.FromCurrentFree:0.####} free + {split.FromAdditional:0.####} additional + {split.AutoAdditional:0.####} auto-additional"));
        if (split.FromNextMonthBaseline > 0)
            await AppendAsync(booking.ProjectId, next, PadTransactionType.BorrowNextMonth,
                split.FromNextMonthBaseline, userId, ct, bookingId: booking.Id,
                note: $"Borrowed by continuation in {month.Year}-{month.Month:D2}" +
                    (split.NextMonthConflict ? " — exceeds free PAD, review conflicts" : ""));
        return split;
    }

    /// <summary>
    /// Dry run of the month-end close (design §6.4): ordering checks, the
    /// pre-close checklist and the carry-over / expiry calculation shown
    /// step by step. Writes nothing.
    /// </summary>
    public async Task<ClosePreviewDto> PreviewCloseAsync(
        Guid projectId, int year, int month, CancellationToken ct)
    {
        var plan = await PlanCloseAsync(projectId, year, month, ct);
        return plan.Preview;
    }

    /// <summary>
    /// Month-end close: re-checks the plan, then releases still-open
    /// commitments, writes carry-over (only from unused baseline, capped per
    /// contract terms, carry consumed first) and expiry for both months and
    /// locks the month — all in one transaction. Throws
    /// <see cref="CloseBlockedException"/> on ordering errors, or on blocking
    /// checklist items unless <paramref name="force"/> (with a reason).
    /// </summary>
    public async Task<CloseMonthResultDto> CloseMonthAsync(
        Guid projectId, int year, int month, Guid? userId, CancellationToken ct,
        bool force = false, string? reason = null)
    {
        return await InTransactionAsync(async () =>
        {
            var plan = await PlanCloseAsync(projectId, year, month, ct);
            var preview = plan.Preview;
            if (preview.OrderingErrors.Length > 0)
                throw new CloseBlockedException(preview);
            var blocking = preview.Checklist.Any(i => i.Blocking);
            if (blocking && !force)
                throw new CloseBlockedException(preview);

            var monthStart = new DateOnly(year, month, 1);
            var next = monthStart.AddMonths(1);

            foreach (var (bookingId, open) in plan.Releases)
                await AppendAsync(projectId, monthStart, PadTransactionType.Release, open,
                    userId, ct, bookingId: bookingId, note: "Released at month-end close");

            foreach (var (originYear, originMonth, amount) in plan.ReCarries)
            {
                await AppendAsync(projectId, monthStart, PadTransactionType.CarryOverOut,
                    amount, userId, ct,
                    note: $"Re-carry of PAD from {originYear}-{originMonth:D2}",
                    origin: (originYear, originMonth));
                await AppendAsync(projectId, next, PadTransactionType.CarryOverIn,
                    amount, userId, ct,
                    note: $"Carry-over from {originYear}-{originMonth:D2}",
                    origin: (originYear, originMonth));
            }
            foreach (var (originYear, originMonth, amount) in plan.ResidualExpiries)
                await AppendAsync(projectId, monthStart, PadTransactionType.Expire,
                    amount, userId, ct,
                    note: $"Carry-over from {originYear}-{originMonth:D2} expired",
                    origin: (originYear, originMonth));

            if (plan.CarryOver > 0)
            {
                await AppendAsync(projectId, monthStart, PadTransactionType.CarryOverOut,
                    plan.CarryOver, userId, ct,
                    note: "Carry-over to next month", origin: (year, month));
                await AppendAsync(projectId, next, PadTransactionType.CarryOverIn,
                    plan.CarryOver, userId, ct,
                    note: $"Carry-over from {year}-{month:D2}", origin: (year, month));
            }
            if (plan.ExpiredBaseline > 0)
                await AppendAsync(projectId, monthStart, PadTransactionType.Expire,
                    plan.ExpiredBaseline, userId, ct, note: "Expired unused baseline");

            db.ProjectMonthCloses.Add(new ProjectMonthClose
            {
                ProjectId = projectId, Year = year, Month = month, ClosedByUserId = userId,
                ForcedReason = blocking ? reason : null,
            });
            await db.SaveChangesAsync(ct);
            return preview.Result;
        }, ct);
    }

    private record ClosePlan(
        ClosePreviewDto Preview,
        List<(Guid BookingId, decimal Open)> Releases,
        List<(int OriginYear, int OriginMonth, decimal Amount)> ReCarries,
        List<(int OriginYear, int OriginMonth, decimal Amount)> ResidualExpiries,
        decimal CarryOver,
        decimal ExpiredBaseline);

    private async Task<ClosePlan> PlanCloseAsync(
        Guid projectId, int year, int month, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var next = monthStart.AddMonths(1);
        var closingIndex = year * 12 + month;

        // ── Ordering — never forceable ─────────────────────────────────────
        var errors = new List<string>();
        var closes = await db.ProjectMonthCloses
            .Where(c => c.ProjectId == projectId)
            .Select(c => new { c.Year, c.Month }).ToListAsync(ct);
        var allTxs = await db.PadTransactions
            .Where(t => t.ProjectId == projectId).ToListAsync(ct);
        if (closes.Any(c => c.Year == year && c.Month == month))
            errors.Add("alreadyClosed");
        // Every earlier month with ledger rows must be closed — otherwise its
        // carry-over would land in (or skip past) this month unaccounted.
        if (allTxs.Select(t => (t.Year, t.Month)).Distinct()
                .Any(m => m.Year * 12 + m.Month < closingIndex
                    && !closes.Any(c => c.Year == m.Year && c.Month == m.Month)))
            errors.Add("previousMonthOpen");
        if (closes.Any(c => c.Year == next.Year && c.Month == next.Month))
            errors.Add("nextMonthClosed");
        if (Today <= monthEnd)
            errors.Add("monthNotEnded");

        // ── Checklist (§6.4) ───────────────────────────────────────────────
        var checklist = new List<CloseCheckItemDto>();
        void Check(string code, int count, bool blocking)
        {
            if (count > 0) checklist.Add(new CloseCheckItemDto(code, count, blocking));
        }

        var bookings = await db.Bookings
            .Where(b => b.ProjectId == projectId && b.Date >= monthStart && b.Date <= monthEnd)
            .Select(b => b.Status).ToListAsync(ct);
        Check("unresolvedBookings", bookings.Count(Live.Contains), true);
        Check("planningLeftovers", bookings.Count(Planning.Contains), false);

        var entries = await db.TimeEntries
            .Where(e => e.ProjectId == projectId && e.Date >= monthStart && e.Date <= monthEnd)
            .Select(e => new { e.TimesheetId, Status = (TimesheetStatus?)e.Timesheet!.Status })
            .ToListAsync(ct);
        Check("unsubmittedTime", entries.Count(e => e.TimesheetId == null), true);
        Check("unapprovedTimesheets",
            entries.Count(e => e.Status == TimesheetStatus.Submitted), true);

        var expenses = await db.Expenses
            .Where(e => e.ProjectId == projectId && e.Date >= monthStart && e.Date <= monthEnd)
            .Select(e => new { e.TimesheetId, Status = (TimesheetStatus?)e.Timesheet!.Status })
            .ToListAsync(ct);
        Check("unapprovedExpenses", expenses.Count(e =>
            e.TimesheetId == null || e.Status == TimesheetStatus.Submitted), true);

        var travel = await db.TravelRecords
            .Where(t => t.ProjectId == projectId && t.Date >= monthStart && t.Date <= monthEnd)
            .Select(t => new { t.TimesheetId, Status = (TimesheetStatus?)t.Timesheet!.Status })
            .ToListAsync(ct);
        Check("travelNeedingReview", travel.Count(t =>
            t.TimesheetId == null || t.Status == TimesheetStatus.Submitted), true);

        Check("pendingAdditionalPad", await db.AdditionalPadRequests.CountAsync(r =>
            r.ProjectId == projectId && r.Year == year && r.Month == month
            && (r.Status == AdditionalPadStatus.Draft || r.Status == AdditionalPadStatus.Submitted), ct),
            true);

        var txs = allTxs.Where(t => t.Year == year && t.Month == month).ToList();
        var sums = PadMath.Sum(txs);
        Check("negativeFree", sums.Free < 0 ? 1 : 0, true);

        // ── Calculation ────────────────────────────────────────────────────
        // Commitments that never utilised die with the month rather than
        // dragging into the next one (only reachable when forced past
        // unresolved bookings, or for orphaned commitments).
        var releases = txs.Where(t => t.BookingId != null)
            .GroupBy(t => t.BookingId!.Value)
            .Select(g => (BookingId: g.Key,
                Open: g.Where(t => t.Type == PadTransactionType.Commit).Sum(t => t.Amount)
                    - g.Where(t => t.Type == PadTransactionType.Release).Sum(t => t.Amount)
                    - g.Where(t => t.Type == PadTransactionType.Utilise).Sum(t => t.Amount)))
            .Where(x => x.Open > 0)
            .ToList();
        var released = releases.Sum(r => r.Open);

        var terms = await TermsForAsync(projectId, monthEnd, ct);
        var maxPercent = terms?.CarryOverMaxPercent ?? 20m;
        var carryMonths = Math.Max(1, terms?.CarryOverMonths ?? 1);

        var carryIn = sums.CarryOverIn;
        var baselineUsed = PadMath.BaselineUsed(
            sums.Baseline, sums.Utilised, carryIn, sums.BorrowedFromThisMonth);
        var carryOver = PadMath.CarryOver(
            sums.Baseline, sums.Utilised, maxPercent, carryIn, sums.BorrowedFromThisMonth);
        var expiredBaseline = PadMath.Expiry(
            sums.Baseline, sums.Utilised, maxPercent, carryIn, sums.BorrowedFromThisMonth);
        var cap = Math.Ceiling(sums.Baseline * maxPercent / 100m);

        var reCarries = new List<(int, int, decimal)>();
        var residualExpiries = new List<(int, int, decimal)>();
        foreach (var r in PadMath.CarryResiduals(allTxs, year, month))
        {
            var age = closingIndex - (r.OriginYear * 12 + r.OriginMonth);
            if (age < carryMonths) reCarries.Add((r.OriginYear, r.OriginMonth, r.Residual));
            else residualExpiries.Add((r.OriginYear, r.OriginMonth, r.Residual));
        }

        var carryConsumed = Math.Min(sums.Utilised, carryIn);
        var steps = new List<CloseStepDto>
        {
            Step("baseline", sums.Baseline, $"Baseline {sums.Baseline:0.####}"),
            Step("carryOverIn", carryIn, $"Carry-over in {carryIn:0.####}"),
            Step("utilised", sums.Utilised, $"Utilised {sums.Utilised:0.####}"),
            Step("carryConsumed", carryConsumed,
                $"Carry-over consumed first: min({sums.Utilised:0.####}, {carryIn:0.####}) = {carryConsumed:0.####}"),
            Step("baselineUsed", baselineUsed,
                $"Baseline used: {sums.Utilised:0.####} − {carryConsumed:0.####} + {sums.BorrowedFromThisMonth:0.####} borrowed = {baselineUsed:0.####}"),
            Step("unusedBaseline", sums.Baseline - baselineUsed,
                $"Unused baseline: {sums.Baseline:0.####} − {baselineUsed:0.####} = {sums.Baseline - baselineUsed:0.####}"),
            Step("carryCap", cap,
                $"Carry-over cap: ⌈{sums.Baseline:0.####} × {maxPercent:0.##} %⌉ = {cap:0.####}"),
            Step("carryOverOut", carryOver,
                $"Carry-over to {next.Year}-{next.Month:D2}: min(unused, cap) = {carryOver:0.####}"),
            Step("expiredBaseline", expiredBaseline,
                $"Expired unused baseline: {sums.Baseline - baselineUsed:0.####} − {carryOver:0.####} = {expiredBaseline:0.####}"),
        };
        foreach (var (y, m, amount) in reCarries)
            steps.Add(Step("reCarry", amount, $"Re-carry of {y}-{m:D2} residual {amount:0.####}"));
        foreach (var (y, m, amount) in residualExpiries)
            steps.Add(Step("carryExpired", amount, $"Carry-over from {y}-{m:D2} expires: {amount:0.####}"));
        if (released > 0)
            steps.Add(Step("released", released, $"Open commitments released: {released:0.####}"));

        var carriedOut = carryOver + reCarries.Sum(r => r.Item3);
        var expired = expiredBaseline + residualExpiries.Sum(r => r.Item3);
        var result = new CloseMonthResultDto(
            sums.Baseline, sums.Utilised, released, carriedOut, expired,
            carriedOut, next.Year, next.Month);

        var preview = new ClosePreviewDto(
            year, month,
            CanClose: errors.Count == 0 && !checklist.Any(i => i.Blocking),
            errors.ToArray(), checklist, steps, result);
        return new ClosePlan(preview, releases, reCarries, residualExpiries,
            carryOver, expiredBaseline);
    }

    private static CloseStepDto Step(string code, decimal amount, FormattableString text) =>
        new(code, amount, text.ToString(Inv));

    public record CommitResult(bool Ok, decimal FreeAfter, decimal Amount, bool MonthClosed);
}

/// <summary>Close refused — ordering errors or unforced blocking checklist items.</summary>
public class CloseBlockedException(ClosePreviewDto preview)
    : InvalidOperationException("Month close is blocked.")
{
    public ClosePreviewDto Preview { get; } = preview;
}
