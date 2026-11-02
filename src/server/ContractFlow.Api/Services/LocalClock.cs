namespace ContractFlow.Api.Services;

/// <summary>
/// Business-local "today" — cancellation notice, month-end and completion
/// rules count in the operating time zone, not UTC (between 00:00 and 02:00
/// Swedish time the UTC date is still yesterday). The clock comes from an
/// injected <see cref="TimeProvider"/> so tests can travel in time.
/// </summary>
public static class LocalClock
{
    private static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        // .NET 6+ maps IANA ids on Windows too; fall back for stripped images.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }

    public static DateOnly Today(this TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);
}
