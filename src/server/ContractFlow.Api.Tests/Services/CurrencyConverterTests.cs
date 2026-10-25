using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;

namespace ContractFlow.Api.Tests.Services;

/// <summary>Month-rate lookup and per-currency summaries (design §9.3).</summary>
public class CurrencyConverterTests
{
    private static readonly CurrencyConverter Fx =
        CurrencyConverter.From(2029, 1, [("EUR", "SEK", 11.5m), ("SEK", "NOK", 0.95m)]);

    [Fact]
    public void ExactPair_InversePair_SameCurrency_Missing()
    {
        Assert.Equal(11.5m, Fx.RateFor("eur", "sek"));
        Assert.Equal(13800m, Fx.Convert(1200m, "EUR", "SEK"));
        Assert.Equal(Math.Round(1m / 11.5m, 6, MidpointRounding.AwayFromZero), Fx.RateFor("SEK", "EUR"));
        Assert.Equal(1m, Fx.RateFor("SEK", "SEK"));
        Assert.Null(Fx.RateFor("USD", "SEK"));
        Assert.Null(Fx.Convert(1m, "USD", "SEK"));
        Assert.Contains("USD→SEK", Fx.MissingRateWarning("usd", "sek"));
        Assert.Contains("2029-01", Fx.MissingRateWarning("usd", "sek"));
    }

    [Fact]
    public void CurrencySummary_GroupsPerSourceCurrency()
    {
        var summary = CurrencySummaryDto.Of(
        [
            ("EUR", 1200m, 11.5m, 15180m),
            ("EUR", 100m, 11.5m, 1150m),
            ("NOK", 1000m, 1.0526m, 1052.6m),
        ]);
        Assert.Equal(["EUR", "NOK"], summary.Select(s => s.Currency));
        Assert.Equal(1300m, summary[0].OriginalTotal);
        Assert.Equal(14950m, summary[0].ConvertedTotal); // before mark-up
    }

    [Fact]
    public void MarkupFor_CategoryOverride_ElseDefault()
    {
        var t = new ContractTerms
        {
            Currency = "SEK", HolidayCalendar = "SE", ExpenseMarkupPercent = 5m,
            ExpenseMarkups = [new() { Category = ExpenseCategory.Material, Percent = 10m },
                              new() { Category = ExpenseCategory.Travel, Percent = 0m }],
        };
        Assert.Equal(10m, t.MarkupFor(ExpenseCategory.Material));
        Assert.Equal(0m, t.MarkupFor(ExpenseCategory.Travel));
        Assert.Equal(5m, t.MarkupFor(ExpenseCategory.Meal));
    }
}
