using ContractFlow.Api.Services;

namespace ContractFlow.Api.Tests.Services;

public class BusinessDaysTests
{
    [Theory]
    [InlineData(2026, 4, 5)]  // Easter Sunday 2026
    [InlineData(2025, 4, 20)] // Easter Sunday 2025
    [InlineData(2027, 3, 28)] // Easter Sunday 2027
    public void EasterSunday_KnownDates(int year, int month, int day)
        => Assert.Equal(new DateOnly(year, month, day), BusinessDays.EasterSunday(year));

    [Theory]
    [InlineData(2026, 1, 1)]   // New Year's Day
    [InlineData(2026, 1, 6)]   // Epiphany
    [InlineData(2026, 4, 3)]   // Good Friday 2026
    [InlineData(2026, 4, 6)]   // Easter Monday 2026
    [InlineData(2026, 5, 1)]   // Labour Day
    [InlineData(2026, 5, 14)]  // Ascension Day 2026 (Easter+39)
    [InlineData(2026, 6, 6)]   // National Day (Saturday)
    [InlineData(2026, 6, 19)]  // Midsummer Eve 2026 (de facto non-working)
    [InlineData(2026, 6, 20)]  // Midsummer Day 2026
    [InlineData(2026, 12, 24)] // Christmas Eve (de facto)
    [InlineData(2026, 12, 25)] // Christmas Day
    [InlineData(2026, 12, 31)] // New Year's Eve (de facto)
    public void NotBusinessDay_SwedishHolidays(int y, int m, int d)
        => Assert.False(BusinessDays.IsBusinessDay(new DateOnly(y, m, d)));

    [Theory]
    [InlineData(2026, 10, 15)] // ordinary Thursday
    [InlineData(2026, 3, 10)]  // ordinary Tuesday
    public void IsBusinessDay_OrdinaryDays(int y, int m, int d)
        => Assert.True(BusinessDays.IsBusinessDay(new DateOnly(y, m, d)));

    [Theory]
    [InlineData(2026, 10, 17)] // Saturday
    [InlineData(2026, 10, 18)] // Sunday
    public void NotBusinessDay_Weekends(int y, int m, int d)
        => Assert.False(BusinessDays.IsBusinessDay(new DateOnly(y, m, d)));

    [Fact]
    public void AddBusinessDays_SkipsWeekendsAndHolidays()
    {
        // Thu 15 Oct 2026 + 1 business day = Fri 16; + 3 skips the weekend → Wed 21.
        var thu = new DateOnly(2026, 10, 15);
        Assert.Equal(new DateOnly(2026, 10, 16), BusinessDays.AddBusinessDays(thu, 1));
        Assert.Equal(new DateOnly(2026, 10, 21), BusinessDays.AddBusinessDays(thu, 4));

        // Over Christmas/New Year 2025–26 the holidays and weekends are skipped.
        var mon = new DateOnly(2025, 12, 22);
        Assert.Equal(new DateOnly(2026, 1, 5), BusinessDays.AddBusinessDays(mon, 5));
    }

    [Fact]
    public void HolidaysFor_CountryCalendars_Differ()
    {
        // Norwegian Constitution Day 2027-05-17 is a Monday: holiday in NO,
        // ordinary Monday in SE.
        var may17 = new DateOnly(2027, 5, 17);
        Assert.False(BusinessDays.IsBusinessDay(may17, "NO"));
        Assert.True(BusinessDays.IsBusinessDay(may17, "SE"));

        // Maundy Thursday 2026-04-02: holiday in NO/DK, a workday in SE.
        var maundy = new DateOnly(2026, 4, 2);
        Assert.False(BusinessDays.IsBusinessDay(maundy, "NO"));
        Assert.False(BusinessDays.IsBusinessDay(maundy, "DK"));
        Assert.True(BusinessDays.IsBusinessDay(maundy, "SE"));

        // Midsummer Eve is non-working in both SE and FI.
        var midsummerEve = new DateOnly(2026, 6, 19);
        Assert.False(BusinessDays.IsBusinessDay(midsummerEve, "SE"));
        Assert.False(BusinessDays.IsBusinessDay(midsummerEve, "FI"));
    }

