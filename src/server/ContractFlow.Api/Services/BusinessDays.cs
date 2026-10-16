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
    /// <summary>Calendars the API supports — code → display name (English).
    /// New countries join by adding a provider below — an offline rule set is
    /// preferred over a licensed/network holiday service for billing paths.</summary>
    public static readonly IReadOnlyDictionary<string, string> SupportedCalendars =
        new Dictionary<string, string>
        {
            ["SE"] = "Sweden",
            ["NO"] = "Norway",
            ["DK"] = "Denmark",
            ["FI"] = "Finland",
            ["GB-ENG"] = "United Kingdom — England & Wales",
            ["GB-SCT"] = "United Kingdom — Scotland",
            ["GB-NIR"] = "United Kingdom — Northern Ireland",
            ["DE"] = "Germany (nationwide)",
            ["FR"] = "France",
            ["IT"] = "Italy",
            ["US"] = "United States (federal)",
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
            "GB-ENG" => GbEnglandWalesHolidays(year),
            "GB-SCT" => GbScotlandHolidays(year),
            "GB-NIR" => GbNorthernIrelandHolidays(year),
            "DE" => GermanHolidays(year),
            "FR" => FrenchHolidays(year),
            "IT" => ItalianHolidays(year),
            "US" => UsFederalHolidays(year),
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

    /// <summary>England & Wales bank holidays — weekend holidays move to the
    /// next Monday ("substitute days"); Christmas/Boxing chain-aware.</summary>
    public static HashSet<DateOnly> GbEnglandWalesHolidays(int year)
    {
        var easter = EasterSunday(year);
        return
        [
            .. UkChain(new DateOnly(year, 1, 1)),
            easter.AddDays(-2),                                     // Good Friday
            easter.AddDays(1),                                      // Easter Monday
            NthWeekday(year, 5, DayOfWeek.Monday, 1),               // Early May BH
            LastWeekday(year, 5, DayOfWeek.Monday),                 // Spring BH
            LastWeekday(year, 8, DayOfWeek.Monday),                 // Summer BH
            .. UkChain(new DateOnly(year, 12, 25), new DateOnly(year, 12, 26)),       // Christmas + Boxing
        ];
    }

    /// <summary>Scottish bank holidays — Jan 2 and St Andrew's Day instead of
    /// Easter Monday/late-summer, each with substitute-day handling.</summary>
    public static HashSet<DateOnly> GbScotlandHolidays(int year)
    {
        var easter = EasterSunday(year);
        return
        [
            .. UkChain(new DateOnly(year, 1, 1), new DateOnly(year, 1, 2)),           // New Year + Jan 2
            easter.AddDays(-2),                                     // Good Friday
            NthWeekday(year, 5, DayOfWeek.Monday, 1),               // Early May BH
            NthWeekday(year, 8, DayOfWeek.Monday, 1),               // Summer BH (first Mon)
            UkSub(new DateOnly(year, 11, 30)),                               // St Andrew's Day
            .. UkChain(new DateOnly(year, 12, 25), new DateOnly(year, 12, 26)),       // Christmas + Boxing
        ];
    }

    /// <summary>Northern Ireland — the England & Wales set plus St Patrick's
    /// Day and the Battle of the Boyne (Orangemen's Day).</summary>
    public static HashSet<DateOnly> GbNorthernIrelandHolidays(int year)
    {
        var set = GbEnglandWalesHolidays(year);
        set.Add(UkSub(new DateOnly(year, 3, 17)));                           // St Patrick's Day
        set.Add(UkSub(new DateOnly(year, 7, 12)));                           // Battle of the Boyne
        return set;
    }

    /// <summary>German nationwide holidays (gesetzliche Feiertage that apply in
    /// every state) — state-specific days like Corpus Christi or Reformation
    /// Day are intentionally absent; pick the closest fit per contract.</summary>
    public static HashSet<DateOnly> GermanHolidays(int year)
    {
        var easter = EasterSunday(year);
        return
        [
            new(year, 1, 1),                                        // Neujahr
            easter.AddDays(-2),                                     // Karfreitag
            easter.AddDays(1),                                      // Ostermontag
            new(year, 5, 1),                                        // Tag der Arbeit
            easter.AddDays(39),                                     // Christi Himmelfahrt
            easter.AddDays(50),                                     // Pfingstmontag
            new(year, 10, 3),                                       // Tag der Deutschen Einheit
            new(year, 12, 25),                                      // 1. Weihnachtstag
            new(year, 12, 26),                                      // 2. Weihnachtstag
        ];
    }

    /// <summary>French public holidays (jours fériés, metropolitan France).</summary>
    public static HashSet<DateOnly> FrenchHolidays(int year)
    {
        var easter = EasterSunday(year);
        return
        [
            new(year, 1, 1),                                        // Jour de l'An
            easter.AddDays(1),                                      // Lundi de Pâques
            new(year, 5, 1),                                        // Fête du Travail
            new(year, 5, 8),                                        // Victoire 1945
            easter.AddDays(39),                                     // Ascension
            easter.AddDays(50),                                     // Lundi de Pentecôte
            new(year, 7, 14),                                       // Fête nationale
            new(year, 8, 15),                                       // Assomption
            new(year, 11, 1),                                       // Toussaint
            new(year, 11, 11),                                      // Armistice 1918
            new(year, 12, 25),                                      // Noël
        ];
    }

    /// <summary>Italian public holidays (festività nazionali).</summary>
    public static HashSet<DateOnly> ItalianHolidays(int year)
    {
        var easter = EasterSunday(year);
        return
        [
            new(year, 1, 1),                                        // Capodanno
            new(year, 1, 6),                                        // Epifania
            easter.AddDays(1),                                      // Lunedì dell'Angelo
            new(year, 4, 25),                                       // Liberazione
            new(year, 5, 1),                                        // Festa del Lavoro
            new(year, 6, 2),                                        // Festa della Repubblica
            new(year, 8, 15),                                       // Ferragosto
            new(year, 11, 1),                                       // Ognissanti
            new(year, 12, 8),                                       // Immacolata
            new(year, 12, 25),                                      // Natale
            new(year, 12, 26),                                      // Santo Stefano
        ];
    }

    /// <summary>US federal holidays (5 U.S.C. §6103) — Saturday occurrences are
    /// observed on the preceding Friday, Sunday on the following Monday.</summary>
    public static HashSet<DateOnly> UsFederalHolidays(int year)
    {
        return
        [
            UsObserved(new DateOnly(year, 1, 1)),                            // New Year's Day
            NthWeekday(year, 1, DayOfWeek.Monday, 3),               // MLK Jr. Day
            NthWeekday(year, 2, DayOfWeek.Monday, 3),               // Washington's Birthday
            LastWeekday(year, 5, DayOfWeek.Monday),                 // Memorial Day
            UsObserved(new DateOnly(year, 6, 19)),                           // Juneteenth
            UsObserved(new DateOnly(year, 7, 4)),                            // Independence Day
            NthWeekday(year, 9, DayOfWeek.Monday, 1),               // Labor Day
            NthWeekday(year, 10, DayOfWeek.Monday, 2),              // Columbus Day
            UsObserved(new DateOnly(year, 11, 11)),                          // Veterans Day
            NthWeekday(year, 11, DayOfWeek.Thursday, 4),            // Thanksgiving
            UsObserved(new DateOnly(year, 12, 25)),                          // Christmas Day
        ];
    }

    /// <summary>UK substitute-day rule: a holiday on a weekend moves to the
    /// next Monday.</summary>
    private static DateOnly UkSub(DateOnly d) => d.DayOfWeek switch
    {
        DayOfWeek.Saturday => d.AddDays(2),
        DayOfWeek.Sunday => d.AddDays(1),
        _ => d,
    };

    /// <summary>Substitute days for consecutive holidays (Christmas+Boxing,
    /// Jan 1–2): when a substitute collides with the next holiday it shifts
    /// one more day, matching UK practice (e.g. Dec 25 Sat 2021 → Mon 27,
    /// Dec 26 Sun → Tue 28).</summary>
    private static IEnumerable<DateOnly> UkChain(params DateOnly[] days)
    {
        var taken = new HashSet<DateOnly>();
        foreach (var d in days)
        {
            var o = UkSub(d);
            while (!taken.Add(o)) o = o.AddDays(1);
            yield return o;
        }
    }

    /// <summary>US federal observance: Saturday → Friday before, Sunday →
    /// Monday after.</summary>
    private static DateOnly UsObserved(DateOnly d) => d.DayOfWeek switch
    {
        DayOfWeek.Saturday => d.AddDays(-1),
        DayOfWeek.Sunday => d.AddDays(1),
        _ => d,
    };

    /// <summary>The n-th `dow` of a month (n ≥ 1).</summary>
    private static DateOnly NthWeekday(int year, int month, DayOfWeek dow, int n) =>
        Enumerable.Range(1, 7)
            .Select(d => new DateOnly(year, month, d))
            .First(d => d.DayOfWeek == dow)
            .AddDays(7 * (n - 1));

    /// <summary>The last `dow` of a month.</summary>
    private static DateOnly LastWeekday(int year, int month, DayOfWeek dow) =>
        Enumerable.Range(0, 7)
            .Select(d => new DateOnly(year, month, DateTime.DaysInMonth(year, month)).AddDays(-d))
            .First(d => d.DayOfWeek == dow);

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
