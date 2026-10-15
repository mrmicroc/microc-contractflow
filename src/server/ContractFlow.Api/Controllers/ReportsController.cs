using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Management reporting (design §8). Internal-only: revenue/cost/margin is the
/// fifth ledger — never exposed to resources or customer reviewers (§10).
/// </summary>
[ApiController]
[Route("api/reports")]
public class ReportsController(AppDbContext db, BillingService billing) : ControllerBase
{
    public record MarginRow(
        Guid ProjectId, string ProjectName, string CustomerName,
        decimal Billed, decimal Cost, decimal Margin, decimal? MarginPercent,
        BillingService.CostBreakdown CostBreakdown);

    /// <summary>Per-project margin for a month: invoiced amount (specs, all
    /// statuses — the "to be billed" basis) minus direct cost (approved rows
    /// priced at agreement rates).</summary>
    [HttpGet("margins")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Margins(
        [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (month is < 1 or > 12) return BadRequest(new { error = "month must be 1–12." });

        var billed = await db.InvoiceSpecifications
            .Where(s => s.Year == year && s.Month == month)
            .GroupBy(s => s.ProjectId)
            .Select(g => new { ProjectId = g.Key, Total = g.Sum(s => s.TotalAmount) })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Total, ct);

        // Projects to report: billed this month OR carrying approved rows.
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var withCost = await db.TimeEntries
            .Where(e => e.Date >= from && e.Date <= to
                && e.Timesheet != null && e.Timesheet.Status == TimesheetStatus.Approved)
            .Select(e => e.ProjectId)
            .Concat(db.Expenses
                .Where(x => x.ProjectId != null && x.Date >= from && x.Date <= to
                    && x.Timesheet != null && x.Timesheet.Status == TimesheetStatus.Approved)
                .Select(x => x.ProjectId!.Value))
            .Distinct().ToListAsync(ct);

        var projectIds = billed.Keys.Union(withCost).ToList();
        var projects = await db.Projects
            .Include(p => p.Contract).ThenInclude(c => c.Customer)
            .Where(p => projectIds.Contains(p.Id))
            .ToListAsync(ct);

        var rows = new List<MarginRow>();
        foreach (var p in projects.OrderBy(p => p.Name))
        {
            var cost = await billing.ProjectCostAsync(p.Id, year, month, ct);
            var rev = billed.GetValueOrDefault(p.Id);
            var margin = rev - cost.Total;
            rows.Add(new MarginRow(
                p.Id, p.Name, p.Contract.Customer.Name,
                rev, cost.Total, margin,
                rev != 0 ? Math.Round(margin / rev * 100m, 1) : null,
                cost));
        }
        return Ok(rows);
    }
}
