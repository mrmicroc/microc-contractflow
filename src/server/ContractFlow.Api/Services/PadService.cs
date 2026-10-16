using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Services;

/// <summary>
/// PAD ledger operations (design §4): appends transactions, derives monthly
/// accounts, and performs month-end close (carry-over + expiry). The ledger is
/// append-only — corrections are new rows.
/// </summary>
public class PadService(AppDbContext db)
{
    /// <summary>Terms version in force for the project's contract on a date.</summary>
    public async Task<ContractTerms?> TermsForAsync(
        Guid projectId, DateOnly date, CancellationToken ct)
    {
        var contractId = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => p.ContractId)
            .FirstOrDefaultAsync(ct);
        if (contractId == Guid.Empty) return null;

        return await db.ContractTerms
            .Where(t => t.ContractId == contractId && t.EffectiveFrom <= date)
            .OrderByDescending(t => t.EffectiveFrom)
            .ThenByDescending(t => t.Version)
            .FirstOrDefaultAsync(ct);
    }

    public Task<List<PadTransaction>> MonthAsync(
        Guid projectId, int year, int month, CancellationToken ct) =>
        db.PadTransactions
            .Where(t => t.ProjectId == projectId && t.Year == year && t.Month == month)
            .OrderBy(t => t.CreatedUtc)
            .ToListAsync(ct);

    public async Task<bool> MonthIsClosedAsync(Guid projectId, int year, int month, CancellationToken ct) =>
        await db.ProjectMonthCloses
            .AnyAsync(c => c.ProjectId == projectId && c.Year == year && c.Month == month, ct);

    public async Task<PadTransaction> AppendAsync(
        Guid projectId, DateOnly month, PadTransactionType type, decimal amount,
        Guid? userId, CancellationToken ct,
        Guid? bookingId = null, Guid? additionalPadRequestId = null, string? note = null,
        (int Year, int Month)? origin = null)
    {
        var tx = new PadTransaction
        {
            ProjectId = projectId,
            Year = month.Year,
            Month = month.Month,
            Type = type,
            Amount = amount,
            BookingId = bookingId,
            AdditionalPadRequestId = additionalPadRequestId,
            OriginYear = origin?.Year,
            OriginMonth = origin?.Month,
            Note = note,
            CreatedByUserId = userId,
        };
        db.PadTransactions.Add(tx);
        await db.SaveChangesAsync(ct);
        return tx;
    }

    /// <summary>Open commitment for one booking: commits − releases − utilises.</summary>
    public async Task<decimal> OpenCommitmentAsync(Guid bookingId, CancellationToken ct)
    {
        var txs = await db.PadTransactions
            .Where(t => t.BookingId == bookingId)
            .ToListAsync(ct);
        return txs.Where(t => t.Type == PadTransactionType.Commit).Sum(t => t.Amount)
             - txs.Where(t => t.Type == PadTransactionType.Release).Sum(t => t.Amount)
             - txs.Where(t => t.Type == PadTransactionType.Utilise).Sum(t => t.Amount);
    }

    /// <summary>
    /// Tries to reserve PAD for a booking being confirmed. Succeeds only when
    /// the month's free PAD covers the commitment — approved Additional PAD
    /// already counts into free, so partial availability works automatically
    /// (design §4.3).
    /// </summary>
    public async Task<CommitResult> TryCommitAsync(
        Booking booking, Guid? userId, CancellationToken ct)
    {
        var terms = await TermsForAsync(booking.ProjectId, booking.Date, ct);
        // No PAD component on the in-force terms → confirming is a pure status
        // change; the ledger is untouched (hourly/fixed-price contracts).
        if (terms?.PadEnabled != true)
            return new CommitResult(true, 0m, 0m, MonthClosed: false);

        var amount = PadMath.CommitAmount(
            booking.PlannedHours,
            terms?.PadHoursPerDay ?? 8m,
            terms?.MinPadPerBookedDay ?? 1m);

        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        if (await MonthIsClosedAsync(booking.ProjectId, month.Year, month.Month, ct))
            return new CommitResult(false, 0m, amount, MonthClosed: true);

        var sums = PadMath.Sum(await MonthAsync(booking.ProjectId, month.Year, month.Month, ct));
        if (sums.Free < amount)
            return new CommitResult(false, sums.Free, amount, MonthClosed: false);

        await AppendAsync(booking.ProjectId, month, PadTransactionType.Commit, amount,
            userId, ct, bookingId: booking.Id);
        return new CommitResult(true, sums.Free - amount, amount, MonthClosed: false);
    }

    /// <summary>Writes a Release or Utilise for the booking's open commitment.</summary>
    public async Task<decimal> SettleCommitmentAsync(
        Booking booking, PadTransactionType type, Guid? userId, string? note, CancellationToken ct)
    {
        var open = await OpenCommitmentAsync(booking.Id, ct);
        if (open <= 0) return 0m;
        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        await AppendAsync(booking.ProjectId, month, type, open, userId, ct,
            bookingId: booking.Id, note: note);
        return open;
    }

    /// <summary>
    /// Partial-charge cancellation settlement: utilises
    /// <paramref name="utilisePad"/> PAD of the open commitment and releases
    /// the remainder (stepped cancellation policy, design §4.3).
    /// </summary>
    public async Task<(decimal Utilised, decimal Released)> SettlePartialAsync(
        Booking booking, decimal utilisePad, Guid? userId, CancellationToken ct)
    {
        var open = await OpenCommitmentAsync(booking.Id, ct);
        if (open <= 0) return (0m, 0m);
        var month = new DateOnly(booking.Date.Year, booking.Date.Month, 1);
        var used = Math.Min(open, Math.Max(0m, utilisePad));
        if (used > 0)
            await AppendAsync(booking.ProjectId, month, PadTransactionType.Utilise,
                used, userId, ct, bookingId: booking.Id,
                note: "Cancellation — chargeable portion");
        var rest = open - used;
        if (rest > 0)
            await AppendAsync(booking.ProjectId, month, PadTransactionType.Release,
                rest, userId, ct, bookingId: booking.Id,
                note: "Cancellation — released remainder");
        return (used, rest);
    }

    /// <summary>
    /// Month-end close (design §6.4): releases any still-open commitments,
    /// computes carry-over (only from unused baseline, capped per contract
    /// terms) and expiry, writes the ledger entries for both months and locks
    /// the month. Throws <see cref="InvalidOperationException"/> when already
    /// closed.
    /// </summary>
    public async Task<CloseMonthResultDto> CloseMonthAsync(
        Guid projectId, int year, int month, Guid? userId, CancellationToken ct)
    {
        if (await MonthIsClosedAsync(projectId, year, month, ct))
            throw new InvalidOperationException("Month is already closed.");

        var monthStart = new DateOnly(year, month, 1);
        var txs = await MonthAsync(projectId, year, month, ct);

        // Release commitments that never utilised — dead reservations die with
        // the month rather than dragging into the next one.
        decimal released = 0m;
        foreach (var group in txs.Where(t => t.BookingId != null).GroupBy(t => t.BookingId!.Value))
        {
            var open = group.Where(t => t.Type == PadTransactionType.Commit).Sum(t => t.Amount)
                     - group.Where(t => t.Type == PadTransactionType.Release).Sum(t => t.Amount)
                     - group.Where(t => t.Type == PadTransactionType.Utilise).Sum(t => t.Amount);
            if (open <= 0) continue;
            await AppendAsync(projectId, monthStart, PadTransactionType.Release, open,
                userId, ct, bookingId: group.Key, note: "Released at month-end close");
            released += open;
        }

        var lastDay = monthStart.AddMonths(1).AddDays(-1);
        var terms = await TermsForAsync(projectId, lastDay, ct);
        var maxPercent = terms?.CarryOverMaxPercent ?? 20m;
        var carryMonths = Math.Max(1, terms?.CarryOverMonths ?? 1);
        var closingIndex = year * 12 + month;

        var sums = PadMath.Sum(await MonthAsync(projectId, year, month, ct));
        var carryOver = PadMath.CarryOver(sums.Baseline, sums.Utilised, maxPercent);
        var expired = PadMath.Expiry(sums.Baseline, sums.Utilised, maxPercent);

        var next = monthStart.AddMonths(1);

        // Age living carry-overs: residuals per origin month, then re-carry
        // what's still inside the contract window and expire what's older.
        var allTxs = await db.PadTransactions
            .Where(t => t.ProjectId == projectId)
            .ToListAsync(ct);
        foreach (var r in PadMath.CarryResiduals(allTxs, year, month))
        {
            var age = closingIndex - (r.OriginYear * 12 + r.OriginMonth);
            if (age < carryMonths)
            {
                await AppendAsync(projectId, monthStart, PadTransactionType.CarryOverOut,
                    r.Residual, userId, ct,
                    note: $"Re-carry of PAD from {r.OriginYear}-{r.OriginMonth:D2}",
                    origin: (r.OriginYear, r.OriginMonth));
                await AppendAsync(projectId, next, PadTransactionType.CarryOverIn,
                    r.Residual, userId, ct,
                    note: $"Carry-over from {r.OriginYear}-{r.OriginMonth:D2}",
                    origin: (r.OriginYear, r.OriginMonth));
            }
            else
            {
                await AppendAsync(projectId, monthStart, PadTransactionType.Expire,
                    r.Residual, userId, ct,
                    note: $"Carry-over from {r.OriginYear}-{r.OriginMonth:D2} expired",
                    origin: (r.OriginYear, r.OriginMonth));
                expired += r.Residual;
            }
        }

        if (carryOver > 0)
        {
            await AppendAsync(projectId, monthStart, PadTransactionType.CarryOverOut,
                carryOver, userId, ct,
                note: "Carry-over to next month", origin: (year, month));
            await AppendAsync(projectId, next, PadTransactionType.CarryOverIn,
                carryOver, userId, ct,
                note: $"Carry-over from {year}-{month:D2}", origin: (year, month));
        }
        if (PadMath.Expiry(sums.Baseline, sums.Utilised, maxPercent) is > 0m)
            await AppendAsync(projectId, monthStart, PadTransactionType.Expire,
                PadMath.Expiry(sums.Baseline, sums.Utilised, maxPercent), userId, ct,
                note: "Expired unused baseline");

        db.ProjectMonthCloses.Add(new ProjectMonthClose
        {
            ProjectId = projectId, Year = year, Month = month, ClosedByUserId = userId,
        });
        await db.SaveChangesAsync(ct);

        return new CloseMonthResultDto(
            sums.Baseline, sums.Utilised, released, carryOver, expired,
            carryOver, next.Year, next.Month);
    }

    public record CommitResult(bool Ok, decimal FreeAfter, decimal Amount, bool MonthClosed);
}
