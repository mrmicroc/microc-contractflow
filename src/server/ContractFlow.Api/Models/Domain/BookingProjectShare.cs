namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// One project's planned part of a booked day shared between projects of the
/// same contract. The shares together make up the booking's planned hours;
/// the booking's own <see cref="Booking.ProjectId"/> is the primary project
/// and is always one of the shares. Commitment splits by planned hours;
/// utilisation at completion splits by the actual time per project.
/// </summary>
public class BookingProjectShare
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public decimal PlannedHours { get; set; }
}

/// <summary>
/// A start-time change on a booking, with the local date it was notified —
/// the start time in force at the notice deadline decides when working time
/// commences; later changes never postpone it.
/// </summary>
public class BookingStartTimeChange
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    /// <summary>The new start time; null = start time withdrawn.</summary>
    public TimeOnly? StartTime { get; set; }
    /// <summary>Business-local date the change was notified.</summary>
    public DateOnly NotifiedOn { get; set; }
    public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;
    public Guid? ByUserId { get; set; }
}
