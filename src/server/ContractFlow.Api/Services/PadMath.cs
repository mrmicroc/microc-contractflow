using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Services;

/// <summary>
/// Pure PAD arithmetic — the commercial rules of design §4.3 expressed as
/// deterministic functions over ledger transactions. Every function here is
/// covered by tests built on the spec's worked examples.
/// </summary>
public static class PadMath
{
    /// <summary>
    /// PAD committed when a Booked Day is confirmed: min 1.00 PAD per inspector
    /// (configurable), longer days proportionally more — hours / hours-per-PAD.
    /// </summary>
    public static decimal CommitAmount(decimal plannedHours, decimal padHoursPerDay, decimal minPadPerBookedDay)
    {
        if (padHoursPerDay <= 0) padHoursPerDay = 8m;
        return Round4(Math.Max(minPadPerBookedDay, plannedHours / padHoursPerDay));
    }

    /// <summary>Ledger precision — PAD amounts are stored with 4 decimals, so
    /// computed amounts round here rather than silently in the database.</summary>
    public static decimal Round4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Baseline actually consumed in a month. Carry-over is consumed before
    /// baseline (§4.3), so utilisation first eats the month's carry-in; PAD
    /// lent to the previous month's overrun (BorrowNextMonth) eats baseline
    /// directly.
    /// </summary>
    public static decimal BaselineUsed(
        decimal baseline, decimal utilised, decimal carryIn = 0m, decimal borrowed = 0m) =>
        Math.Min(baseline, Math.Max(0m, utilised - carryIn) + borrowed);

    /// <summary>
    /// Carry-over at month close (spec example: baseline 10, used 7 → 2 carried
    /// — 20 % of baseline rounded up to whole PAD — and 1 expires). Only unused
    /// baseline can carry; carry-over is capped at the unused amount. Carry-in
    /// is consumed first: baseline 10 + carry-in 2 with 11 utilised leaves 1
    /// unused baseline → 1 carries, nothing expires.
    /// </summary>
    public static decimal CarryOver(decimal baseline, decimal utilised, decimal maxPercent,
        decimal carryIn = 0m, decimal borrowed = 0m)
    {
        var unused = baseline - BaselineUsed(baseline, utilised, carryIn, borrowed);
        var cap = Math.Ceiling(baseline * maxPercent / 100m);
        return Math.Min(Math.Max(0m, unused), Math.Max(0m, cap));
    }

    /// <summary>PAD that dies at close: unused baseline that did not carry over.</summary>
    public static decimal Expiry(decimal baseline, decimal utilised, decimal maxPercent,
        decimal carryIn = 0m, decimal borrowed = 0m)
        => Math.Max(0m, baseline - BaselineUsed(baseline, utilised, carryIn, borrowed))
           - CarryOver(baseline, utilised, maxPercent, carryIn, borrowed);

    /// <summary>
    /// Continuation beyond commitment (§4.3): a confirmed Booked Day overruns
    /// by <paramref name="extraPad"/>. Sources in order: ① remaining
    /// uncommitted current-month PAD (excluding approved Additional) → ②
    /// approved Additional PAD → ③ next-month baseline (reduces that month's
    /// free; flags a conflict when it goes negative) → ④ auto-incurred
    /// Additional PAD. <paramref name="additionalAvailable"/> is the unused
    /// approved Additional PAD inside the current month's free;
    /// <paramref name="next"/> is null when next month cannot lend (closed).
    /// </summary>
    public static OverrunSplit AllocateOverrun(
        decimal extraPad, PadMonthSums current, PadMonthSums? next, decimal additionalAvailable)
    {
        var rest = Math.Max(0m, extraPad);
        var free = Math.Max(0m, current.Free);
        var additional = Math.Min(Math.Max(0m, additionalAvailable), free);

        var fromFree = Math.Min(rest, free - additional);
        rest -= fromFree;
        var fromAdditional = Math.Min(rest, additional);
        rest -= fromAdditional;

        var lendable = next is null ? 0m : Math.Max(0m, next.Baseline - next.BorrowedFromThisMonth);
        var fromNext = Math.Min(rest, lendable);
        rest -= fromNext;

        var conflict = next is not null && fromNext > 0m && next.Free - fromNext < 0m;
        return new OverrunSplit(
            Round4(fromFree), Round4(fromAdditional), Round4(fromNext), Round4(rest), conflict);
    }

