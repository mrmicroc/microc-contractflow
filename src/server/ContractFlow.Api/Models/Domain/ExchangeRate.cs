namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// Month rate for converting one currency into another (design §9.3, §11 Q4):
/// rows in a foreign currency — an expense paid abroad, a per diem in EUR —
/// are summed per currency at month end and converted to the document's
/// currency at the rate of the document's month. Entered by finance; the
/// original amount and currency always stay on the converted line.
/// </summary>
public class ExchangeRate
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    /// <summary>ISO 4217 code converted from, e.g. "EUR".</summary>
    public required string FromCurrency { get; set; }
    /// <summary>ISO 4217 code converted to, e.g. "SEK".</summary>
    public required string ToCurrency { get; set; }
    /// <summary>Units of <see cref="ToCurrency"/> per 1 <see cref="FromCurrency"/>
    /// (EUR→SEK 11.5 = one euro is 11.5 kronor).</summary>
    public decimal Rate { get; set; }
    /// <summary>Where the rate came from (central bank, bank statement…).</summary>
    public string? Note { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
