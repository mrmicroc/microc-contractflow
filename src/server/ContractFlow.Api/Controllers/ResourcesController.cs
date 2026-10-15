using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/resources")]
public class ResourcesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var resources = await db.Resources
            .OrderBy(r => r.DisplayName)
            .Select(r => new ResourceSummaryDto(
                r.Id, r.DisplayName, r.Type, r.IsActive, r.UserId))
            .ToListAsync(ct);
        return Ok(resources);
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var resource = await db.Resources
            .Include(r => r.Leave)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null) return NotFound();
        return Ok(ToDto(resource));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(ResourceRequest req, CancellationToken ct)
    {
        if (req.UserId.HasValue &&
            !await db.Users.AnyAsync(u => u.Id == req.UserId.Value, ct))
            return BadRequest(new { error = "Unknown user." });

        var resource = new Resource
        {
            DisplayName = req.DisplayName.Trim(),
            Type = req.Type,
            Email = req.Email,
            Phone = req.Phone,
            Competencies = req.Competencies,
            UserId = req.UserId,
            IsActive = req.IsActive,
        };
        db.Resources.Add(resource);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = resource.Id }, ToDto(resource));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, ResourceRequest req, CancellationToken ct)
    {
        var resource = await db.Resources
            .Include(r => r.Leave)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null) return NotFound();
        if (req.UserId.HasValue &&
            !await db.Users.AnyAsync(u => u.Id == req.UserId.Value, ct))
            return BadRequest(new { error = "Unknown user." });

        resource.DisplayName = req.DisplayName.Trim();
        resource.Type = req.Type;
        resource.Email = req.Email;
        resource.Phone = req.Phone;
        resource.Competencies = req.Competencies;
        resource.UserId = req.UserId;
        resource.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(resource));
    }

    /// <summary>
    /// Deletes the resource, cascading agreements and leave. Blocked while
    /// bookings exist — deactivate instead to preserve planning history.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var resource = await db.Resources.FindAsync([id], ct);
        if (resource is null) return NotFound();
        if (await db.Bookings.AnyAsync(b => b.ResourceId == id, ct))
            return Conflict(new
            {
                error = "hasDependents",
                message = "Resource has bookings — deactivate instead to preserve history.",
            });

        db.Resources.Remove(resource);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Compensation agreements — internal economics; PMs and resources never
    // see rates (design §10) → Admin + Finance read, Admin writes. ────────────

    [HttpGet("{id:guid}/agreements")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> ListAgreements(Guid id, CancellationToken ct)
    {
        if (!await db.Resources.AnyAsync(r => r.Id == id, ct)) return NotFound();

        var agreements = await db.CompensationAgreements
            .Include(a => a.Contract)
            .Where(a => a.ResourceId == id)
            .OrderBy(a => a.ContractId == null ? 0 : 1)
            .ThenByDescending(a => a.Version)
            .ToListAsync(ct);
        return Ok(agreements.Select(ToDto));
    }

    [HttpPost("{id:guid}/agreements")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> CreateAgreement(
        Guid id, CompensationAgreementRequest req, CancellationToken ct)
    {
        if (!await db.Resources.AnyAsync(r => r.Id == id, ct)) return NotFound();
        if (req.ContractId.HasValue &&
            !await db.Contracts.AnyAsync(c => c.Id == req.ContractId.Value, ct))
            return BadRequest(new { error = "Unknown contract." });

        var nextVersion = await db.CompensationAgreements
            .Where(a => a.ResourceId == id && a.ContractId == req.ContractId)
            .Select(a => (int?)a.Version)
            .MaxAsync(ct) + 1 ?? 1;

        var agreement = new CompensationAgreement
        {
            ResourceId = id,
            ContractId = req.ContractId,
            Version = nextVersion,
            EffectiveFrom = req.EffectiveFrom,
            Currency = req.Currency.ToUpperInvariant(),
            HourlyRate = req.HourlyRate,
            SaturdayRate = req.SaturdayRate,
            SundayRate = req.SundayRate,
            HolidayRate = req.HolidayRate,
            OvertimeAfterHours = req.OvertimeAfterHours,
            OvertimePercent = req.OvertimePercent,
            TravelTimePercent = req.TravelTimePercent,
            MileageRate = req.MileageRate,
            PerDiem = req.PerDiem,
            PerDiemAmount = req.PerDiemAmount,
            ExpensePolicy = req.ExpensePolicy,
            Notes = req.Notes,
        };
        db.CompensationAgreements.Add(agreement);
        await db.SaveChangesAsync(ct);

        agreement.Contract = req.ContractId.HasValue
            ? await db.Contracts.FindAsync([req.ContractId.Value], ct)
            : null;
        return Ok(ToDto(agreement));
    }

    /// <summary>Removes a single agreement version (e.g. entered by mistake).</summary>
    [HttpDelete("{id:guid}/agreements/{agreementId:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> DeleteAgreement(Guid id, Guid agreementId, CancellationToken ct)
    {
        var agreement = await db.CompensationAgreements
            .FirstOrDefaultAsync(a => a.Id == agreementId && a.ResourceId == id, ct);
        if (agreement is null) return NotFound();

        db.CompensationAgreements.Remove(agreement);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Leave / availability ────────────────────────────────────────────────

    [HttpPost("{id:guid}/leave")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> CreateLeave(
        Guid id, ResourceLeaveRequest req, CancellationToken ct)
    {
        if (!await db.Resources.AnyAsync(r => r.Id == id, ct)) return NotFound();
        if (req.To < req.From)
            return BadRequest(new { error = "Leave end cannot precede its start." });

        var leave = new ResourceLeave
        {
            ResourceId = id,
            From = req.From,
            To = req.To,
            Type = req.Type,
            Notes = req.Notes,
        };
        db.ResourceLeaves.Add(leave);
        await db.SaveChangesAsync(ct);

        return Ok(new ResourceLeaveDto(
            leave.Id, leave.ResourceId, leave.From, leave.To, leave.Type, leave.Notes));
    }

    [HttpDelete("{id:guid}/leave/{leaveId:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> DeleteLeave(Guid id, Guid leaveId, CancellationToken ct)
    {
        var leave = await db.ResourceLeaves
            .FirstOrDefaultAsync(l => l.Id == leaveId && l.ResourceId == id, ct);
        if (leave is null) return NotFound();

        db.ResourceLeaves.Remove(leave);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ResourceDetailDto ToDto(Resource r) => new(
        r.Id, r.DisplayName, r.Type, r.Email, r.Phone, r.Competencies,
        r.IsActive, r.UserId,
        r.Leave.OrderBy(l => l.From)
            .Select(l => new ResourceLeaveDto(l.Id, l.ResourceId, l.From, l.To, l.Type, l.Notes))
            .ToList());

    private static CompensationAgreementDto ToDto(CompensationAgreement a) => new(
        a.Id, a.ResourceId, a.ContractId, a.Contract?.Name,
        a.Version, a.EffectiveFrom, a.CreatedUtc,
        a.Currency, a.HourlyRate, a.SaturdayRate, a.SundayRate, a.HolidayRate,
        a.OvertimeAfterHours, a.OvertimePercent, a.TravelTimePercent,
        a.MileageRate, a.PerDiem, a.PerDiemAmount, a.ExpensePolicy, a.Notes);
}