    [Theory]
    // United Kingdom — England & Wales bank holidays with substitute days.
    [InlineData("GB-ENG", 2021, 12, 27)] // Christmas Sat → Monday substitute
    [InlineData("GB-ENG", 2021, 12, 28)] // Boxing Sun → Tuesday substitute
    [InlineData("GB-ENG", 2026, 5, 4)]   // Early May bank holiday
    [InlineData("GB-ENG", 2026, 8, 31)]  // Summer bank holiday
    // Scotland — Jan 1 Sun 2023 substitutes to Mon 2, so Jan 2 rolls to Tue 3.
    [InlineData("GB-SCT", 2023, 1, 2)]
    [InlineData("GB-SCT", 2023, 1, 3)]
    [InlineData("GB-SCT", 2026, 8, 3)]   // Summer BH is the *first* Mon in Aug
    // Northern Ireland — the two extra patron/boyne days.
    [InlineData("GB-NIR", 2026, 3, 17)]  // St Patrick's Day
    [InlineData("GB-NIR", 2026, 7, 13)]  // Jul 12 is a Sunday → Mon 13 substitute
    // Germany — nationwide days only.
    [InlineData("DE", 2025, 10, 3)]      // Tag der Deutschen Einheit (Friday)
    [InlineData("DE", 2026, 5, 25)]      // Pfingstmontag (Whit Monday)
    // France.
    [InlineData("FR", 2026, 7, 14)]      // Fête nationale (Tuesday)
    [InlineData("FR", 2026, 5, 8)]       // Victoire 1945 (Friday)
    // Italy.
    [InlineData("IT", 2026, 6, 2)]       // Festa della Repubblica (Tuesday)
    [InlineData("IT", 2026, 12, 8)]      // Immacolata (Tuesday)
    // United States — federal observance moves Saturday to Friday.
    [InlineData("US", 2026, 7, 3)]       // Independence Day Sat → Fri observed
    [InlineData("US", 2026, 11, 26)]     // Thanksgiving (4th Thursday)
    [InlineData("US", 2026, 1, 19)]      // MLK Jr. Day (3rd Monday)
    public void NotBusinessDay_OtherCountryHolidays(string cal, int y, int m, int d)
        => Assert.False(BusinessDays.IsBusinessDay(new DateOnly(y, m, d), cal));

    [Fact]
    public void RegionalCalendars_DifferWithinTheUk()
    {
        // Aug 31 2026 (last Monday) is a bank holiday in England & Wales but a
        // normal Monday in Scotland (their summer BH is Aug 3).
        var aug31 = new DateOnly(2026, 8, 31);
        Assert.False(BusinessDays.IsBusinessDay(aug31, "GB-ENG"));
        Assert.True(BusinessDays.IsBusinessDay(aug31, "GB-SCT"));

        // Germany's nationwide set excludes state days: Corpus Christi
        // (2026-06-04, Thursday) is a holiday in Bavaria but not nationwide.
        Assert.True(BusinessDays.IsBusinessDay(new DateOnly(2026, 6, 4), "DE"));
    }

    [Fact]
    public void HolidaysFor_NoneOrUnknown_WeekendsOnly()
    {
        // New Year's Day is a weekday holiday in SE but a business day under
        // NONE — and unknown codes degrade the same way rather than failing.
        var newYear = new DateOnly(2026, 1, 1); // Thursday
        Assert.False(BusinessDays.IsBusinessDay(newYear, "SE"));
        Assert.True(BusinessDays.IsBusinessDay(newYear, "NONE"));
        Assert.True(BusinessDays.IsBusinessDay(newYear, "XX"));
        Assert.False(BusinessDays.IsBusinessDay(new DateOnly(2026, 1, 3), "NONE")); // Sat
    }

    [Fact]
    public void CountBusinessDays_UsesTheContractCalendar()
    {
        // Wed 2026-04-01 → Tue 2026-04-07. Business days in the window:
        // SE counts Thu 4/2 and Tue 4/7 (Good Friday + Easter Monday off);
        // DK additionally has Maundy Thursday off → only Tue 4/7 counts.
        var on = new DateOnly(2026, 4, 1);      // Wed
        var start = new DateOnly(2026, 4, 7);   // Tue after Easter
        Assert.True(BusinessDays.CountBusinessDays(on, start, "DK")
                  < BusinessDays.CountBusinessDays(on, start, "SE"));
    }

    [Fact]
    public void IsTimelyCancellation_RespectsBusinessDayDeadline()
    {
        // Today Thu 15 Oct 2026; deadline 4 business days → timely for bookings
        // on Wed 21 or later, late for anything sooner.
        var today = new DateOnly(2026, 10, 15);
        Assert.True(BusinessDays.IsTimelyCancellation(today, new DateOnly(2026, 10, 21), 4));
        Assert.False(BusinessDays.IsTimelyCancellation(today, new DateOnly(2026, 10, 20), 4));
        Assert.False(BusinessDays.IsTimelyCancellation(today, new DateOnly(2026, 10, 16), 4));
    }
}
