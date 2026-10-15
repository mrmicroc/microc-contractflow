using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/quantities")]
public class EstimatedQuantitiesController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(EstimatedQuantityRequest req, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });

        var quantity = new EstimatedQuantity
        {
            ProjectId = req.ProjectId,
            Label = req.Label.Trim(),
            Quantity = req.Quantity,
            Unit = req.Unit.Trim(),
            Notes = req.Notes,
        };
        db.EstimatedQuantities.Add(quantity);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(quantity));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(
        Guid id, EstimatedQuantityRequest req, CancellationToken ct)
    {
        var quantity = await db.EstimatedQuantities.FindAsync([id], ct);
        if (quantity is null) return NotFound();
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });

        quantity.ProjectId = req.ProjectId;
        quantity.Label = req.Label.Trim();
        quantity.Quantity = req.Quantity;
        quantity.Unit = req.Unit.Trim();
        quantity.Notes = req.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(quantity));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var quantity = await db.EstimatedQuantities.FindAsync([id], ct);
        if (quantity is null) return NotFound();

        db.EstimatedQuantities.Remove(quantity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static EstimatedQuantityDto ToDto(EstimatedQuantity q) =>
        new(q.Id, q.ProjectId, q.Label, q.Quantity, q.Unit, q.Notes);
}
