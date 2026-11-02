using System.Globalization;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// A project's baseline schedule — the contracted monthly PAD allocation
/// entered as one grid instead of month by month, checked against the
/// project's contracted total. Writes go through the same append-only
/// BaselineSet deltas as the single-month endpoint, so every amendment stays
/// visible in the ledger.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/pad/baseline-schedule")]
public class BaselineScheduleController(AppDbContext db, PadService pad, ICurrentUser currentUser)
    : ControllerBase
{
    public record MonthAmount(int Year, int Month, decimal Amount);

    /// <summary><c>Difference</c> = contracted total − scheduled sum (null when
    /// no total is set); <c>Warning</c> explains a non-zero difference.</summary>
    public record ScheduleDto(
        List<MonthAmount> Months, decimal Scheduled, decimal? ContractedTotal,
        decimal? Difference, string? Warning);

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();
        return Ok(await ScheduleAsync(project, ct));
    }

    /// <summary>Sets every listed month's baseline (others untouched). All or
    /// nothing: any closed month refuses the whole schedule with 409.</summary>
    [HttpPut]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Put(Guid projectId, List<MonthAmount> months, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();
        if (months.Any(m => m.Month is < 1 or > 12 || m.Year is < 2000 or > 2100 || m.Amount < 0m))
            return BadRequest(new { error = "Each row needs a month 1–12, a year 2000–2100 and an amount ≥ 0." });
        if (months.GroupBy(m => (m.Year, m.Month)).Any(g => g.Count() > 1))
            return BadRequest(new { error = "A month may appear only once." });

        var closed = new List<string>();
        foreach (var m in months)
            if (await pad.MonthIsClosedAsync(projectId, m.Year, m.Month, ct))
                closed.Add(string.Create(CultureInfo.InvariantCulture, $"{m.Year}-{m.Month:00}"));
        if (closed.Count > 0)
            return Conflict(new { error = "monthClosed", message = "Closed months cannot change baseline.", months = closed });

        var me = await currentUser.GetAsync(ct);
        await pad.InTransactionAsync(async () =>
        {
            foreach (var m in months.OrderBy(m => m.Year).ThenBy(m => m.Month))
            {
                var current = PadMath.Sum(await pad.MonthAsync(projectId, m.Year, m.Month, ct)).Baseline;
                var delta = m.Amount - current;
                if (delta == 0m) continue;
                await pad.AppendAsync(projectId, new DateOnly(m.Year, m.Month, 1),
                    PadTransactionType.BaselineSet, delta, me?.Id, ct,
                    note: string.Create(CultureInfo.InvariantCulture, $"Baseline set to {m.Amount} (schedule)"));
            }
            return true;
        }, ct);
        return Ok(await ScheduleAsync(project, ct));
    }

    private async Task<ScheduleDto> ScheduleAsync(Project project, CancellationToken ct)
    {
        var rows = (await db.PadTransactions
                .Where(t => t.ProjectId == project.Id && t.Type == PadTransactionType.BaselineSet)
                .Select(t => new { t.Year, t.Month, t.Amount })
                .ToListAsync(ct))
            .GroupBy(t => new { t.Year, t.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Amount = g.Sum(t => t.Amount) });
        var months = rows
            .Where(r => r.Amount != 0m)
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .Select(r => new MonthAmount(r.Year, r.Month, r.Amount)).ToList();
        var scheduled = months.Sum(m => m.Amount);
        decimal? diff = project.ContractedBaselinePad is { } total ? total - scheduled : null;
        var warning = diff is { } d && d != 0m
            ? string.Create(CultureInfo.InvariantCulture,
                $"The schedule totals {scheduled:0.##} PAD; the contracted total is {project.ContractedBaselinePad:0.##} ({d:+0.##;-0.##} unscheduled).")
            : null;
        return new ScheduleDto(months, scheduled, project.ContractedBaselinePad, diff, warning);
    }
}
