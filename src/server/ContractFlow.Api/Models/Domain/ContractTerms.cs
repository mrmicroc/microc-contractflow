namespace ContractFlow.Api.Models.Domain;

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

    // ── Currency & customer rates ───────────────────────────────────────────
    /// <summary>ISO 4217 currency code, e.g. "SEK".</summary>
    public required string Currency { get; set; }
    /// <summary>Price per PAD (pre-allocated day), PAD-model contracts.</summary>
    public decimal? PadRate { get; set; }
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
