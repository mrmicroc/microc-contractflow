namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// Booking status flow (design §3):
/// Forecast → Tentative → Requested → <b>Confirmed</b> (a Booked Day — commits
/// PAD) → Acknowledged → InProgress → Completed. Cancelled/Rescheduled are
/// terminal. Only Confirmed bookings touch the PAD ledger.
/// </summary>
public enum BookingStatus
{
    Forecast,
    Tentative,
    Requested,
    Confirmed,
    Acknowledged,
    InProgress,
    Completed,
    Cancelled,
    Rescheduled,
}

/// <summary>
/// A planned/confirmed day of work for one resource on one project. One
/// booking = one date; multi-day assignments produce one booking per day. A
/// booking is a planning object — separate from time reports (execution) even
/// when they refer to the same work (design §1.1).
/// </summary>
public class Booking
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }

    public DateOnly Date { get; set; }
    /// <summary>Planned duration in hours (default 8 = one full day). Drives the
    /// PAD commitment: max(MinPadPerBookedDay, PlannedHours / PadHoursPerDay).</summary>
    public decimal PlannedHours { get; set; } = 8m;
    /// <summary>The customer's activity/work-order reference for this booking.</summary>
    public string? ActivityRef { get; set; }
    public string? WorkDescription { get; set; }
    public string? CustomerContact { get; set; }
    /// <summary>Reference to the booking instruction/evidence (mail, order no).</summary>
    public string? EvidenceRef { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Forecast;
    /// <summary>Separate acknowledgement flag — never releases PAD by itself.</summary>
    public DateTime? AcknowledgedUtc { get; set; }

    /// <summary>Set on the replacement booking created by a reschedule —
    /// points back to the original (which keeps its own PAD consequences).</summary>
    public Guid? ReplacesBookingId { get; set; }
    /// <summary>Set on the cancelled original — points forward to the replacement.</summary>
    public Guid? RescheduledToBookingId { get; set; }

    /// <summary>Resolved cancellation charge in hours (non-PAD contracts —
    /// billed on the cancelled month's invoice; on PAD contracts the charge
    /// materialises as utilised PAD instead, so this stays null there).</summary>
    public decimal? CancellationChargeHours { get; set; }
    /// <summary>Resolved fixed cancellation fee in the contract currency.</summary>
    public decimal? CancellationChargeAmount { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
