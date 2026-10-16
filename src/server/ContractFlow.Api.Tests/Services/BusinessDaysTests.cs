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
