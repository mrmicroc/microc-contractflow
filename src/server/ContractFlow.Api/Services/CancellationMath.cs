using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Services;

/// <summary>
/// Cancellation-charge resolution (design §4.3): a versioned ladder of
/// notice-period rungs instead of a single binary deadline. Pure arithmetic —
/// how the charge materialises (PAD utilisation vs. invoice line) is the
/// caller's job.
/// </summary>
public static class CancellationMath
{
    /// <summary>
    /// Resolves the charge for cancelling <paramref name="businessDaysBefore"/>
    /// business days ahead. The ladder is matched from the loosest rung down:
    /// the first step whose threshold is met applies. Below the lowest rung
    /// the booking bills in full (<paramref name="plannedHours"/>). An empty
    /// ladder falls back to the legacy binary deadline.
    /// </summary>
    public static CancellationCharge Resolve(
        IReadOnlyList<CancellationStep>? steps,
        int businessDaysBefore,
        decimal plannedHours,
        int fallbackDeadlineDays)
    {
        if (steps is { Count: > 0 })
        {
            CancellationStep? hit = steps
                .Where(s => businessDaysBefore >= s.MinBusinessDaysBefore)
                .OrderBy(s => s.MinBusinessDaysBefore)   // tightest matching rung
                .LastOrDefault();
            if (hit is null) // less notice than the lowest rung → full charge
                return new CancellationCharge(plannedHours, null, Full: true);
            // A percentage rung charges that share of the full booking: of the
            // planned hours (hourly / PAD) or of the day (day rate).
            var share = hit.ChargePercent is > 0m ? hit.ChargePercent.Value / 100m : (decimal?)null;
            if (share >= 1m && hit.ChargeHours is null && hit.ChargeDayFraction is null)
                return new CancellationCharge(plannedHours, hit.ChargeAmount, Full: true);
            return new CancellationCharge(
                hit.ChargeHours ?? (share is { } s ? Math.Round(plannedHours * s, 2, MidpointRounding.AwayFromZero) : null),
                hit.ChargeAmount,
                DayFraction: hit.ChargeDayFraction ?? share);
        }

        var timely = businessDaysBefore >= fallbackDeadlineDays;
        return timely
            ? new CancellationCharge(null, null)
            : new CancellationCharge(plannedHours, null, Full: true);
    }

    /// <param name="Hours">Billed hours (PAD contracts convert to PAD
    /// utilisation; null = no hour charge).</param>
    /// <param name="Amount">Fixed cancellation fee (null = none).</param>
    /// <param name="Full">The whole booking is charged (missed deadline / below
    /// the lowest rung) — on PAD contracts the full commitment is consumed,
    /// never less than the 1.00 PAD minimum (§4.3, §6.3); on hourly contracts
    /// the daily minimum applies; on day-rate contracts a full (or half) day.</param>
    /// <param name="DayFraction">Day-rate contracts: the rung's share of the
    /// day rate (0–1).</param>
    public record CancellationCharge(
        decimal? Hours, decimal? Amount, bool Full = false, decimal? DayFraction = null)
    {
        public bool IsFree => Hours is not > 0m && Amount is not > 0m && DayFraction is not > 0m;
    }
}
