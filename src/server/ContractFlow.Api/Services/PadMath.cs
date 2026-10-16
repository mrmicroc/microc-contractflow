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
        return Math.Max(minPadPerBookedDay, plannedHours / padHoursPerDay);
    }

    /// <summary>
    /// Carry-over at month close (spec example: baseline 10, used 7 → 2 carried
    /// — 20 % of baseline rounded up to whole PAD — and 1 expires). Only unused
    /// baseline can carry; carry-over is capped at the unused amount.
    /// </summary>
    public static decimal CarryOver(decimal baseline, decimal utilised, decimal maxPercent)
    {
        var unused = Math.Max(0m, baseline - utilised);
        var cap = Math.Ceiling(baseline * maxPercent / 100m);
        return Math.Min(unused, Math.Max(0m, cap));
    }

    /// <summary>PAD that dies at close: unused baseline that did not carry over.</summary>
    public static decimal Expiry(decimal baseline, decimal utilised, decimal maxPercent)
        => Math.Max(0m, baseline - utilised) - CarryOver(baseline, utilised, maxPercent);

    /// <summary>
    /// Residual per carry-over origin alive in <paramref name="closingYear"/>/
    /// <paramref name="closingMonth"/>: walks the ledger chronologically —
    /// each month's utilisation eats that month's own pool (baseline +
    /// additional + adjustments + borrowings) first, then living carry-over
    /// rows oldest-origin-first. Returned residuals are what carry rows
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
            var ownPool = sums.Baseline + sums.AdditionalApproved + sums.Adjustments
                + sums.BorrowedFromThisMonth;
            var eatCarry = Math.Max(0m, sums.Utilised - ownPool);

            // New carry rows landing this month become living (in origin order).
            foreach (var c in g.Where(t => t.Type == PadTransactionType.CarryOverIn))
            {
                var origin = (c.OriginYear, c.OriginMonth) is (int y, int m)
                    ? (y, m)
                    : PrevMonth(g.Key.Year, g.Key.Month);
                residuals[origin] = residuals.GetValueOrDefault(origin) + c.Amount;
            }
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
