using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>Expenses with the three independent flags (design §6.2):
/// reimbursable-to-resource / rechargeable-to-customer / included-in-rate.
/// Receipt upload lands with blob storage — ReceiptRef meanwhile.</summary>
[ApiController]
[Route("api/expenses")]
public class ExpensesController(AppDbContext db, ICurrentUser currentUser, PadService pad)
    : ReportingControllerBase(db, currentUser)
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? resourceId, [FromQuery] Guid? projectId,
        CancellationToken ct)
    {
        var scoped = await ScopedResourceIdAsync(ct);
        var query = Db.Expenses
            .Include(x => x.Resource).Include(x => x.Project).AsQueryable();
        if (scoped.HasValue) query = query.Where(x => x.ResourceId == scoped.Value);
        else if (resourceId.HasValue) query = query.Where(x => x.ResourceId == resourceId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Date <= to.Value);
        return Ok((await query.OrderBy(x => x.Date).ToListAsync(ct)).Select(ToDto));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Create(ExpenseRequest req, CancellationToken ct)
    {
        var (resourceId, error) = await WritableResourceAsync(req.ResourceId, ct);
        if (error is not null) return error;
        var projectError = await ValidateAsync(req.ProjectId, req.BookingId, resourceId!.Value, req.Date, ct);
        if (projectError is not null) return projectError;
        if (req.IncludedInRate && (req.ReimbursableToResource || req.RechargeableToCustomer))
            return BadRequest(new { error = "An expense included in the rate cannot also be reimbursed or recharged." });

        var me = await Me(ct);
        var row = new Expense
        {
            ResourceId = resourceId.Value,
            ProjectId = req.ProjectId,
            BookingId = req.BookingId,
            Date = req.Date,
            Category = req.Category,
            Amount = req.Amount,
            Currency = req.Currency.ToUpperInvariant(),
            Description = req.Description,
            ReceiptRef = req.ReceiptRef,
            ReimbursableToResource = req.ReimbursableToResource,
            RechargeableToCustomer = req.RechargeableToCustomer,
            IncludedInRate = req.IncludedInRate,
            CreatedByUserId = me?.Id,
        };
        Db.Expenses.Add(row);
        await Db.SaveChangesAsync(ct);
        return Created($"/api/expenses/{row.Id}", await DtoAsync(row.Id, ct));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Update(Guid id, ExpenseRequest req, CancellationToken ct)
    {
        var row = await Db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        if (!await OwnsAsync(row.ResourceId, ct)) return ForbidResult();
        if (await LockedAsync(row.TimesheetId, ct)) return LockedError();
        var projectError = await ValidateAsync(req.ProjectId, req.BookingId, row.ResourceId, req.Date, ct);
        if (projectError is not null) return projectError;
        if (req.IncludedInRate && (req.ReimbursableToResource || req.RechargeableToCustomer))
            return BadRequest(new { error = "An expense included in the rate cannot also be reimbursed or recharged." });

        row.ProjectId = req.ProjectId;
        row.BookingId = req.BookingId;
        row.Date = req.Date;
        row.Category = req.Category;
        row.Amount = req.Amount;
        row.Currency = req.Currency.ToUpperInvariant();
        row.Description = req.Description;
        row.ReceiptRef = req.ReceiptRef;
        row.ReimbursableToResource = req.ReimbursableToResource;
        row.RechargeableToCustomer = req.RechargeableToCustomer;
        row.IncludedInRate = req.IncludedInRate;
        await Db.SaveChangesAsync(ct);
        return Ok(await DtoAsync(id, ct));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Resource)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var row = await Db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        if (!await OwnsAsync(row.ResourceId, ct)) return ForbidResult();
        if (await LockedAsync(row.TimesheetId, ct)) return LockedError();
        Db.Expenses.Remove(row);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<IActionResult?> ValidateAsync(
        Guid? projectId, Guid? bookingId, Guid resourceId, DateOnly date, CancellationToken ct)
    {
        if (projectId.HasValue)
        {
            if (!await Db.Projects.AnyAsync(p => p.Id == projectId && p.IsActive, ct))
                return BadRequest(new { error = "Unknown or inactive project." });
            if (await pad.MonthIsClosedAsync(projectId.Value, date.Year, date.Month, ct))
                return Conflict(new { error = "monthClosed", message = "The project month is closed." });
        }
        if (bookingId.HasValue && !await Db.Bookings.AnyAsync(
                b => b.Id == bookingId.Value && b.ResourceId == resourceId, ct))
            return BadRequest(new { error = "Booking does not belong to this resource." });
        return null;
    }

    private async Task<ExpenseDto> DtoAsync(Guid id, CancellationToken ct) =>
        ToDto(await Db.Expenses
            .Include(x => x.Resource).Include(x => x.Project)
            .FirstAsync(x => x.Id == id, ct));

    private static ExpenseDto ToDto(Expense x) => new(
        x.Id, x.ResourceId, x.Resource.DisplayName, x.ProjectId, x.Project?.Name,
        x.BookingId, x.Date, x.Category, x.Amount, x.Currency, x.Description,
        x.ReceiptRef, x.ReimbursableToResource, x.RechargeableToCustomer,
        x.IncludedInRate, x.TimesheetId);
}
