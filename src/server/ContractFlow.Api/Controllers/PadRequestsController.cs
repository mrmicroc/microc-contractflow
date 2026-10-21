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
        if (req.BookingId.HasValue)
        {
            var booking = await db.Bookings.FindAsync([req.BookingId.Value], ct);
            if (booking is null || booking.ProjectId != req.ProjectId
                || booking.Date.Year != req.Year || booking.Date.Month != req.Month)
                return BadRequest(new { error = "The booking must belong to the request's project and month." });
            if (!Prepared(booking))
                return BadRequest(new { error = "Only a prepared (not yet confirmed) booking can wait on a request." });
        }

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
            BookingId = req.BookingId,
            Status = AdditionalPadStatus.Submitted,
            CreatedByUserId = me?.Id,
        };
        db.AdditionalPadRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(request));
    }

    /// <summary>
    /// Approve — appends AdditionalApproved and, when the request names a
    /// prepared booking, commits that booking in the same transaction so the
    /// approved PAD can't be taken by another booking first (§4.3).
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody] AdditionalPadApproveRequest? req, CancellationToken ct)
    {
        var request = await db.AdditionalPadRequests.FindAsync([id], ct);
        if (request is null) return NotFound();
        if (request.Status != AdditionalPadStatus.Submitted)
            return BadRequest(new { error = "Only submitted requests can be approved." });
        if (await pad.MonthIsClosedAsync(request.ProjectId, request.Year, request.Month, ct))
            return Conflict(new { error = "monthClosed" });

        var me = await currentUser.GetAsync(ct);
        try
        {
            await pad.InTransactionAsync(async () =>
            {
                request.Status = AdditionalPadStatus.Approved;
                request.DecidedUtc = DateTime.UtcNow;
                request.ApprovedByRepresentative = req?.ApprovedByRepresentative?.Trim();
                await pad.AppendAsync(
                    request.ProjectId, new DateOnly(request.Year, request.Month, 1),
                    PadTransactionType.AdditionalApproved, request.Quantity, me?.Id, ct,
                    additionalPadRequestId: request.Id,
                    note: request.Reason ?? "Additional PAD approved");

                if (request.BookingId is { } bookingId
                    && await db.Bookings.FindAsync([bookingId], ct) is { } booking
                    && Prepared(booking)
                    && (await pad.TryCommitAsync(booking, me?.Id, ct)).Ok)
                    booking.Status = BookingStatus.Confirmed;
                await db.SaveChangesAsync(ct);
                return true;
            }, ct);
        }
        catch (DbUpdateException)
        {
            // Unique (request, AdditionalApproved) index — a concurrent approve won.
            return Conflict(new { error = "alreadyDecided", message = "The request was decided concurrently." });
        }
        return Ok(ToDto(request));
    }

    private static bool Prepared(Booking b) =>
        b.Status is BookingStatus.Forecast or BookingStatus.Tentative or BookingStatus.Requested;

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
        r.Reference, r.Status, r.CreatedUtc, r.DecidedUtc, r.BookingId,
        r.ApprovedByRepresentative);
}
