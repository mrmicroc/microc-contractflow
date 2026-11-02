using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Services;

/// <summary>
/// PAD lines of the customer invoice specification. The month's allocation
/// (carry-in + baseline + adjustments) bills on one line; PAD consumed beyond
/// it is always separate — customer-approved Additional PAD per approving
/// request, auto-incurred Additional PAD on its own line. Each bucket bills
/// what its target exceeds the same bucket on earlier revisions, so nothing
/// bills twice across supplements.
/// </summary>
public partial class BillingService
{
    private async Task AddPadLinesAsync(
        InvoiceSpecification spec, Guid projectId, int year, int month, DateOnly to,
        ContractTerms padTerms, List<InvoiceSpecification> others, Action<string> warn,
        CancellationToken ct)
    {
        var tx = await db.PadTransactions
            .Where(t => t.ProjectId == projectId && t.Year == year && t.Month == month)
            .ToListAsync(ct);
        var sums = PadMath.Sum(tx);
        var closed = await db.ProjectMonthCloses
            .AnyAsync(c => c.ProjectId == projectId && c.Year == year && c.Month == month, ct);

        // PAD lent to the previous month's continuation counts as this month's
        // utilised baseline — for invoicing as for carry-over and expiry.
        var pool = sums.Baseline + sums.CarryOverIn + sums.Adjustments;
        var consumed = sums.Utilised + sums.BorrowedFromThisMonth;
        var over = Math.Max(0m, consumed - pool);
        // Approved Additional PAD is consumed before anything is auto-incurred
        // (continuation ② before ④; new services use free PAD incl. approved
        // first). Utilisation beyond every source (no allocation entered, data
        // fixes) stays on the ordinary PAD line.
        var approvedTotal = tx.Where(t => t.Type == PadTransactionType.AdditionalApproved).Sum(t => t.Amount);
        var autoTotal = tx.Where(t => t.Type == PadTransactionType.AutoAdditional).Sum(t => t.Amount);
        var approvedUsed = Math.Min(over, Math.Max(0m, approvedTotal));
        var autoUsed = Math.Min(over - approvedUsed, Math.Max(0m, autoTotal));
        var unsourced = over - approvedUsed - autoUsed;
        var withinPool = Math.Max(0m, Math.Min(consumed, pool)) + unsourced;

        // ── Allocation line ────────────────────────────────────────────────
        decimal allocation;
        string description, explanation;
        if (padTerms.PadInvoicing == PadInvoicing.AllocationConsumed)
        {
            description = string.Create(Inv, $"PAD allocation {year}-{month:00}");
            if (closed)
            {
                // opening carry + baseline − carry-out: utilised within the
                // allocation plus what expired; carried-out PAD bills where it lands.
                allocation = Math.Max(0m, pool - sums.CarryOverOut) + unsourced;
                explanation = string.Create(Inv,
                    $"{sums.CarryOverIn:0.##} carry-in + {sums.Baseline:0.##} baseline")
                    + (sums.Adjustments != 0m ? string.Create(Inv, $" + {sums.Adjustments:0.##} adjustments") : "")
                    + string.Create(Inv,
                        $" − {sums.CarryOverOut:0.##} carried out = {allocation:0.##} PAD ({withinPool:0.##} utilised + {sums.Expired:0.##} expired)");
            }
            else
            {
                allocation = withinPool;
                explanation = string.Create(Inv, $"{withinPool:0.##} PAD utilised within the allocation");
                warn("The PAD month is not closed — expired PAD is not known yet; regenerate (or supplement) after the month-end close.");
            }
        }
        else
        {
            allocation = withinPool;
            description = string.Create(Inv, $"PAD utilised {year}-{month:00}");
            explanation = string.Create(Inv, $"{withinPool:0.##} PAD utilised");
        }
        AddBucket(InvoiceLineKind.Pad, null, allocation, padTerms.PadRate, description, explanation, "PAD rate");

        if (approvedUsed + autoUsed <= 0m) return;

        // ── Additional PAD beyond the allocation ───────────────────────────
        var fallbackRate = padTerms.AdditionalPadRate ?? padTerms.PadRate;

        var requests = await db.AdditionalPadRequests
            .Where(r => r.ProjectId == projectId && r.Year == year && r.Month == month
                && r.Status == AdditionalPadStatus.Approved)
            .OrderBy(r => r.CreatedUtc).ToListAsync(ct);
        var rest = approvedUsed;
        foreach (var r in requests)
        {
            if (rest <= 0m) break;
            var take = Math.Min(rest, r.Quantity);
            rest -= take;
            var rate = r.Rate ?? fallbackRate;
            AddBucket(InvoiceLineKind.AdditionalPad, r.Id, take, rate,
                string.Create(Inv, $"Additional PAD {year}-{month:00}") + (r.Reference is null ? "" : $" · {r.Reference}"),
                string.Create(Inv, $"{take:0.##} approved Additional PAD")
                    + (r.Rate is not null ? " (request rate)" : padTerms.AdditionalPadRate is not null ? " (additional PAD rate)" : ""),
                "Additional PAD rate");
        }
        if (rest > 0m)
            AddBucket(InvoiceLineKind.AdditionalPad, null, rest, fallbackRate,
                string.Create(Inv, $"Additional PAD {year}-{month:00}"),
                string.Create(Inv, $"{rest:0.##} approved Additional PAD"), "Additional PAD rate");
        if (autoUsed > 0m)
            AddBucket(InvoiceLineKind.AutoAdditionalPad, null, autoUsed, fallbackRate,
                string.Create(Inv, $"Additional PAD (auto-incurred) {year}-{month:00}"),
                string.Create(Inv, $"{autoUsed:0.##} PAD beyond the allocation and approved Additional PAD"),
                "Additional PAD rate");

        void AddBucket(InvoiceLineKind kind, Guid? sourceId, decimal target, decimal? rate,
            string lineDescription, string lineExplanation, string rateName)
        {
            var billed = others.SelectMany(s => s.Lines)
                .Where(l => l.Kind == kind && l.SourceId == sourceId).Sum(l => l.Quantity);
            var quantity = PadMath.Round4(target - billed);
            if (quantity < 0m)
            {
                warn(string.Create(Inv,
                    $"{-quantity:0.##} PAD on earlier revisions exceeds what the ledger now shows ({lineDescription}) — a credit is needed."));
                return;
            }
            if (quantity == 0m) return;
            if (rate is not > 0m)
            {
                warn(string.Create(Inv,
                    $"The PAD component has no {rateName} — {quantity:0.##} PAD not billed ({lineDescription})."));
                return;
            }
            spec.Lines.Add(new InvoiceLine
            {
                InvoiceSpecificationId = spec.Id,
                Kind = kind, Date = to, SourceId = sourceId,
                Description = lineDescription,
                Explanation = lineExplanation + string.Create(Inv, $" × {rate.Value:0.##} {spec.Currency}")
                    + (billed > 0m ? string.Create(Inv, $" (after {billed:0.##} PAD on earlier revisions)") : ""),
                Quantity = quantity, UnitPrice = rate.Value,
                Amount = TimeMath.Round2(quantity * rate.Value),
            });
        }
    }

    /// <summary>
    /// Marks rows whose time is covered by the PAD line instead of billing as
    /// hours: booking-bound rows, rows inside a shared booked day, and — when
    /// the terms let unbooked services consume PAD — unbooked contract-mode
    /// rows. (DayBilling covers a flagged row only when its terms have PAD.)
    /// </summary>
    private async Task<List<MonthRow>> MarkPadCoveredAsync(List<MonthRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return rows;
        var contracts = rows.Select(r => r.ContractId).Distinct().ToList();
        var terms = await db.ContractTerms.Where(t => contracts.Contains(t.ContractId)).ToListAsync(ct);
        var shared = await PadCoverage.SharedDaysAsync(db,
            rows.Select(r => r.ResourceId).Distinct().ToList(),
            rows.Min(r => r.Date), rows.Max(r => r.Date), ct);
        return rows.Select(r => r.Booked ? r : r with
        {
            Booked = PadCoverage.Covers(DayBilling.TermsAt(terms, r.ContractId, r.Date), r.Booked,
                r.Billing, shared.Contains((r.ResourceId, r.Date, r.ProjectId))),
        }).ToList();
    }
}
