namespace ContractFlow.Api.Models.Domain;

public enum TravelMode
{
    Car,
    Train,
    Flight,
    Boat,
    Other,
}

/// <summary>
/// One travel leg on a date (design §3 Execution). A leg is distinct from both
/// the booking and the work shift. Car legs with a distance double as mileage
/// entries — compensated per the agreement's MileageRate.
/// </summary>
public class TravelRecord
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid? BookingId { get; set; }
    public Booking? Booking { get; set; }

    public DateOnly Date { get; set; }
    public string? FromPlace { get; set; }
    public string? ToPlace { get; set; }
    public TravelMode Mode { get; set; } = TravelMode.Car;
    /// <summary>Kilometres driven — the mileage basis when Mode is Car.</summary>
    public decimal? DistanceKm { get; set; }
    /// <summary>Travel duration in hours — compensated at TravelTimePercent of the hourly rate.</summary>
    public decimal? DurationHours { get; set; }

    /// <summary>Grouping into a continuous attendance period (one mobilisation
    /// fee per period); assigned by admin confirmation.</summary>
    public Guid? MobilisationPeriodId { get; set; }
    public MobilisationPeriod? MobilisationPeriod { get; set; }

    public string? Notes { get; set; }
    public Guid? TimesheetId { get; set; }
    public Timesheet? Timesheet { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
