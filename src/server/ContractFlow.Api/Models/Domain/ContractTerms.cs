namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// How booked time bills the customer (design: contract configuration — a
/// contract combines billing components rather than being one fixed type).
/// </summary>
public enum TimeBilling
{
    /// <summary>Time is not billed (e.g. pure PAD or fixed-price contract).</summary>
    None,
    /// <summary>Rate × hours per day type, overtime rules, daily minimum.</summary>
    Hourly,
    /// <summary>Flat price per attendance day, optional half-day rate.</summary>
    DailyRate,
}

/// <summary>
/// Versioned commercial terms of a contract — rates, PAD rules, cancellation
/// deadline, travel/expense policy, currency, indexation. Effective-dated and
/// never rewritten: a change creates a new row with a higher
/// <see cref="Version"/>, so historical months reconcile against the terms of
/// their time (design §3, §9.3).
/// </summary>
public class ContractTerms
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;

    /// <summary>1-based, assigned by the server (max + 1 within the contract).</summary>
    public int Version { get; set; }
    /// <summary>First date this version applies. The applicable version for a
    /// given date is the one with the latest EffectiveFrom ≤ that date.</summary>
    public DateOnly EffectiveFrom { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // ── Billing components (contract configuration) ─────────────────────────
    /// <summary>Whether and how time bills: hourly rates, per-day rate, or not
    /// at all (e.g. pure PAD or fixed-price contracts).</summary>
    public TimeBilling TimeBilling { get; set; } = TimeBilling.Hourly;
    /// <summary>PAD component on — bookings commit PAD per the PAD rules below,
    /// and the month bills utilised PAD × <see cref="PadRate"/>.</summary>
    public bool PadEnabled { get; set; }
    /// <summary>Fixed-price component — a one-off amount billed in the month
    /// containing <see cref="FixedPriceDate"/>.</summary>
    public bool FixedPriceEnabled { get; set; }
    public decimal? FixedPriceAmount { get; set; }
    /// <summary>The date the fixed price lands on an invoice specification.</summary>
    public DateOnly? FixedPriceDate { get; set; }
    public string? FixedPriceNote { get; set; }

    // ── Currency & customer rates ───────────────────────────────────────────
    /// <summary>ISO 4217 currency code, e.g. "SEK".</summary>
    public required string Currency { get; set; }
    /// <summary>Price per PAD (pre-allocated day), PAD-model contracts.</summary>
    public decimal? PadRate { get; set; }
    /// <summary>Flat price per attendance day — the DailyRate component
    /// (<see cref="TimeBilling"/> = DailyRate).</summary>
    public decimal? DayRate { get; set; }
    /// <summary>Price when the day's hours stay at or under
    /// <see cref="HalfDayMaxHours"/>; null = always the full day rate.</summary>
    public decimal? HalfDayRate { get; set; }
    /// <summary>Day-length ceiling for the half-day rate.</summary>
    public decimal? HalfDayMaxHours { get; set; }
    /// <summary>Ordinary weekday hourly rate; also base for OT multipliers.</summary>
    public decimal? HourlyRate { get; set; }
    public decimal? SaturdayRate { get; set; }
    public decimal? SundayRate { get; set; }
    public decimal? HolidayRate { get; set; }
    /// <summary>Overtime starts after this many hours in a day (spec: 9th hour → 8).</summary>
    public int OvertimeAfterHours { get; set; } = 8;
    /// <summary>Overtime multiplier in percent (175 = ×1.75).</summary>
    public decimal OvertimePercent { get; set; } = 150m;
    /// <summary>Fixed customer fee per mobilisation period, regardless of cost.</summary>
    public decimal? MobilisationFee { get; set; }
    /// <summary>Mark-up percent on recharged expenses (spec example: 10 %).</summary>
    public decimal ExpenseMarkupPercent { get; set; }
    /// <summary>Minimum billable hours per attendance day (design §5 step 4 —
    /// the adjustment line is added on top of actuals, never overwriting them).
    /// Null = no minimum.</summary>
    public decimal? MinBillableHoursPerDay { get; set; }

    // ── PAD rules (design §4.3) ─────────────────────────────────────────────
    /// <summary>Hours per 1 PAD — default 8.</summary>
    public decimal PadHoursPerDay { get; set; } = 8m;
    /// <summary>Minimum PAD committed per confirmed Booked Day — default 1.00.</summary>
    public decimal MinPadPerBookedDay { get; set; } = 1m;
    /// <summary>Max share of unused baseline that may carry over — default 20 %.</summary>
    public decimal CarryOverMaxPercent { get; set; } = 20m;
    /// <summary>Free cancellation deadline in business days — default 4.</summary>
    public int CancellationDeadlineBusinessDays { get; set; } = 4;
    /// <summary>Holiday calendar key ("SE" default) driving day-type resolution.</summary>
    public required string HolidayCalendar { get; set; }

    // ── Policies ────────────────────────────────────────────────────────────
    /// <summary>Travel & expense policy description (what recharges to customer).</summary>
    public string? TravelExpensePolicy { get; set; }
    /// <summary>Rate indexation rule description, e.g. annual index uplift.</summary>
    public string? Indexation { get; set; }
    public string? Notes { get; set; }
}
