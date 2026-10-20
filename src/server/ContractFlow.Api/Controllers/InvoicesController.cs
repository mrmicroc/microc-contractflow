using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Customer invoice specifications (design §3, §6.4): generated per
/// project × month (× revision) from approved timesheets, then draft →
/// customerReview → final → issued → paid (customerReview may go back to
/// draft). Frozen once past draft — rows approved later go on a supplement
/// revision, never by editing an issued spec.
/// </summary>
[ApiController]
[Route("api/invoices")]
public class InvoicesController(AppDbContext db, ICurrentUser currentUser, BillingService billing)
    : ReportingControllerBase(db, currentUser)
{
    public record InvoiceDto(
        Guid Id, Guid ProjectId, string ProjectName, string CustomerName,
        int Year, int Month, int Revision, InvoiceStatus Status, string Currency,
        decimal TotalAmount, int LineCount, DateTime CreatedUtc,
        DateTime? IssuedUtc, Guid? IssuedByUserId, DateTime? PaidUtc, Guid? PaidByUserId,
        List<string> Warnings);

    public record InvoiceDetailDto(InvoiceDto Invoice, List<LineDto> Lines);
    public record LineDto(
        Guid Id, InvoiceLineKind Kind, DateOnly? Date, string Description,
        string? Explanation, decimal Quantity, decimal UnitPrice, decimal Amount);

    /// <summary><paramref name="Supplement"/>: when the month's latest
    /// revision has left draft, create the next revision with what is still
    /// unbilled instead of refusing.</summary>
    public record GenerateRequest(Guid ProjectId, int Year, int Month, bool Supplement = false);

    private static InvoiceDto ToDto(InvoiceSpecification s) => new(
        s.Id, s.ProjectId, s.Project.Name, s.Project.Contract.Customer.Name,
        s.Year, s.Month, s.Revision, s.Status, s.Currency, s.TotalAmount, s.Lines.Count,
        s.CreatedUtc, s.IssuedUtc, s.IssuedByUserId, s.PaidUtc, s.PaidByUserId,
        BillingService.Warnings(s.WarningsJson));

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
        var rows = await q.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .ThenBy(s => s.Revision).ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    /// <summary>Approved rows of the month no specification has billed yet —
    /// the input for a supplement revision and a month-end warning.</summary>
    [HttpGet("unbilled")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Unbilled(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? projectId, CancellationToken ct)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100)
            return BadRequest(new { error = "Invalid year/month." });
        return Ok(await billing.UnbilledAsync(year, month, projectId, ct));
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
    /// draft replaces its lines; anything past draft is frozen — pass
    /// supplement=true to bill what was approved since.</summary>
    [HttpPost("generate")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Generate(GenerateRequest req, CancellationToken ct)
    {
        if (req.Month is < 1 or > 12 || req.Year is < 2000 or > 2100)
            return BadRequest(new { error = "Invalid year/month." });
        var me = await Me(ct);
        try
        {
            var s = await billing.GenerateInvoiceAsync(
                req.ProjectId, req.Year, req.Month, me?.Id, ct, req.Supplement);
            return Ok(ToDto((await LoadAsync(s.Id, ct))!));
        }
        catch (BillingException ex)
        {
            return Conflict(new { error = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/review")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public Task<IActionResult> Review(Guid id, CancellationToken ct) =>
        Transition(id, InvoiceStatus.Draft, InvoiceStatus.CustomerReview, ct);

    /// <summary>Customer review sent it back — the spec returns to draft so it
    /// can be regenerated or corrected.</summary>
    [HttpPost("{id:guid}/back-to-draft")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public Task<IActionResult> BackToDraft(Guid id, CancellationToken ct) =>
        Transition(id, InvoiceStatus.CustomerReview, InvoiceStatus.Draft, ct);

    [HttpPost("{id:guid}/finalize")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public Task<IActionResult> Finalize(Guid id, CancellationToken ct) =>
        Transition(id, InvoiceStatus.CustomerReview, InvoiceStatus.Final, ct);

    /// <summary>Issue — stamps who/when and moves fully billed timesheets to
    /// Invoiced, in one transaction.</summary>
    [HttpPost("{id:guid}/issue")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Issue(Guid id, CancellationToken ct)
    {
        var me = await Me(ct);
        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        var result = await Transition(id, InvoiceStatus.Final, InvoiceStatus.Issued, ct, s =>
        {
            s.IssuedUtc = DateTime.UtcNow;
            s.IssuedByUserId = me?.Id;
        });
        if (result is OkObjectResult)
        {
            await billing.RefreshInvoicedAsync(id, ct);
            await Db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return result;
    }

    [HttpPost("{id:guid}/pay")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Pay(Guid id, CancellationToken ct)
    {
        var me = await Me(ct);
        return await Transition(id, InvoiceStatus.Issued, InvoiceStatus.Paid, ct, s =>
        {
            s.PaidUtc = DateTime.UtcNow;
            s.PaidByUserId = me?.Id;
        });
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
        await Db.TimeEntries.Where(e => e.InvoiceSpecificationId == id)
            .ExecuteUpdateAsync(x => x.SetProperty(e => e.InvoiceSpecificationId, (Guid?)null), ct);
        Db.InvoiceSpecifications.Remove(s);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Status change + optional stamping in a single SaveChanges.</summary>
    private async Task<IActionResult> Transition(
        Guid id, InvoiceStatus from, InvoiceStatus to, CancellationToken ct,
        Action<InvoiceSpecification>? apply = null)
    {
        var s = await LoadAsync(id, ct);
        if (s is null) return NotFound();
        if (s.Status != from)
            return BadRequest(new { error = $"Specification must be {from} first (is {s.Status})." });
        s.Status = to;
        apply?.Invoke(s);
        await Db.SaveChangesAsync(ct);
        return Ok(ToDto(s));
    }

    private Task<InvoiceSpecification?> LoadAsync(Guid id, CancellationToken ct) =>
        Db.InvoiceSpecifications
            .Include(s => s.Project).ThenInclude(p => p.Contract).ThenInclude(c => c.Customer)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
}
