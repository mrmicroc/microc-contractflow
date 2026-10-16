namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// A continuous attendance period for one resource on one project (design §3):
/// consecutive days of bookings/time entries, suggested by the system and
/// confirmed (or split/merged) by admin. May carry one customer mobilisation
/// fee per period — the fee comes from the contract terms in force, never from
/// actual cost (§5 worked example).
/// </summary>
public class MobilisationPeriod
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>The work package the period mobilised to — resolves package-level
    /// mobilisation-fee overrides (<see cref="WorkPackageRate"/>) at billing.
    /// Null = the contract-level fee.</summary>
    public Guid? WorkPackageId { get; set; }
    public WorkPackage? WorkPackage { get; set; }

    /// <summary>Only confirmed periods are persisted — suggested groupings are
    /// computed on demand by the suggest endpoint.</summary>
    public DateTime ConfirmedUtc { get; set; } = DateTime.UtcNow;
    public Guid? ConfirmedByUserId { get; set; }
}
