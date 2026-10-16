namespace ContractFlow.Api.Services;

/// <summary>
/// Business-day calendars keyed by country code (design §4.3). "SE" is the
/// default for contracts. Public holidays decide both the Holiday day type in
/// billing and the notice counting for cancellation. Unknown codes fall back
/// to weekends only — <see cref="SupportedCalendars"/> lists the implemented
/// ones for UI dropdowns.
/// </summary>
public static class BusinessDays
{
    /// <summary>Calendars the API supports — code → display name (English).</summary>
    public static readonly IReadOnlyDictionary<string, string> SupportedCalendars =
        new Dictionary<string, string>
        {
            ["SE"] = "Sweden",
            ["NO"] = "Norway",
            ["DK"] = "Denmark",
            ["FI"] = "Finland",
            ["NONE"] = "None (weekends only)",
        };

    /// <summary>Holiday set for a calendar code; unknown/NONE → weekends only.</summary>
    public static HashSet<DateOnly> HolidaysFor(string? calendar, int year) =>
        calendar?.ToUpperInvariant() switch
        {
            "SE" => SwedishHolidays(year),
            "NO" => NorwegianHolidays(year),
            "DK" => DanishHolidays(year),
            "FI" => FinnishHolidays(year),
            _ => [], // NONE or unknown — weekends only
        };

    /// <summary>Swedish public holidays + de facto non-working eves for a year.</summary>
    public static HashSet<DateOnly> SwedishHolidays(int year)
    {
        var easter = EasterSunday(year);
        var midsummer = Enumerable.Range(20, 7)                     // Jun 20–26
            .Select(d => new DateOnly(year, 6, d))
            .First(d => d.DayOfWeek == DayOfWeek.Saturday);
        var allSaints = Enumerable.Range(0, 7)                      // Oct 31 – Nov 6
            .Select(d => new DateOnly(year, 10, 31).AddDays(d))
            .First(d => d.DayOfWeek == DayOfWeek.Saturday);

        return
        [
            new(year, 1, 1),                                        // New Year's Day
            new(year, 1, 6),                                        // Epiphany
            easter.AddDays(-2),                                     // Good Friday
            easter,                                                 // Easter Sunday
            easter.AddDays(1),                                      // Easter Monday
            new(year, 5, 1),                                        // Labour Day
            easter.AddDays(39),                                     // Ascension Day
            easter.AddDays(49),                                     // Whit Sunday
            new(year, 6, 6),                                        // National Day
            midsummer,                                              // Midsummer Day
            midsummer.AddDays(-1),                                  // Midsummer Eve (de facto)
            allSaints,                                              // All Saints' Day
            new(year, 12, 24),                                      // Christmas Eve (de facto)
            new(year, 12, 25),                                      // Christmas Day
            new(year, 12, 26),                                      // Boxing Day
            new(year, 12, 31),                                      // New Year's Eve (de facto)
        ];
    }

    /// <summary>Norwegian public holidays (helligdager) for a year.</summary>
    public static HashSet<DateOnly> NorwegianHolidays(int year)
    {
        var easter = EasterSunday(year);
        return
        [
            new(year, 1, 1),                                        // New Year's Day
            easter.AddDays(-3),                                     // Maundy Thursday
            easter.AddDays(-2),                                     // Good Friday
            easter,                                                 // Easter Sunday
            easter.AddDays(1),                                      // Easter Monday
            new(year, 5, 1),                                        // Labour Day
            new(year, 5, 17),                                       // Constitution Day
            easter.AddDays(39),                                     // Ascension Day
            easter.AddDays(49),                                     // Whit Sunday
            easter.AddDays(50),                                     // Whit Monday
            new(year, 12, 25),                                      // Christmas Day
            new(year, 12, 26),                                      // Boxing Day
        ];
    }

    /// <summary>Danish public holidays (helligdage) for a year. Great Prayer
    /// Day abolished from 2024 — intentionally absent.</summary>
    public static HashSet<DateOnly> DanishHolidays(int year)
    {
        var easter = EasterSunday(year);
        return
        [
            new(year, 1, 1),                                        // New Year's Day
            easter.AddDays(-3),                                     // Maundy Thursday
            easter.AddDays(-2),                                     // Good Friday
            easter,                                                 // Easter Sunday
            easter.AddDays(1),                                      // Easter Monday
            easter.AddDays(39),                                     // Ascension Day
            easter.AddDays(49),                                     // Whit Sunday
            easter.AddDays(50),                                     // Whit Monday
            new(year, 12, 24),                                      // Christmas Eve (de facto)
            new(year, 12, 25),                                      // Christmas Day
            new(year, 12, 26),                                      // Boxing Day
            new(year, 12, 31),                                      // New Year's Eve (de facto)
        ];
    }

    /// <summary>Finnish public holidays (pyhäpäivät) for a year.</summary>
    public static HashSet<DateOnly> FinnishHolidays(int year)
    {
        var easter = EasterSunday(year);
        var midsummer = Enumerable.Range(20, 7)                     // Jun 20–26
            .Select(d => new DateOnly(year, 6, d))
            .First(d => d.DayOfWeek == DayOfWeek.Saturday);
        return
        [
            new(year, 1, 1),                                        // New Year's Day
            new(year, 1, 6),                                        // Epiphany
            easter.AddDays(-2),                                     // Good Friday
            easter,                                                 // Easter Sunday
            easter.AddDays(1),                                      // Easter Monday
            new(year, 5, 1),                                        // May Day
            easter.AddDays(39),                                     // Ascension Day
            midsummer.AddDays(-1),                                  // Midsummer Eve (de facto)
            midsummer,                                              // Midsummer Day
            new(year, 12, 6),                                       // Independence Day
            new(year, 12, 24),                                      // Christmas Eve (de facto)
            new(year, 12, 25),                                      // Christmas Day
            new(year, 12, 26),                                      // Boxing Day
        ];
    }

    /// <summary>Easter Sunday, Anonymous Gregorian algorithm.</summary>
    public static DateOnly EasterSunday(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100;
        int d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    public static bool IsBusinessDay(DateOnly date, string? calendar = "SE") =>
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
        && !HolidaysFor(calendar, date.Year).Contains(date);

    /// <summary>Adds n business days forward (negative n goes backward).</summary>
    public static DateOnly AddBusinessDays(DateOnly from, int n, string? calendar = "SE")
    {
        var step = n >= 0 ? 1 : -1;
        var remaining = Math.Abs(n);
        var date = from;
        while (remaining > 0)
        {
            date = date.AddDays(step);
            if (IsBusinessDay(date, calendar)) remaining--;
        }
        return date;
    }

    /// <summary>
    /// True when `on` is at least `deadlineDays` business days before `start` —
    /// the cancellation-deadline test (default 4 business days before the
    /// booked start). Cancelling exactly on the deadline day counts as timely.
    /// </summary>
    public static bool IsTimelyCancellation(DateOnly on, DateOnly start, int deadlineDays,
        string? calendar = "SE")
        => start >= AddBusinessDays(on, deadlineDays, calendar);

    /// <summary>Business days of notice when cancelling `on` for a booking on
    /// `start`: business days strictly after `on` up to and including `start`
    /// (same-day or past cancellations = 0).</summary>
    public static int CountBusinessDays(DateOnly on, DateOnly start, string? calendar = "SE")
    {
        if (start <= on) return 0;
        var n = 0;
        for (var d = on.AddDays(1); d <= start; d = d.AddDays(1))
            if (IsBusinessDay(d, calendar)) n++;
        return n;
    }
}
