using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Customer invoice specifications (design §3, §6.4): generated per
/// project × month from approved timesheets, then draft → customerReview →
/// final → issued → paid. Frozen once past draft — a paid/issued spec is the
/// audit record, corrections come as revisions.
/// </summary>
[ApiController]
[Route("api/invoices")]
public class InvoicesController(AppDbContext db, ICurrentUser currentUser, BillingService billing)
    : ReportingControllerBase(db, currentUser)
{
    public record InvoiceDto(
        Guid Id, Guid ProjectId, string ProjectName, string CustomerName,
        int Year, int Month, InvoiceStatus Status, string Currency,
        decimal TotalAmount, int LineCount, DateTime CreatedUtc,
        DateTime? IssuedUtc, DateTime? PaidUtc);

    public record InvoiceDetailDto(InvoiceDto Invoice, List<LineDto> Lines);
    public record LineDto(
        Guid Id, InvoiceLineKind Kind, DateOnly? Date, string Description,
        string? Explanation, decimal Quantity, decimal UnitPrice, decimal Amount);

    public record GenerateRequest(Guid ProjectId, int Year, int Month);

    private static InvoiceDto ToDto(InvoiceSpecification s) => new(
        s.Id, s.ProjectId, s.Project.Name, s.Project.Contract.Customer.Name,
        s.Year, s.Month, s.Status, s.Currency, s.TotalAmount, s.Lines.Count,
        s.CreatedUtc, s.IssuedUtc, s.PaidUtc);

    private static LineDto ToDto(InvoiceLine l) => new(
        l.Id, l.Kind, l.Date, l.Description, l.Explanation,
        l.Quantity, l.UnitPrice, l.Amount);

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? projectId, [FromQuery] int? year, [FromQuery] int? month,
        [FromQuery] InvoiceStatus? status, CancellationToken ct)
    {
        var q = Db.InvoiceSpecifications
            .Include(s => s.Project).ThenInclude(p => p.Contract).ThenInclude(c => c.Customer)
            .Include(s => s.Lines).AsQueryable();
        if (projectId.HasValue) q = q.Where(s => s.ProjectId == projectId);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (status.HasValue) q = q.Where(s => s.Status == status);
        var rows = await q.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var s = await LoadAsync(id, ct);
        if (s is null) return NotFound();
        return Ok(new InvoiceDetailDto(ToDto(s), s.Lines.OrderBy(l => l.Date).Select(ToDto).ToList()));
    }

    /// <summary>(Re)generates the draft spec for project × month. Rebuilding a
    /// draft replaces its lines; anything past draft is frozen.</summary>
    [HttpPost("generate")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Generate(GenerateRequest req, CancellationToken ct)
    {
        var me = await Me(ct);
        try
        {
            var s = await billing.GenerateInvoiceAsync(req.ProjectId, req.Year, req.Month, me?.Id, ct);
            return Ok(ToDto((await LoadAsync(s.Id, ct))!));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "invoiceFrozen", message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/review")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public Task<IActionResult> Review(Guid id, CancellationToken ct) =>
        Transition(id, InvoiceStatus.Draft, InvoiceStatus.CustomerReview, ct);

    [HttpPost("{id:guid}/finalize")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public Task<IActionResult> Finalize(Guid id, CancellationToken ct) =>
        Transition(id, InvoiceStatus.CustomerReview, InvoiceStatus.Final, ct);

    [HttpPost("{id:guid}/issue")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Issue(Guid id, CancellationToken ct)
    {
        var result = await Transition(id, InvoiceStatus.Final, InvoiceStatus.Issued, ct);
        if (result is OkObjectResult)
        {
            var s = await Db.InvoiceSpecifications.FindAsync([id], ct);
            s!.IssuedUtc = DateTime.UtcNow;
            await Db.SaveChangesAsync(ct);
        }
        return result;
    }

    [HttpPost("{id:guid}/pay")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Pay(Guid id, CancellationToken ct)
    {
        var result = await Transition(id, InvoiceStatus.Issued, InvoiceStatus.Paid, ct);
        if (result is OkObjectResult)
        {
            var s = await Db.InvoiceSpecifications.FindAsync([id], ct);
            s!.PaidUtc = DateTime.UtcNow;
            await Db.SaveChangesAsync(ct);
        }
        return result;
    }

    /// <summary>Draft specs can be deleted outright (regeneration recreates
    /// them); anything reviewed is the audit trail.</summary>
    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var s = await Db.InvoiceSpecifications.FindAsync([id], ct);
        if (s is null) return NotFound();
        if (s.Status != InvoiceStatus.Draft)
            return Conflict(new
            {
                error = "invoiceFrozen",
                message = "Only draft specifications can be deleted.",
            });
        Db.InvoiceSpecifications.Remove(s);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<IActionResult> Transition(
        Guid id, InvoiceStatus from, InvoiceStatus to, CancellationToken ct)
    {
        var s = await LoadAsync(id, ct);
        if (s is null) return NotFound();
        if (s.Status != from)
            return BadRequest(new { error = $"Specification must be {from} first (is {s.Status})." });
        s.Status = to;
        await Db.SaveChangesAsync(ct);
        return Ok(ToDto(s));
    }

    private Task<InvoiceSpecification?> LoadAsync(Guid id, CancellationToken ct) =>
        Db.InvoiceSpecifications
            .Include(s => s.Project).ThenInclude(p => p.Contract).ThenInclude(c => c.Customer)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
}
