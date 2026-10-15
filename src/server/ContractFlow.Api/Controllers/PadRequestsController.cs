using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Additional PAD requests (design §3/§4.3): submitted by Admin/PM, decided by
/// Admin on behalf of the customer. Approval appends an AdditionalApproved
/// ledger entry to the request's project-month.
/// </summary>
[ApiController]
[Route("api/pad-requests")]
public class PadRequestsController(AppDbContext db, PadService pad, ICurrentUser currentUser)
    : ControllerBase
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? projectId, [FromQuery] int? year, [FromQuery] int? month,
        CancellationToken ct)
    {
        var query = db.AdditionalPadRequests.AsQueryable();
        if (projectId.HasValue) query = query.Where(r => r.ProjectId == projectId.Value);
        if (year.HasValue) query = query.Where(r => r.Year == year.Value);
        if (month.HasValue) query = query.Where(r => r.Month == month.Value);

        var requests = await query.OrderByDescending(r => r.CreatedUtc).ToListAsync(ct);
        return Ok(requests.Select(ToDto));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Create(AdditionalPadRequestCreate req, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });
        if (await pad.MonthIsClosedAsync(req.ProjectId, req.Year, req.Month, ct))
            return Conflict(new { error = "monthClosed" });

        var me = await currentUser.GetAsync(ct);
        var request = new AdditionalPadRequest
        {
            ProjectId = req.ProjectId,
            Year = req.Year,
            Month = req.Month,
            Quantity = req.Quantity,
            Rate = req.Rate,
            Reason = req.Reason,
            Reference = req.Reference,
            Status = AdditionalPadStatus.Submitted,
            CreatedByUserId = me?.Id,
        };
        db.AdditionalPadRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(request));
    }

    [HttpPost("{id:guid}/approve")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var request = await db.AdditionalPadRequests.FindAsync([id], ct);
        if (request is null) return NotFound();
        if (request.Status != AdditionalPadStatus.Submitted)
            return BadRequest(new { error = "Only submitted requests can be approved." });
        if (await pad.MonthIsClosedAsync(request.ProjectId, request.Year, request.Month, ct))
            return Conflict(new { error = "monthClosed" });

        var me = await currentUser.GetAsync(ct);
        request.Status = AdditionalPadStatus.Approved;
        request.DecidedUtc = DateTime.UtcNow;
        await pad.AppendAsync(
            request.ProjectId, new DateOnly(request.Year, request.Month, 1),
            PadTransactionType.AdditionalApproved, request.Quantity, me?.Id, ct,
            additionalPadRequestId: request.Id,
            note: request.Reason ?? "Additional PAD approved");
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(request));
    }

    [HttpPost("{id:guid}/reject")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        var request = await db.AdditionalPadRequests.FindAsync([id], ct);
        if (request is null) return NotFound();
        if (request.Status != AdditionalPadStatus.Submitted)
            return BadRequest(new { error = "Only submitted requests can be rejected." });

        request.Status = AdditionalPadStatus.Rejected;
        request.DecidedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(request));
    }

    [HttpPost("{id:guid}/withdraw")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct)
    {
        var request = await db.AdditionalPadRequests.FindAsync([id], ct);
        if (request is null) return NotFound();
        if (request.Status != AdditionalPadStatus.Submitted)
            return BadRequest(new { error = "Only submitted requests can be withdrawn." });

        request.Status = AdditionalPadStatus.Withdrawn;
        request.DecidedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(request));
    }

    /// <summary>Hard delete for undecided requests — decided ones stay as history.</summary>
    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var request = await db.AdditionalPadRequests.FindAsync([id], ct);
        if (request is null) return NotFound();
        if (request.Status is not (AdditionalPadStatus.Draft or AdditionalPadStatus.Submitted))
            return Conflict(new
            {
                error = "decidedRequest",
                message = "Decided requests are history — they cannot be deleted.",
            });

        db.AdditionalPadRequests.Remove(request);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static AdditionalPadRequestDto ToDto(AdditionalPadRequest r) => new(
        r.Id, r.ProjectId, r.Year, r.Month, r.Quantity, r.Rate, r.Reason,
        r.Reference, r.Status, r.CreatedUtc, r.DecidedUtc);
}
