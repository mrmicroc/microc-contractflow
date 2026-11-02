using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Services;

/// <summary>A mobilisation period's place in the contract's visit count.</summary>
/// <param name="Number">1-based, chronological over the whole contract
/// (start date, then confirmation time) — each resource's mobilisation is its
/// own visit, so two inspectors on one trip are two visits.</param>
/// <param name="Included">The terms' included-visit limit in force at the
/// period start (null = not limited).</param>
public record VisitInfo(Guid PeriodId, int Number, int? Included)
{
    public bool WithinLimit => Included is null || Number <= Included.Value;
}

/// <summary>
/// Scope rules carried by work packages: a package outside the contract's
/// scope is additional work (billed by time, no PAD commitment, travel
/// billed), and mobilisations to packages marked as visits count against the
/// contract's included visits.
/// </summary>
public static class WorkPackageRules
{
    /// <summary>True when the booking's work package lies outside the
    /// contract's scope — such a booking confirms without committing PAD.</summary>
    public static Task<bool> IsOutsideContractAsync(
        AppDbContext db, Guid? workPackageId, CancellationToken ct) =>
        workPackageId is null
            ? Task.FromResult(false)
            : db.WorkPackages.AnyAsync(w => w.Id == workPackageId
                && w.PricingMode == WorkPackagePricing.OutsideContract, ct);

    /// <summary>Numbers every visit of the contract (confirmed mobilisation
    /// periods to packages with <see cref="WorkPackage.CountsAsVisit"/>).</summary>
    public static async Task<Dictionary<Guid, VisitInfo>> NumberVisitsAsync(
        AppDbContext db, Guid contractId, CancellationToken ct)
    {
        var periods = await db.MobilisationPeriods
            .Where(p => p.Project.ContractId == contractId
                && p.WorkPackage != null && p.WorkPackage.CountsAsVisit)
            .Select(p => new { p.Id, p.StartDate, p.ConfirmedUtc })
            .ToListAsync(ct);
        var terms = await db.ContractTerms.Where(t => t.ContractId == contractId).ToListAsync(ct);
        return periods
            .OrderBy(p => p.StartDate).ThenBy(p => p.ConfirmedUtc).ThenBy(p => p.Id)
            .Select((p, i) => new VisitInfo(p.Id, i + 1,
                DayBilling.TermsAt(terms, contractId, p.StartDate)?.IncludedVisits))
            .ToDictionary(v => v.PeriodId);
    }

    /// <summary>A period whose travel bills: it mobilised outside the
    /// contract's scope, or it is a visit beyond the included limit.</summary>
    public static bool TravelBillable(MobilisationPeriod p, VisitInfo? visit) =>
        p.WorkPackage?.PricingMode == WorkPackagePricing.OutsideContract
        || visit is { WithinLimit: false };
}
