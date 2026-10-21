using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// One-off dated amounts on a contract (milestones, instalments, set-up
/// fees) — not versioned terms but events. Each bills once, on its project's
/// specification for the month of its date; once that spec has left draft
/// the charge is frozen.
/// </summary>
[ApiController]
[Route("api/contracts/{contractId:guid}/charges")]
public class ScheduledChargesController(AppDbContext db, BillingService billing) : ControllerBase
{
    /// <summary><c>ProjectId</c> null = billed on the contract's default
    /// project (first active by name) — <c>BilledProjectId</c> says which.</summary>
    public record ChargeDto(
        Guid Id, Guid ContractId, Guid? ProjectId, string? ProjectName, Guid? BilledProjectId,
        DateOnly Date, decimal Amount, string Description, bool Invoiced);

    public class ChargeRequest
    {
        public Guid? ProjectId { get; set; }
        [Required]
        public DateOnly Date { get; set; }
        [Range(0.01, 1_000_000_000)]
        public decimal Amount { get; set; }
        [Required, StringLength(300, MinimumLength = 1)]
        public required string Description { get; set; }
    }

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List(Guid contractId, CancellationToken ct)
    {
        if (!await db.Contracts.AnyAsync(c => c.Id == contractId, ct)) return NotFound();
        var rows = await db.ScheduledCharges.Include(c => c.Project)
            .Where(c => c.ContractId == contractId)
            .OrderBy(c => c.Date).ThenBy(c => c.CreatedUtc).ToListAsync(ct);
        var fallback = await billing.DefaultChargeProjectAsync(contractId, ct);
        var result = new List<ChargeDto>();
        foreach (var c in rows) result.Add(await ToDtoAsync(c, fallback, ct));
        return Ok(result);
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(Guid contractId, ChargeRequest req, CancellationToken ct)
    {
        if (!await db.Contracts.AnyAsync(c => c.Id == contractId, ct)) return NotFound();
        if (await ValidateProjectAsync(contractId, req.ProjectId, ct) is { } bad) return bad;
        var charge = new ScheduledCharge
        {
            ContractId = contractId, ProjectId = req.ProjectId, Date = req.Date,
            Amount = req.Amount, Description = req.Description.Trim(),
        };
        db.ScheduledCharges.Add(charge);
        await db.SaveChangesAsync(ct);
        return Created($"/api/contracts/{contractId}/charges/{charge.Id}", await DtoAsync(charge.Id, ct));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid contractId, Guid id, ChargeRequest req, CancellationToken ct)
    {
        var charge = await db.ScheduledCharges.FirstOrDefaultAsync(c => c.Id == id && c.ContractId == contractId, ct);
        if (charge is null) return NotFound();
        if (await billing.ChargeInvoicedAsync(id, ct)) return InvoicedError();
        if (await ValidateProjectAsync(contractId, req.ProjectId, ct) is { } bad) return bad;
        charge.ProjectId = req.ProjectId;
        charge.Date = req.Date;
        charge.Amount = req.Amount;
        charge.Description = req.Description.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(await DtoAsync(id, ct));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Delete(Guid contractId, Guid id, CancellationToken ct)
    {
        var charge = await db.ScheduledCharges.FirstOrDefaultAsync(c => c.Id == id && c.ContractId == contractId, ct);
        if (charge is null) return NotFound();
        if (await billing.ChargeInvoicedAsync(id, ct)) return InvoicedError();
        // Lines on a draft spec drop out at its next regeneration.
        db.ScheduledCharges.Remove(charge);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<IActionResult?> ValidateProjectAsync(Guid contractId, Guid? projectId, CancellationToken ct) =>
        projectId is null || await db.Projects.AnyAsync(p => p.Id == projectId && p.ContractId == contractId, ct)
            ? null
            : BadRequest(new { error = "The project does not belong to this contract." });

    private ConflictObjectResult InvoicedError() => Conflict(new
    {
        error = "invoiced",
        message = "The charge is on a specification beyond draft — it can no longer change.",
    });

    private async Task<ChargeDto> DtoAsync(Guid id, CancellationToken ct)
    {
        var c = await db.ScheduledCharges.Include(x => x.Project).FirstAsync(x => x.Id == id, ct);
        return await ToDtoAsync(c, await billing.DefaultChargeProjectAsync(c.ContractId, ct), ct);
    }

    private async Task<ChargeDto> ToDtoAsync(ScheduledCharge c, Guid? fallback, CancellationToken ct) => new(
        c.Id, c.ContractId, c.ProjectId, c.Project?.Name, c.ProjectId ?? fallback,
        c.Date, c.Amount, c.Description, await billing.ChargeInvoicedAsync(c.Id, ct));
}
