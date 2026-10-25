using System.Globalization;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Services;

/// <summary>
/// Month-end currency conversion for one document (design §9.3, §11 Q4):
/// the rate table of the document's month governs. An exact pair wins; the
/// inverse pair is used as 1 / rate. A missing rate is reported, never
/// guessed — the row then stays out of the total.
/// </summary>
public sealed class CurrencyConverter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly Dictionary<(string From, string To), decimal> _rates;

    public int Year { get; }
    public int Month { get; }

    private CurrencyConverter(int year, int month, Dictionary<(string, string), decimal> rates)
    {
        Year = year;
        Month = month;
        _rates = rates;
    }

    public static async Task<CurrencyConverter> ForMonthAsync(
        AppDbContext db, int year, int month, CancellationToken ct)
    {
        var rows = await db.ExchangeRates
            .Where(r => r.Year == year && r.Month == month)
            .Select(r => new { r.FromCurrency, r.ToCurrency, r.Rate })
            .ToListAsync(ct);
        return new CurrencyConverter(year, month,
            rows.ToDictionary(r => (r.FromCurrency, r.ToCurrency), r => r.Rate));
    }

    /// <summary>Pure factory for tests.</summary>
    public static CurrencyConverter From(int year, int month,
        IEnumerable<(string From, string To, decimal Rate)> rates) =>
        new(year, month, rates.ToDictionary(r => (r.From, r.To), r => r.Rate));

    /// <summary>Units of <paramref name="to"/> per 1 <paramref name="from"/>,
    /// or null when the month has no rate for the pair either way.</summary>
    public decimal? RateFor(string from, string to)
    {
        from = from.ToUpperInvariant();
        to = to.ToUpperInvariant();
        if (from == to) return 1m;
        if (_rates.TryGetValue((from, to), out var rate) && rate > 0m) return rate;
        if (_rates.TryGetValue((to, from), out var inverse) && inverse > 0m)
            return Math.Round(1m / inverse, 6, MidpointRounding.AwayFromZero);
        return null;
    }

    /// <summary>Converts to the document currency, rounded to 2 decimals.</summary>
    public decimal? Convert(decimal amount, string from, string to) =>
        RateFor(from, to) is { } rate ? TimeMath.Round2(amount * rate) : null;

    /// <summary>The warning a document carries for a missing pair.</summary>
    public string MissingRateWarning(string from, string to) =>
        string.Create(Inv,
            $"No {from.ToUpperInvariant()}→{to.ToUpperInvariant()} rate for {Year}-{Month:00} — enter it under exchange rates; those rows are left out of the total.");
}
