using System.Text;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Services;

/// <summary>
/// Renders RFC 5545 iCalendar feeds for resource calendars (design §7.2
/// "personal .ics feeds"). All-day events — a booking is date-grained, so
/// VEVENTs carry DATE values and no VTIMEZONE is needed. The feed carries no
/// rates or commercial terms: only title, date and work package.
/// </summary>
public static class IcsBuilder
{
    /// <summary>Booking statuses a feed shows — committed Booked Days only;
    /// forecast/tentative/requested planning never reaches a calendar.</summary>
    public static readonly HashSet<BookingStatus> FeedStatuses =
    [
        BookingStatus.Confirmed, BookingStatus.Acknowledged,
        BookingStatus.InProgress, BookingStatus.Completed,
    ];

    public static string Feed(string calendarName, IEnumerable<Booking> bookings)
    {
        var sb = new StringBuilder();
        void Line(string content) => sb.Append(Fold(content)).Append("\r\n");

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//MICROC//ContractFlow//EN");
        Line("CALSCALE:GREGORIAN");
        Line($"X-WR-CALNAME:{Escape(calendarName)}");

        foreach (var b in bookings.Where(b => FeedStatuses.Contains(b.Status)))
        {
            Line("BEGIN:VEVENT");
            Line($"UID:{b.Id}@contractflow");
            Line($"DTSTAMP:{b.CreatedUtc:yyyyMMdd'T'HHmmss'Z'}");
            // DTEND is exclusive — an all-day event on D ends at D+1.
            Line($"DTSTART;VALUE=DATE:{b.Date:yyyyMMdd}");
            Line($"DTEND;VALUE=DATE:{b.Date.AddDays(1):yyyyMMdd}");
            var summary = b.WorkDescription is { Length: > 0 } d
                ? $"{b.Project.Name} — {d}"
                : b.Project.Name;
            Line($"SUMMARY:{Escape(summary)}");
            if (b.WorkPackage?.Name is { Length: > 0 } location)
                Line($"LOCATION:{Escape(location)}");
            // RFC 5545 §3.8.1.11: VEVENT status is TENTATIVE/CONFIRMED/CANCELLED
            // only — completed days stay CONFIRMED.
            Line("STATUS:CONFIRMED");
            Line("END:VEVENT");
        }

        Line("END:VCALENDAR");
        return sb.ToString();
    }

    /// <summary>RFC 5545 §3.3.11 text escaping.</summary>
    public static string Escape(string text) => text
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");

    /// <summary>
    /// RFC 5545 §3.1 line folding: content lines longer than 75 octets are
    /// split with CRLF + a single space — never inside a UTF-8 sequence.
    /// </summary>
    public static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var sb = new StringBuilder();
        var octets = 0;
        var limit = 75;
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(line);
        while (e.MoveNext())
        {
            var element = e.GetTextElement();
            var size = Encoding.UTF8.GetByteCount(element);
            if (octets + size > limit)
            {
                sb.Append("\r\n ");
                octets = 0;
                limit = 74; // the leading space counts toward the 75
            }
            sb.Append(element);
            octets += size;
        }
        return sb.ToString();
    }
}
