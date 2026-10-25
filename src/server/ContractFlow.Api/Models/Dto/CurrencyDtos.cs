using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Services;

namespace ContractFlow.Api.Models.Dto;

/// <summary>Month-end sum of one source currency on a document: what was
/// reported, the month rate, and the converted value (before any mark-up).</summary>
public record CurrencySummaryDto(string Currency, decimal OriginalTotal, decimal Rate, decimal ConvertedTotal)
{
    public static List<CurrencySummaryDto> Of(
        IEnumerable<(string Currency, decimal Original, decimal Rate, decimal Amount)> lines) =>
        lines.GroupBy(l => l.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new CurrencySummaryDto(
                g.Key,
                g.Sum(l => l.Original),
                g.First().Rate,
                TimeMath.Round2(g.Sum(l => TimeMath.Round2(l.Original * l.Rate)))))
            .ToList();
}

public record ExchangeRateDto(
    Guid Id, int Year, int Month, string FromCurrency, string ToCurrency,
    decimal Rate, string? Note, DateTime CreatedUtc, bool InUse);

public class ExchangeRateRequest
{
    [Range(2000, 2100)]
    public int Year { get; set; }
    [Range(1, 12)]
    public int Month { get; set; }
    [Required, StringLength(3, MinimumLength = 3)]
    public required string FromCurrency { get; set; }
    [Required, StringLength(3, MinimumLength = 3)]
    public required string ToCurrency { get; set; }
    /// <summary>Units of ToCurrency per 1 FromCurrency.</summary>
    [Range(0.000001, 1_000_000)]
    public decimal Rate { get; set; }
    [StringLength(200)]
    public string? Note { get; set; }
}
