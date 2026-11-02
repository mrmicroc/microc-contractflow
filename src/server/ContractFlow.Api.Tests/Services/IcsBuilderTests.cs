using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Xunit;

namespace ContractFlow.Api.Tests.Services;

public class IcsBuilderTests
{
    private static Booking Booking(string summary, string date) => new()
    {
        Id = Guid.NewGuid(),
        Project = new Project { Name = summary },
        Resource = new Resource { DisplayName = "Tester", Type = ResourceType.Employee },
        Date = DateOnly.Parse(date),
        Status = BookingStatus.Confirmed,
        WorkDescription = null,
    };

    [Fact]
    public void Feed_WrapsCalendarAndEvents()
    {
        var ics = IcsBuilder.Feed("Tester", [Booking("Site A", "2027-06-01")]);

        Assert.StartsWith("BEGIN:VCALENDAR", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.Contains("VERSION:2.0", ics);
        Assert.Contains("X-WR-CALNAME:Tester", ics);
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20270601", ics);
        // DTEND is exclusive → next day for a single-day event
        Assert.Contains("DTEND;VALUE=DATE:20270602", ics);
        Assert.Contains("SUMMARY:Site A", ics);
        Assert.Contains("STATUS:CONFIRMED", ics);
    }

    [Fact]
    public void Feed_CompletedBooking_StaysConfirmed()
    {
        // RFC 5545: VEVENT STATUS is TENTATIVE / CONFIRMED / CANCELLED only.
        var b = Booking("Site B", "2027-06-01");
        b.Status = BookingStatus.Completed;
        var ics = IcsBuilder.Feed("T", [b]);
        Assert.Contains("STATUS:CONFIRMED", ics);
        Assert.DoesNotContain("COMPLETED", ics);
    }

    [Theory]
    [InlineData(BookingStatus.Forecast)]
    [InlineData(BookingStatus.Tentative)]
    [InlineData(BookingStatus.Requested)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Rescheduled)]
    public void Feed_SkipsNonCommittedBookings(BookingStatus status)
    {
        var b = Booking("Site C", "2027-06-01");
        b.Status = status;
        Assert.DoesNotContain("BEGIN:VEVENT", IcsBuilder.Feed("T", [b]));
    }

    [Fact]
    public void Feed_WorkPackage_BecomesLocation()
    {
        var b = Booking("Site D", "2027-06-01");
        b.WorkPackage = new WorkPackage { Name = "North Link, bay 4" };
        Assert.Contains("LOCATION:North Link\\, bay 4", IcsBuilder.Feed("T", [b]));
    }

    [Fact]
    public void Fold_SplitsLongLinesAt75Octets()
    {
        var line = "SUMMARY:" + new string('x', 200);
        var folded = IcsBuilder.Fold(line);
        var parts = folded.Split("\r\n");
        Assert.True(parts.Length > 1);
        Assert.All(parts, p => Assert.True(System.Text.Encoding.UTF8.GetByteCount(p) <= 75));
        Assert.All(parts.Skip(1), p => Assert.StartsWith(" ", p));
        Assert.Equal(line, string.Concat(parts.Select((p, i) => i == 0 ? p : p[1..])));
    }

    [Fact]
    public void Fold_NeverSplitsMultiByteCharacters()
    {
        var line = "SUMMARY:" + new string('å', 60); // 2 octets each
        var parts = IcsBuilder.Fold(line).Split("\r\n");
        Assert.All(parts, p => Assert.True(System.Text.Encoding.UTF8.GetByteCount(p) <= 75));
        Assert.Equal(line, string.Concat(parts.Select((p, i) => i == 0 ? p : p[1..])));
    }

    [Fact]
    public void Escape_HandlesRfc5545Specials()
    {
        Assert.Equal(
            "a\\,b\\;c\\\\d\\ne",
            IcsBuilder.Escape("a,b;c\\d\ne"));
    }

    [Fact]
    public void Feed_UsesWorkDescriptionInSummary_WhenPresent()
    {
        var b = Booking("Site A", "2027-06-01");
        b.WorkDescription = "inspect, test";
        var ics = IcsBuilder.Feed("T", [b]);
        Assert.Contains("SUMMARY:Site A — inspect\\, test", ics);
    }
}
