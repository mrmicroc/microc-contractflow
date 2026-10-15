using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>Monthly PAD account per project — the dashboard of design §8.</summary>
[ApiController]
[Route("api/projects/{projectId:guid}/pad")]
public class PadController(AppDbContext db, PadService pad, ICurrentUser currentUser)
    : ControllerBase
{
    [HttpGet("{year:int}/{month:int}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Dashboard(
        Guid projectId, int year, int month, CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([projectId], ct);
        if (project is null) return NotFound();
        if (month is < 1 or > 12) return BadRequest(new { error = "Invalid month." });

        var txs = await pad.MonthAsync(projectId, year, month, ct);
        var sums = PadMath.Sum(txs);
        var close = await db.ProjectMonthCloses
            .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.Year == year && c.Month == month, ct);

        return Ok(new PadDashboardDto(
            project.Id, project.Name, year, month,
            sums.Baseline, sums.CarryOverIn, sums.AdditionalApproved, sums.CommittedOpen,
            sums.Utilised, sums.Released, sums.Expired, sums.CarryOverOut,
            sums.BorrowedFromThisMonth, sums.Adjustments, sums.TotalAvailable, sums.Free,
            close != null, close?.ClosedUtc,
            txs.Select(t => new PadTransactionDto(
                t.Id, t.Type, t.Amount, t.BookingId, t.Note, t.CreatedUtc)).ToList()));
    }

    /// <summary>
    /// Sets the month's baseline. BaselineSet rows are deltas: posting "set to
    /// X" appends a row of (X − current baseline), so the ledger stays
    /// append-only and every amendment is visible in the transaction list.
    /// </summary>
    [HttpPost("{year:int}/{month:int}/baseline")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> SetBaseline(
        Guid projectId, int year, int month, BaselineRequest req, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return NotFound();
        if (month is < 1 or > 12) return BadRequest(new { error = "Invalid month." });
        if (await pad.MonthIsClosedAsync(projectId, year, month, ct))
            return Conflict(new { error = "monthClosed" });

        var sums = PadMath.Sum(await pad.MonthAsync(projectId, year, month, ct));
        var delta = req.Amount - sums.Baseline;
        if (delta == 0)
            return Ok(new { baseline = sums.Baseline, changed = false });

        var me = await currentUser.GetAsync(ct);
        var note = $"Baseline set to {req.Amount}" + (req.Note is null ? "" : $" — {req.Note}");
        var tx = await pad.AppendAsync(projectId, new DateOnly(year, month, 1),
            PadTransactionType.BaselineSet, delta, me?.Id, ct, note: note);
        return Ok(new PadTransactionDto(tx.Id, tx.Type, tx.Amount, tx.BookingId, tx.Note, tx.CreatedUtc));
    }

    /// <summary>
    /// Month-end close: releases open commitments, writes carry-over / expiry
    /// for this month and CarryOverIn for the next, then locks the month.
    /// </summary>
    [HttpPost("{year:int}/{month:int}/close")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Close(Guid projectId, int year, int month, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return NotFound();
        if (month is < 1 or > 12) return BadRequest(new { error = "Invalid month." });

        var me = await currentUser.GetAsync(ct);
        try
        {
            var result = await pad.CloseMonthAsync(projectId, year, month, me?.Id, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "monthClosed", message = ex.Message });
        }
    }
}
