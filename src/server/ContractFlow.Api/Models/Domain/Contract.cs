namespace ContractFlow.Api.Models.Domain;

public class Contract
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public Guid? EndClientId { get; set; }
    /// <summary>The organisation the work is ultimately for, when the
    /// contract party (<see cref="Customer"/>) is an intermediary — e.g. a
    /// consultancy buying MICROC services for its own client. Null = direct
    /// contract: the customer IS the end client. Both come from the same
    /// organisation register, so one organisation can be the party on one
    /// contract and the end client on another.</summary>
    public Customer? EndClient { get; set; }
    /// <summary>The end client's own reference (order/project number) —
    /// carried onto the invoice specification for the intermediary.</summary>
    public string? EndClientReference { get; set; }

    public required string Name { get; set; }
    /// <summary>The customer's own contract/framework reference.</summary>
    public string? ExternalReference { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The customer's authorised representatives — who may request,
    /// confirm, cancel and reschedule booked days, approve Additional PAD and
    /// agree allocations between projects (JSON-owned list).</summary>
    public List<AuthorisedRepresentative> Representatives { get; set; } = [];

    public List<ContractTerms> TermsVersions { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
    /// <summary>One-off dated amounts (milestones, instalments).</summary>
    public List<ScheduledCharge> ScheduledCharges { get; set; } = [];
}

/// <summary>A person at the customer authorised to instruct MICROC under the
/// contract, with a free-text note on the scope of that authority.</summary>
public class AuthorisedRepresentative
{
    public required string Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>E.g. "Bookings and Additional PAD", "Operational only".</summary>
    public string? Authority { get; set; }
}
