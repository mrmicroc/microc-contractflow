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
