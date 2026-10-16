using System.Globalization;

namespace ContractFlow.Api.Services;

/// <summary>
/// ISO 3166-1 countries and ISO 4217 currencies enumerated from the runtime's
/// culture data — no embedded data file, and the catalogues stay current with
/// the framework. Used to fill searchable pickers in the UI.
/// </summary>
public static class RegionCatalog
{
    public sealed record CatalogEntry(string Code, string Name);

    private static readonly Lazy<IReadOnlyList<CatalogEntry>> _countries = new(LoadCountries);
    private static readonly Lazy<IReadOnlyList<CatalogEntry>> _currencies = new(LoadCurrencies);

    /// <summary>Every ISO 3166-1 alpha-2 region known to the runtime.</summary>
    public static IReadOnlyList<CatalogEntry> Countries => _countries.Value;

    /// <summary>Every active ISO 4217 currency (test/none pseudo-codes excluded).</summary>
    public static IReadOnlyList<CatalogEntry> Currencies => _currencies.Value;

    private static IReadOnlyList<CatalogEntry> LoadCountries() =>
        Regions()
            .GroupBy(r => r.TwoLetterISORegionName)
            .Select(g => new CatalogEntry(g.Key, g.First().EnglishName))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<CatalogEntry> LoadCurrencies() =>
        Regions()
            .Where(r => r.ISOCurrencySymbol is not ("XTS" or "XXX"))
            .GroupBy(r => r.ISOCurrencySymbol)
            .Select(g => new CatalogEntry(g.Key, g.First().CurrencyEnglishName))
            .OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<RegionInfo> Regions() =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(c => new RegionInfo(c.Name))
            .Where(r => r.TwoLetterISORegionName.Length == 2
                     && r.TwoLetterISORegionName.All(char.IsAsciiLetterUpper));
}
