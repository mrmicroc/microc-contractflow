using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Management reporting (design §8). Internal-only: revenue/cost/margin is the
/// fifth ledger — never exposed to resources or customer reviewers (§10). The
/// cost side carries subcontractor rates, so Project Managers (who never see
/// them) have no access either.
/// </summary>
[ApiController]
[Route("api/reports")]
public class ReportsController(AppDbContext db, BillingService billing) : ControllerBase
{
    /// <summary>One project × currency. Amounts in different currencies are
    /// never added together — a project billed in one currency and costed in
    /// another yields two rows.</summary>
    public record MarginRow(
        Guid ProjectId, string ProjectName, string CustomerName, string Currency,
        decimal Billed, decimal Cost, decimal Margin, decimal? MarginPercent,
        BillingService.CostBreakdown? CostBreakdown);

    /// <summary>Per-project margin for a month: invoiced amount (specs, all
    /// statuses and revisions — the "to be billed" basis) minus direct cost
    /// (approved rows priced at agreement rates), per currency.</summary>
    [HttpGet("margins")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Margins(
        [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100)
            return BadRequest(new { error = "month must be 1–12." });

        var billed = await db.InvoiceSpecifications
            .Where(s => s.Year == year && s.Month == month)
            .GroupBy(s => new { s.ProjectId, s.Currency })
            .Select(g => new { g.Key.ProjectId, g.Key.Currency, Total = g.Sum(s => s.TotalAmount) })
            .ToListAsync(ct);

        // Projects to report: billed this month OR carrying approved rows.
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var withCost = await db.TimeEntries
            .Where(e => e.Date >= from && e.Date <= to
                && e.Timesheet != null
                && (e.Timesheet.Status == TimesheetStatus.Approved
                    || e.Timesheet.Status == TimesheetStatus.Invoiced))
            .Select(e => e.ProjectId)
            .Concat(db.Expenses
                .Where(x => x.ProjectId != null && x.Date >= from && x.Date <= to
                    && x.Timesheet != null
                    && (x.Timesheet.Status == TimesheetStatus.Approved
                        || x.Timesheet.Status == TimesheetStatus.Invoiced))
                .Select(x => x.ProjectId!.Value))
            .Distinct().ToListAsync(ct);

        var projectIds = billed.Select(b => b.ProjectId).Union(withCost).ToList();
        var projects = await db.Projects
            .Include(p => p.Contract).ThenInclude(c => c.Customer)
            .Where(p => projectIds.Contains(p.Id))
            .ToListAsync(ct);

        var rows = new List<MarginRow>();
        foreach (var p in projects.OrderBy(p => p.Name))
        {
            var costs = await billing.ProjectCostAsync(p.Id, year, month, ct);
            var currencies = billed.Where(b => b.ProjectId == p.Id).Select(b => b.Currency)
                .Union(costs.Select(c => c.Currency)).Order().ToList();
            foreach (var currency in currencies)
            {
                var rev = billed.Where(b => b.ProjectId == p.Id && b.Currency == currency).Sum(b => b.Total);
                var cost = costs.FirstOrDefault(c => c.Currency == currency);
                var margin = rev - (cost?.Total ?? 0m);
                rows.Add(new MarginRow(
                    p.Id, p.Name, p.Contract.Customer.Name, currency,
                    rev, cost?.Total ?? 0m, margin,
                    rev != 0 ? Math.Round(margin / rev * 100m, 1) : null,
                    cost));
            }
        }
        return Ok(rows);
    }
}
