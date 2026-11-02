using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Services;

/// <summary>
/// Which approved time rows the PAD component covers (billed through the PAD
/// line, never also as hours): rows bound to a booked day, rows inside a
/// shared booked day, and unbooked contract-mode rows on terms that let
/// unbooked services consume PAD. Rows forcing plain-hour billing stay hours.
/// </summary>
public static class PadCoverage
{
    public static bool Covers(ContractTerms? terms, bool booked, EntryBilling billing, bool inSharedDay) =>
        terms?.PadEnabled == true
        && (booked || inSharedDay
            || (terms.UnbookedTimeConsumesPad && billing == EntryBilling.Contract));

    /// <summary>(resource, date, project) of every committed shared booked day
    /// in the window — the sharing projects' time utilises through the booking.</summary>
    public static async Task<HashSet<(Guid ResourceId, DateOnly Date, Guid ProjectId)>> SharedDaysAsync(
        AppDbContext db, IReadOnlyCollection<Guid> resourceIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.BookingProjectShares
            .Where(s => resourceIds.Contains(s.Booking.ResourceId)
                && s.Booking.Date >= from && s.Booking.Date <= to
                && s.Booking.Status != BookingStatus.Cancelled && s.Booking.Status != BookingStatus.Rescheduled
                && s.Booking.Status != BookingStatus.Forecast && s.Booking.Status != BookingStatus.Tentative
                && s.Booking.Status != BookingStatus.Requested)
            .Select(s => new { s.Booking.ResourceId, s.Booking.Date, s.ProjectId })
            .ToListAsync(ct);
        return rows.Select(r => (r.ResourceId, r.Date, r.ProjectId)).ToHashSet();
    }
}
