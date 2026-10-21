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

    /// <summary>Open commitment for one booking: commits − releases − utilises
    /// (all projects of a shared day together).</summary>
    public async Task<decimal> OpenCommitmentAsync(Guid bookingId, CancellationToken ct) =>
        (await OpenCommitmentsAsync(bookingId, ct)).Values.Sum();

    /// <summary>Open commitment per project — a shared booked day commits in
    /// each sharing project's ledger.</summary>
    public async Task<Dictionary<Guid, decimal>> OpenCommitmentsAsync(Guid bookingId, CancellationToken ct)
    {
        var txs = await db.PadTransactions
            .Where(t => t.BookingId == bookingId && (t.Type == PadTransactionType.Commit
                || t.Type == PadTransactionType.Release || t.Type == PadTransactionType.Utilise))
            .ToListAsync(ct);
        return txs.GroupBy(t => t.ProjectId).ToDictionary(g => g.Key,
            g => g.Sum(t => t.Type == PadTransactionType.Commit ? t.Amount : -t.Amount));
    }

    /// <summary>The booking's project split: its shares, or the primary
    /// project with all planned hours.</summary>
    public async Task<List<(Guid ProjectId, decimal Hours)>> PlannedPartsAsync(
        Booking booking, CancellationToken ct)
    {
        var shares = await db.BookingProjectShares
            .Where(s => s.BookingId == booking.Id)
            .OrderBy(s => s.ProjectId != booking.ProjectId).ThenBy(s => s.ProjectId)
            .Select(s => new { s.ProjectId, s.PlannedHours })
            .ToListAsync(ct);
        return shares.Count > 1
            ? shares.Select(s => (s.ProjectId, s.PlannedHours)).ToList()
            : [(booking.ProjectId, booking.PlannedHours)];
    }

    /// <summary>
    /// Tries to reserve PAD for a booking being confirmed. Succeeds only when
    /// the month's free PAD covers the commitment — approved Additional PAD
    /// already counts into free, so partial availability works automatically
    /// (design §4.3). A shared booked day commits each project's planned
    /// share in its own ledger; one project short of PAD fails the whole
    /// confirm and names its shortfall.
    /// </summary>
    public async Task<CommitResult> TryCommitAsync(
        Booking booking, Guid? userId, CancellationToken ct)
    {
        var terms = await TermsForAsync(booking.ProjectId, booking.Date, ct);
        // No PAD component on the in-force terms → confirming is a pure status
        // change; the ledger is untouched (hourly/fixed-price contracts).
        if (terms?.PadEnabled != true)
            return new CommitResult(true, 0m, 0m, MonthClosed: false);

        var parts = await PlannedPartsAsync(booking, ct);
        var split = parts.Count == 1
            ? [(parts[0].ProjectId, PadMath.CommitAmount(parts[0].Hours, terms.PadHoursPerDay, terms.MinPadPerBookedDay))]
            : PadMath.SplitDay(parts, terms.PadHoursPerDay, terms.MinPadPerBookedDay, terms.SharedDayMinimum);
        var amount = split.Sum(s => s.Pad);

        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        foreach (var (projectId, _) in split)
            if (await MonthIsClosedAsync(projectId, month.Year, month.Month, ct))
                return new CommitResult(false, 0m, amount, MonthClosed: true);

        return await InTransactionAsync(async () =>
        {
            var shortfalls = new List<ProjectShortfall>();
            var freeAfter = 0m;
            foreach (var (projectId, pad) in split)
            {
                var sums = PadMath.Sum(await MonthAsync(projectId, month.Year, month.Month, ct));
                if (sums.Free < pad) shortfalls.Add(new ProjectShortfall(projectId, sums.Free, pad));
                if (projectId == booking.ProjectId) freeAfter = sums.Free - pad;
            }
            if (shortfalls.Count > 0)
            {
                var primary = shortfalls.FirstOrDefault(s => s.ProjectId == booking.ProjectId) ?? shortfalls[0];
                return new CommitResult(false, primary.Free, amount, MonthClosed: false, shortfalls);
            }

            foreach (var (projectId, pad) in split)
                await AppendAsync(projectId, month, PadTransactionType.Commit, pad,
                    userId, ct, bookingId: booking.Id,
                    note: split.Count > 1 ? "Shared booked day — this project's share" : null);
            return new CommitResult(true, freeAfter, amount, MonthClosed: false);
        }, ct);
    }

    /// <summary>Writes a Release or Utilise for the booking's open commitment
    /// in every project holding one.</summary>
    public async Task<decimal> SettleCommitmentAsync(
        Booking booking, PadTransactionType type, Guid? userId, string? note, CancellationToken ct)
    {
        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        var total = 0m;
        foreach (var (projectId, open) in await OpenCommitmentsAsync(booking.Id, ct))
        {
            if (open <= 0) continue;
            await AppendAsync(projectId, month, type, open, userId, ct,
                bookingId: booking.Id, note: note);
            total += open;
        }
        return total;
    }

    /// <summary>
    /// Partial-charge cancellation settlement: utilises
    /// <paramref name="utilisePad"/> PAD of the open commitment and releases
    /// the remainder (stepped cancellation policy, design §4.3). A shared day
    /// splits the charge across its projects by their committed (planned)
    /// shares.
    /// </summary>
    public async Task<(decimal Utilised, decimal Released)> SettlePartialAsync(
        Booking booking, decimal utilisePad, Guid? userId, CancellationToken ct)
    {
        var opens = (await OpenCommitmentsAsync(booking.Id, ct))
            .Where(o => o.Value > 0).OrderBy(o => o.Key != booking.ProjectId).ThenBy(o => o.Key).ToList();
        var open = opens.Sum(o => o.Value);
        if (open <= 0) return (0m, 0m);
        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        var used = PadMath.Round4(Math.Min(open, Math.Max(0m, utilisePad)));

        decimal usedAcc = 0m, releasedAcc = 0m;
        for (var i = 0; i < opens.Count; i++)
        {
            var (projectId, projectOpen) = (opens[i].Key, opens[i].Value);
            var part = i == opens.Count - 1
                ? used - usedAcc
                : PadMath.Round4(used * projectOpen / open);
            part = Math.Min(part, projectOpen);
            usedAcc += part;
            if (part > 0)
                await AppendAsync(projectId, month, PadTransactionType.Utilise,
                    part, userId, ct, bookingId: booking.Id,
                    note: "Cancellation — chargeable portion");
            var rest = projectOpen - part;
            releasedAcc += rest;
            if (rest > 0)
                await AppendAsync(projectId, month, PadTransactionType.Release,
                    rest, userId, ct, bookingId: booking.Id,
                    note: "Cancellation — released remainder");
        }
        return (usedAcc, releasedAcc);
    }

    /// <summary>
    /// Completion of a booked day (design §4.3 / continuation). Without actual
    /// hours the whole commitment utilises. With actual hours the day's PAD is
    /// recomputed — never below the commitment of a single-project day; a
    /// shared day re-splits by the actual time per project, so a project may
    /// utilise less than its planned share (remainder released) or more
    /// (overrun ①–④ in that project).
    /// </summary>
    public async Task<CompletionResult> CompleteAsync(
        Booking booking, decimal? actualHours, IReadOnlyDictionary<Guid, decimal>? actualByProject,
        Guid? userId, CancellationToken ct)
    {
        var opens = await OpenCommitmentsAsync(booking.Id, ct);
        var parts = await PlannedPartsAsync(booking, ct);
        var shared = parts.Count > 1;
        var terms = await TermsForAsync(booking.ProjectId, booking.Date, ct);
        var padHours = terms?.PadHoursPerDay ?? 8m;
        var minPad = terms?.MinPadPerBookedDay ?? 1m;
        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);

        if (!shared)
        {
            var used = await SettleCommitmentAsync(
                booking, PadTransactionType.Utilise, userId, "Completed booked day", ct);
            OverrunSplit? split = null;
            var actual = actualHours ?? actualByProject?.Values.Sum();
            if (used > 0 && actual is { } a)
            {
                var actualPad = PadMath.CommitAmount(a, padHours, minPad);
                if (actualPad > used)
                    split = await AllocateOverrunAsync(booking, booking.ProjectId, actualPad - used, userId, ct);
            }
            var perProject = new List<ProjectUtilisation>
            {
                new(booking.ProjectId, used + (split?.Total ?? 0m), split),
            };
            return new CompletionResult(used + (split?.Total ?? 0m), split, perProject);
        }

        // Shared day: actual per project from the body, else the resource's
        // reported time that date on the sharing projects, else the plan.
        var actuals = actualByProject is { Count: > 0 }
            ? parts.Select(p => (p.ProjectId, Hours: actualByProject.GetValueOrDefault(p.ProjectId))).ToList()
            : await ReportedPartsAsync(booking, parts, ct) ?? parts;
        var target = PadMath.SplitDay(actuals, padHours, minPad,
            terms?.SharedDayMinimum ?? SharedDayMinimum.Proportional);

        var results = new List<ProjectUtilisation>();
        OverrunSplit? anyOverrun = null;
        foreach (var (projectId, pad) in target)
        {
            var open = Math.Max(0m, opens.GetValueOrDefault(projectId));
            var use = Math.Min(open, pad);
            if (use > 0)
                await AppendAsync(projectId, month, PadTransactionType.Utilise, use, userId, ct,
                    bookingId: booking.Id, note: "Completed shared booked day — this project's actual share");
            if (open - use > 0)
                await AppendAsync(projectId, month, PadTransactionType.Release, open - use, userId, ct,
                    bookingId: booking.Id, note: "Shared day — planned share above actual released");
            OverrunSplit? split = null;
            if (pad - use > 0)
                split = anyOverrun = await AllocateOverrunAsync(booking, projectId, pad - use, userId, ct);
            results.Add(new ProjectUtilisation(projectId, use + (split?.Total ?? 0m), split));
        }
        // Commitments on projects that dropped out of the split are released.
        foreach (var (projectId, open) in opens.Where(o => o.Value > 0 && target.All(t => t.ProjectId != o.Key)))
            await AppendAsync(projectId, month, PadTransactionType.Release, open, userId, ct,
                bookingId: booking.Id, note: "Shared day — no time on this project");
        return new CompletionResult(results.Sum(r => r.Utilised),
            results.Count == 1 ? anyOverrun : results.FirstOrDefault(r => r.Overrun is not null)?.Overrun,
            results);
    }

    /// <summary>
    /// Timesheet approval hook: approved time without a booked day on terms
    /// whose PAD component has <see cref="ContractTerms.UnbookedTimeConsumesPad"/>
    /// utilises hours (rounded up to the recording increment) ÷ hours-per-PAD
    /// in its project-month. Free PAD is used first; a shortfall becomes
    /// AutoAdditional flagged <c>RequiresApproval</c> — new services beyond
    /// the allocation need the customer's Additional PAD approval. Rows that
    /// force plain-hour billing, or that fall inside a shared booked day,
    /// never consume here. Idempotent per entry (net consumption checked).
    /// </summary>
    public async Task<decimal> ConsumeUnbookedAsync(
        IEnumerable<TimeEntry> entries, Guid? userId, CancellationToken ct)
    {
        var total = 0m;
        foreach (var e in entries.Where(e => e.BookingId == null
                     && e.Billing == EntryBilling.Contract
                     && e.Category != TimeEntryCategory.Break && e.ActualHours > 0))
        {
            var terms = await TermsForAsync(e.ProjectId, e.Date, ct);
            if (terms is not { PadEnabled: true, UnbookedTimeConsumesPad: true }) continue;
            if (await InSharedBookedDayAsync(e.ResourceId, e.ProjectId, e.Date, ct)) continue;
            // Work at a package outside the contract's scope bills by time.
            if (await WorkPackageRules.IsOutsideContractAsync(db, e.WorkPackageId, ct)) continue;
            var net = (await db.PadTransactions
                .Where(t => t.TimeEntryId == e.Id && t.Type == PadTransactionType.Utilise)
                .Select(t => t.Amount).ToListAsync(ct)).Sum();
            if (net > 0) continue;

            var pad = PadMath.UnbookedPad(e.ActualHours, terms.UnbookedTimeIncrementMinutes, terms.PadHoursPerDay);
            if (pad <= 0) continue;
            var month = new DateOnly(e.Date.Year, e.Date.Month, 1);
            var sums = PadMath.Sum(await MonthAsync(e.ProjectId, month.Year, month.Month, ct));
            var shortfall = PadMath.Round4(pad - Math.Min(pad, Math.Max(0m, sums.Free)));
            if (shortfall > 0)
                db.PadTransactions.Add(Row(e, month, PadTransactionType.AutoAdditional, shortfall, userId,
                    "New services beyond free PAD — Additional PAD approval required", requiresApproval: true));
            db.PadTransactions.Add(Row(e, month, PadTransactionType.Utilise, pad, userId,
                string.Create(Inv, $"Unbooked services {e.ActualHours:0.##} h → {pad:0.####} PAD")));
            await db.SaveChangesAsync(ct);
            total += pad;
        }
        return total;
    }

    /// <summary>
    /// Reopened timesheet: the PAD its unbooked rows consumed is given back by
    /// appending negative Utilise / AutoAdditional rows for the same entries —
    /// never by deleting history. Re-approval consumes afresh.
    /// </summary>
    public async Task ReverseUnbookedAsync(IEnumerable<Guid> entryIds, Guid? userId, CancellationToken ct)
    {
        var ids = entryIds.ToList();
        var rows = await db.PadTransactions
            .Where(t => t.TimeEntryId != null && ids.Contains(t.TimeEntryId!.Value)
                && (t.Type == PadTransactionType.Utilise || t.Type == PadTransactionType.AutoAdditional))
            .ToListAsync(ct);
        foreach (var g in rows.GroupBy(t => (t.TimeEntryId!.Value, t.Type, t.ProjectId, t.Year, t.Month)))
        {
            var net = g.Sum(t => t.Amount);
            if (net == 0m) continue;
            db.PadTransactions.Add(new PadTransaction
            {
                ProjectId = g.Key.ProjectId, Year = g.Key.Year, Month = g.Key.Month,
                Type = g.Key.Type, Amount = -net, TimeEntryId = g.Key.Item1,
                RequiresApproval = g.Any(t => t.RequiresApproval),
                Note = "Reversed — timesheet reopened", CreatedByUserId = userId,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>True when the resource holds a live shared booked day that
    /// date covering the project — its time utilises through the booking.</summary>
    public Task<bool> InSharedBookedDayAsync(Guid resourceId, Guid projectId, DateOnly date, CancellationToken ct) =>
        db.BookingProjectShares.AnyAsync(s => s.ProjectId == projectId
            && s.Booking.ResourceId == resourceId && s.Booking.Date == date
            && s.Booking.Status != BookingStatus.Cancelled && s.Booking.Status != BookingStatus.Rescheduled
            && s.Booking.Status != BookingStatus.Forecast && s.Booking.Status != BookingStatus.Tentative
            && s.Booking.Status != BookingStatus.Requested, ct);

    private static PadTransaction Row(TimeEntry e, DateOnly month, PadTransactionType type, decimal amount,
        Guid? userId, string note, bool requiresApproval = false) => new()
    {
        ProjectId = e.ProjectId, Year = month.Year, Month = month.Month, Type = type,
        Amount = PadMath.Round4(amount), TimeEntryId = e.Id, RequiresApproval = requiresApproval,
        Note = note, CreatedByUserId = userId,
    };

    /// <summary>The resource's counted time that date on the sharing
    /// projects — null when nothing is reported yet.</summary>
    private async Task<List<(Guid ProjectId, decimal Hours)>?> ReportedPartsAsync(
        Booking booking, List<(Guid ProjectId, decimal Hours)> parts, CancellationToken ct)
    {
        var projectIds = parts.Select(p => p.ProjectId).ToList();
        var rows = await db.TimeEntries
            .Where(e => e.ResourceId == booking.ResourceId && e.Date == booking.Date
                && projectIds.Contains(e.ProjectId)
                && (e.BookingId == booking.Id || e.BookingId == null)
                && e.Category != TimeEntryCategory.Break)
            .Select(e => new { e.ProjectId, e.ActualHours })
            .ToListAsync(ct);
        if (rows.Count == 0) return null;
        return parts.Select(p => (p.ProjectId,
            rows.Where(r => r.ProjectId == p.ProjectId).Sum(r => r.ActualHours))).ToList();
    }

    /// <summary>
    /// Continuation beyond commitment (§4.3): the booking's actual PAD exceeds
    /// its commitment by <paramref name="extraPad"/>. Allocates ① current free
    /// → ② approved Additional → ③ next-month baseline (BorrowNextMonth row in
    /// the lending month) → ④ auto-incurred Additional, and writes the ledger
    /// rows. Returns the split for display.
    /// </summary>
    public async Task<OverrunSplit> AllocateOverrunAsync(
        Booking booking, Guid projectId, decimal extraPad, Guid? userId, CancellationToken ct)
    {
        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        var next = month.AddMonths(1);
        var currentTxs = await MonthAsync(projectId, month.Year, month.Month, ct);
        var current = PadMath.Sum(currentTxs);
        PadMonthSums? nextSums = await MonthIsClosedAsync(projectId, next.Year, next.Month, ct)
            ? null
            : PadMath.Sum(await MonthAsync(projectId, next.Year, next.Month, ct));

        // Unused approved Additional PAD: what utilisation beyond the
        // month's own pool (baseline + carry + adjustments) hasn't eaten yet.
        var ownPool = current.Baseline + current.CarryOverIn + current.Adjustments;
        var additionalUsed = Math.Max(0m, current.Utilised + current.CommittedOpen - ownPool);
        var additionalAvailable = Math.Max(0m, current.AdditionalApproved - additionalUsed);

        var split = PadMath.AllocateOverrun(extraPad, current, nextSums, additionalAvailable);
        var inMonth = split.FromCurrentFree + split.FromAdditional + split.AutoAdditional;
        if (split.AutoAdditional > 0)
            await AppendAsync(projectId, month, PadTransactionType.AutoAdditional,
                split.AutoAdditional, userId, ct, bookingId: booking.Id,
                note: "Continuation overflow — auto-incurred Additional PAD");
        if (inMonth > 0)
            await AppendAsync(projectId, month, PadTransactionType.Utilise,
                inMonth, userId, ct, bookingId: booking.Id,
                note: string.Create(Inv,
                    $"Continuation beyond commitment: {split.FromCurrentFree:0.####} free + {split.FromAdditional:0.####} additional + {split.AutoAdditional:0.####} auto-additional"));
        if (split.FromNextMonthBaseline > 0)
            await AppendAsync(projectId, next, PadTransactionType.BorrowNextMonth,
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

        // New services beyond free PAD need the customer's approval — blocking
        // until approved Additional PAD requests for the month cover them.
        var awaitingApproval = txs.Where(t => t.Type == PadTransactionType.AutoAdditional && t.RequiresApproval)
            .Sum(t => t.Amount);
        var approvedQty = (await db.AdditionalPadRequests
            .Where(r => r.ProjectId == projectId && r.Year == year && r.Month == month
                && r.Status == AdditionalPadStatus.Approved)
            .Select(r => r.Quantity).ToListAsync(ct)).Sum();
        Check("additionalPadAwaitingApproval", awaitingApproval > approvedQty ? 1 : 0, true);

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
        var policy = CarryOverPolicy.From(terms);
        var maxPercent = policy.MaxPercent;
        var carryMonths = Math.Max(1, terms?.CarryOverMonths ?? 1);

        var carryIn = sums.CarryOverIn;
        var baselineUsed = PadMath.BaselineUsed(
            sums.Baseline, sums.Utilised, carryIn, sums.BorrowedFromThisMonth, policy.Consumption);
        var carryOver = PadMath.CarryOver(
            sums.Baseline, sums.Utilised, policy, carryIn, sums.BorrowedFromThisMonth);
        var expiredBaseline = PadMath.Expiry(
            sums.Baseline, sums.Utilised, policy, carryIn, sums.BorrowedFromThisMonth);
        var unusedBaseline = Math.Max(0m, sums.Baseline - baselineUsed);
        var cap = PadMath.CarryCap(sums.Baseline, unusedBaseline, policy);
        var capBasisText = policy.Basis == CarryOverBasis.Baseline
            ? string.Create(Inv, $"baseline {sums.Baseline:0.####}")
            : string.Create(Inv, $"unused baseline {unusedBaseline:0.####}");
        var capRoundText = (policy.RoundingStep > 0m
                ? string.Create(Inv, $" rounded {policy.Rounding.ToString().ToLowerInvariant()} to {policy.RoundingStep:0.##}")
                : "")
            + (policy.MaxPad is { } ceiling ? string.Create(Inv, $", at most {ceiling:0.##}") : "");

        var reCarries = new List<(int, int, decimal)>();
        var residualExpiries = new List<(int, int, decimal)>();
        foreach (var r in PadMath.CarryResiduals(allTxs, year, month, policy.Consumption))
        {
            var age = closingIndex - (r.OriginYear * 12 + r.OriginMonth);
            if (age < carryMonths) reCarries.Add((r.OriginYear, r.OriginMonth, r.Residual));
            else residualExpiries.Add((r.OriginYear, r.OriginMonth, r.Residual));
        }

        var baselineFirst = policy.Consumption == CarryOverConsumption.BaselineFirst;
        var carryConsumed = baselineFirst
            ? Math.Min(carryIn, Math.Max(0m, sums.Utilised - (sums.Baseline - sums.BorrowedFromThisMonth)))
            : Math.Min(sums.Utilised, carryIn);
        var steps = new List<CloseStepDto>
        {
            Step("baseline", sums.Baseline, $"Baseline {sums.Baseline:0.####}"),
            Step("carryOverIn", carryIn, $"Carry-over in {carryIn:0.####}"),
            Step("utilised", sums.Utilised, $"Utilised {sums.Utilised:0.####}"),
            Step("carryConsumed", carryConsumed, baselineFirst
                ? (FormattableString)$"Baseline consumed first; carry-over takes the rest: {carryConsumed:0.####}"
                : $"Carry-over consumed first: min({sums.Utilised:0.####}, {carryIn:0.####}) = {carryConsumed:0.####}"),
            Step("baselineUsed", baselineUsed, baselineFirst
                ? (FormattableString)$"Baseline used: min({sums.Baseline:0.####}, {sums.Utilised:0.####} + {sums.BorrowedFromThisMonth:0.####} borrowed) = {baselineUsed:0.####}"
                : $"Baseline used: {sums.Utilised:0.####} − {carryConsumed:0.####} + {sums.BorrowedFromThisMonth:0.####} borrowed = {baselineUsed:0.####}"),
            Step("unusedBaseline", sums.Baseline - baselineUsed,
                $"Unused baseline: {sums.Baseline:0.####} − {baselineUsed:0.####} = {sums.Baseline - baselineUsed:0.####}"),
            Step("carryCap", cap,
                $"Carry-over cap: {maxPercent:0.##} % of {capBasisText}{capRoundText} = {cap:0.####}"),
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

    public record CommitResult(bool Ok, decimal FreeAfter, decimal Amount, bool MonthClosed,
        IReadOnlyList<ProjectShortfall>? Shortfalls = null);

    /// <summary>A sharing project whose free PAD can't cover its share.</summary>
    public record ProjectShortfall(Guid ProjectId, decimal Free, decimal Needed);

    public record ProjectUtilisation(Guid ProjectId, decimal Utilised, OverrunSplit? Overrun);

    public record CompletionResult(decimal Utilised, OverrunSplit? Overrun,
        IReadOnlyList<ProjectUtilisation> PerProject);
}

/// <summary>Close refused — ordering errors or unforced blocking checklist items.</summary>
public class CloseBlockedException(ClosePreviewDto preview)
    : InvalidOperationException("Month close is blocked.")
{
    public ClosePreviewDto Preview { get; } = preview;
}
