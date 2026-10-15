using System.Text;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Services;

/// <summary>
/// Renders RFC 5545 iCalendar feeds for resource calendars (design §3 "personal
/// .ics feeds"). All-day events — a booking is date-grained, so VEVENTs carry
/// DATE values and no VTIMEZONE is needed.
/// </summary>
public static class IcsBuilder
{
    public static string Feed(string calendarName, IEnumerable<Booking> bookings)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//MICROC//ContractFlow//EN\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append($"X-WR-CALNAME:{Escape(calendarName)}\r\n");

        foreach (var b in bookings)
        {
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:{b.Id}@contractflow\r\n");
            sb.Append($"DTSTAMP:{b.CreatedUtc:yyyyMMdd'T'HHmmss'Z'}\r\n");
            // DTEND is exclusive — an all-day event on D ends at D+1.
            sb.Append($"DTSTART;VALUE=DATE:{b.Date:yyyyMMdd}\r\n");
            sb.Append($"DTEND;VALUE=DATE:{b.Date.AddDays(1):yyyyMMdd}\r\n");
            var summary = b.WorkDescription is { Length: > 0 } d
                ? $"{b.Project.Name} — {d}"
                : b.Project.Name;
            sb.Append($"SUMMARY:{Escape(summary)}\r\n");
            sb.Append($"STATUS:{(b.Status == BookingStatus.Completed ? "COMPLETED" : "CONFIRMED")}\r\n");
            sb.Append("END:VEVENT\r\n");
        }

        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    /// <summary>RFC 5545 §3.3.11 text escaping.</summary>
    public static string Escape(string text) => text
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");
}
