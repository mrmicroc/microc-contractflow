namespace ContractFlow.Api.Services;

/// <summary>
/// Swedish business-day calendar ("SE") — the default holiday calendar for
/// contracts (design §4.3, cancellation deadline). Official public holidays
/// plus the de facto non-working eves (Midsummer Eve, Christmas Eve,
/// New Year's Eve). Extensible to other country calendars later via
/// <see cref="ContractFlow.Api.Models.Domain.ContractTerms.HolidayCalendar"/>.
/// </summary>
public static class BusinessDays
{
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

    public static bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
        && !SwedishHolidays(date.Year).Contains(date);

    /// <summary>Adds n business days forward (negative n goes backward).</summary>
    public static DateOnly AddBusinessDays(DateOnly from, int n)
    {
        var step = n >= 0 ? 1 : -1;
        var remaining = Math.Abs(n);
        var date = from;
        while (remaining > 0)
        {
            date = date.AddDays(step);
            if (IsBusinessDay(date)) remaining--;
        }
        return date;
    }

    /// <summary>
    /// True when `on` is at least `deadlineDays` business days before `start` —
    /// the cancellation-deadline test (default 4 business days before the
    /// booked start). Cancelling exactly on the deadline day counts as timely.
    /// </summary>
    public static bool IsTimelyCancellation(DateOnly on, DateOnly start, int deadlineDays)
        => start >= AddBusinessDays(on, deadlineDays);

    /// <summary>Business days of notice when cancelling `on` for a booking on
    /// `start`: business days strictly after `on` up to and including `start`
    /// (same-day or past cancellations = 0).</summary>
    public static int CountBusinessDays(DateOnly on, DateOnly start)
    {
        if (start <= on) return 0;
        var n = 0;
        for (var d = on.AddDays(1); d <= start; d = d.AddDays(1))
            if (IsBusinessDay(d)) n++;
        return n;
    }
}
