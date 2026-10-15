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
    public void Feed_CompletedBooking_MapsToCompletedStatus()
    {
        var b = Booking("Site B", "2027-06-01");
        b.Status = BookingStatus.Completed;
        Assert.Contains("STATUS:COMPLETED", IcsBuilder.Feed("T", [b]));
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
