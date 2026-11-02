namespace ContractFlow.Api.Tests.Infrastructure;

/// <summary>
/// Controllable clock for the API under test — real time by default; a test
/// class that needs "after the month ended" (closing, completing) travels
/// forward. One instance per factory, so per test class.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset? _now;

    public override DateTimeOffset GetUtcNow() => _now ?? base.GetUtcNow();

    /// <summary>Pins "now" to noon UTC on <paramref name="date"/>.</summary>
    public void Set(DateOnly date) =>
        _now = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    public void Set(DateTimeOffset now) => _now = now;

    public void Reset() => _now = null;
}