    /// <summary>
    /// Residual per carry-over origin alive in <paramref name="closingYear"/>/
    /// <paramref name="closingMonth"/>: walks the ledger chronologically —
    /// each month's utilisation eats living carry-over rows oldest-origin-first
    /// before the month's own pool (carry before baseline, §4.3). Returned residuals are what carry rows
    /// targeted at the closing month still hold; the caller decides
    /// re-carry vs. expiry per origin age (ContractTerms.CarryOverMonths).
    /// </summary>
    public static List<(int OriginYear, int OriginMonth, decimal Residual)>
        CarryResiduals(IEnumerable<PadTransaction> transactions, int closingYear, int closingMonth)
    {
        // origin → residual alive; carry rows without an explicit origin are
        // treated as carried from the month before they landed (pre-lineage).
        var residuals = new Dictionary<(int Y, int M), decimal>();
        var byMonth = transactions
            .GroupBy(t => (t.Year, t.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month);

        foreach (var g in byMonth)
        {
            if (g.Key.Year > closingYear || g.Key.Year == closingYear && g.Key.Month > closingMonth)
                continue; // only history up to and including the closing month

            var sums = Sum(g);

            // New carry rows landing this month become living (in origin order).
            foreach (var c in g.Where(t => t.Type == PadTransactionType.CarryOverIn))
            {
                var origin = (c.OriginYear, c.OriginMonth) is (int y, int m)
                    ? (y, m)
                    : PrevMonth(g.Key.Year, g.Key.Month);
                residuals[origin] = residuals.GetValueOrDefault(origin) + c.Amount;
            }
            // Carry-over is consumed before baseline (§4.3): utilisation eats
            // living carry first; only the overflow reaches the month's pool.
            var eatCarry = Math.Min(sums.Utilised, residuals.Values.Sum());
            // Allocate this month's carry consumption to the oldest origins.
            foreach (var origin in residuals.Keys.OrderBy(k => k.Y * 12 + k.M).ToList())
            {
                if (eatCarry <= 0) break;
                var take = Math.Min(residuals[origin], eatCarry);
                residuals[origin] -= take;
                eatCarry -= take;
            }
            // Re-carry / expiry rows written at close move the residual out of
            // the living pool (the paired next-month In re-adds it). Fresh
            // baseline carry-outs convert baseline — no residual to subtract.
            foreach (var o in g.Where(t =>
                t.Type is PadTransactionType.CarryOverOut or PadTransactionType.Expire
                && t.OriginYear != null))
            {
                var origin = (o.OriginYear!.Value, o.OriginMonth!.Value);
                if (residuals.ContainsKey(origin))
                    residuals[origin] = Math.Max(0m, residuals[origin] - o.Amount);
            }
            residuals.Keys.Where(k => residuals[k] <= 0m).ToList()
                .ForEach(k => residuals.Remove(k));
        }

        return residuals
            .OrderBy(k => k.Key.Y * 12 + k.Key.M)
            .Select(k => (k.Key.Y, k.Key.M, k.Value))
            .ToList();
    }

    private static (int Y, int M) PrevMonth(int year, int month) =>
        month == 1 ? (year - 1, 12) : (year, month - 1);

    /// <summary>Sums of each ledger category for a project-month.</summary>
    public static PadMonthSums Sum(IEnumerable<PadTransaction> transactions)
    {
        decimal SumType(PadTransactionType t) =>
            transactions.Where(x => x.Type == t).Sum(x => x.Amount);

        return new PadMonthSums(
            // BaselineSet rows are deltas — a "set baseline to X" writes (X − current).
            Baseline: SumType(PadTransactionType.BaselineSet),
            CarryOverIn: SumType(PadTransactionType.CarryOverIn),
            AdditionalApproved: SumType(PadTransactionType.AdditionalApproved)
                        + SumType(PadTransactionType.AutoAdditional),
            CommittedOpen: SumType(PadTransactionType.Commit)
                        - SumType(PadTransactionType.Release)
                        - SumType(PadTransactionType.Utilise),
            Utilised: SumType(PadTransactionType.Utilise),
            Released: SumType(PadTransactionType.Release),
            Expired: SumType(PadTransactionType.Expire),
            CarryOverOut: SumType(PadTransactionType.CarryOverOut),
            // BorrowNextMonth rows live in the LENDING month: they consume its
            // baseline (reducing its free) — never add to its pool.
            BorrowedFromThisMonth: SumType(PadTransactionType.BorrowNextMonth),
            Adjustments: SumType(PadTransactionType.Adjustment));
    }
}

public record PadMonthSums(
    decimal Baseline,
    decimal CarryOverIn,
    decimal AdditionalApproved,
    decimal CommittedOpen,
    decimal Utilised,
    decimal Released,
    decimal Expired,
    decimal CarryOverOut,
    decimal BorrowedFromThisMonth,
    decimal Adjustments)
{
    /// <summary>Total available = Baseline + CarryOverIn + AdditionalApproved (+ adjustments).</summary>
    public decimal TotalAvailable => Baseline + CarryOverIn + AdditionalApproved + Adjustments;

    /// <summary>
    /// Free = available − utilised − open commitments − expired − carried out
    /// − borrowed-from (design §4.2). May go negative only transiently.
    /// </summary>
    public decimal Free =>
        TotalAvailable - Utilised - CommittedOpen - Expired - CarryOverOut - BorrowedFromThisMonth;
}

/// <summary>Source split of an overrun (§4.3 ①–④) — always displayed.</summary>
public record OverrunSplit(
    decimal FromCurrentFree,
    decimal FromAdditional,
    decimal FromNextMonthBaseline,
    decimal AutoAdditional,
    bool NextMonthConflict)
{
    public decimal Total => FromCurrentFree + FromAdditional + FromNextMonthBaseline + AutoAdditional;
}
