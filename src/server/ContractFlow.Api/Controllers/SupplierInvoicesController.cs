using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Supplier invoices (design §6.2): the subcontractor's own incoming invoice,
/// registered by finance/admin and matched against the statement for the
/// period — per-kind diffs stay visible for review.
/// </summary>
[ApiController]
[Route("api/supplier-invoices")]
public class SupplierInvoicesController(AppDbContext db, ICurrentUser currentUser)
    : ReportingControllerBase(db, currentUser)
{
    public record SupplierInvoiceDto(
        Guid Id, Guid ResourceId, string ResourceName, string Number,
        DateOnly InvoiceDate, DateOnly? DueDate, string Currency, decimal TotalAmount,
        SupplierInvoiceStatus Status, Guid? StatementId, DateTime? PaidUtc,
        string? ApprovalNote);

    public record SupplierInvoiceDetailDto(
        SupplierInvoiceDto Invoice, List<LineDto> Lines, List<BillingService.MatchDiff>? Diffs);
    public record LineDto(Guid Id, StatementLineKind Kind, string Description, decimal Amount);

    public record RegisterRequest(
        Guid ResourceId, string Number, DateOnly InvoiceDate, DateOnly? DueDate,
        string Currency, decimal TotalAmount,
        List<LineInput>? Lines);
    public record LineInput(StatementLineKind Kind, string Description, decimal Amount);
    public record MatchRequest(Guid StatementId);
    public record ApproveRequest(string? Note);

    private static SupplierInvoiceDto ToDto(SupplierInvoice x) => new(
        x.Id, x.ResourceId, x.Resource.DisplayName, x.Number, x.InvoiceDate, x.DueDate,
        x.Currency, x.TotalAmount, x.Status, x.StatementId, x.PaidUtc, x.ApprovalNote);

    private static LineDto ToDto(SupplierInvoiceLine l) => new(l.Id, l.Kind, l.Description, l.Amount);

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? resourceId, [FromQuery] SupplierInvoiceStatus? status, CancellationToken ct)
    {
        var scoped = await ScopedResourceIdAsync(ct);
        var q = Db.SupplierInvoices.Include(x => x.Resource).AsQueryable();
        if (scoped.HasValue) q = q.Where(x => x.ResourceId == scoped.Value);
        else if (resourceId.HasValue) q = q.Where(x => x.ResourceId == resourceId);
        if (status.HasValue) q = q.Where(x => x.Status == status);
        var rows = await q.OrderByDescending(x => x.InvoiceDate).ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var x = await LoadAsync(id, ct);
        if (x is null) return NotFound();
        var scoped = await ScopedResourceIdAsync(ct);
        if (scoped.HasValue && scoped.Value != x.ResourceId) return ForbidResult();

        var diffs = x.MatchDiffJson is null ? null
            : JsonSerializer.Deserialize<List<BillingService.MatchDiff>>(x.MatchDiffJson);
        return Ok(new SupplierInvoiceDetailDto(ToDto(x), x.Lines.Select(ToDto).ToList(), diffs));
    }

    /// <summary>Registers the incoming invoice. Total must equal its lines'
    /// sum — the check catches transcription slips at the door.</summary>
    [HttpPost]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
    {
        if (!await Db.Resources.AnyAsync(r => r.Id == req.ResourceId && r.IsActive, ct))
            return BadRequest(new { error = "Unknown or inactive resource." });
        var lines = req.Lines ?? [];
        var sum = lines.Sum(l => l.Amount);
        if (lines.Count > 0 && Math.Abs(sum - req.TotalAmount) > 0.01m)
            return BadRequest(new
            {
                error = "totalMismatch",
                message = $"Total {req.TotalAmount} does not match the lines' sum {sum}.",
            });
        if (await Db.SupplierInvoices.AnyAsync(
            x => x.ResourceId == req.ResourceId && x.Number == req.Number, ct))
            return Conflict(new { error = "duplicate", message = "Invoice number already registered for this resource." });

        var me = await Me(ct);
        var inv = new SupplierInvoice
        {
            Id = Guid.NewGuid(),
            ResourceId = req.ResourceId,
            Number = req.Number.Trim(),
            InvoiceDate = req.InvoiceDate,
            DueDate = req.DueDate,
            Currency = req.Currency,
            TotalAmount = lines.Count > 0 ? sum : req.TotalAmount,
            CreatedByUserId = me?.Id,
        };
        foreach (var l in lines)
            inv.Lines.Add(new SupplierInvoiceLine
            {
                Id = Guid.NewGuid(), SupplierInvoiceId = inv.Id,
                Kind = l.Kind, Description = l.Description, Amount = l.Amount,
            });
        Db.SupplierInvoices.Add(inv);
        await Db.SaveChangesAsync(ct);
        return Ok(ToDto((await LoadAsync(inv.Id, ct))!));
    }

    /// <summary>Matches the invoice against a statement — per-kind diffs are
    /// computed and stored; the outcome lands on Matched or Discrepancy.</summary>
    [HttpPost("{id:guid}/match")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Match(Guid id, MatchRequest req, CancellationToken ct)
    {
        var x = await LoadAsync(id, ct);
        if (x is null) return NotFound();
        if (x.Status is SupplierInvoiceStatus.Approved or SupplierInvoiceStatus.Paid)
            return Conflict(new { error = "invoiceApproved", message = "Approved invoices can't be re-matched." });
        var st = await Db.SubcontractorStatements.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == req.StatementId, ct);
        if (st is null) return NotFound(new { error = "Unknown statement." });
        if (st.ResourceId != x.ResourceId)
            return BadRequest(new { error = "Statement belongs to another resource." });
        if (st.Status == StatementStatus.Draft)
            return BadRequest(new { error = "The statement is still a draft — finalize it first." });
        if (!string.Equals(st.Currency, x.Currency, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new
            {
                error = "currencyMismatch",
                message = $"The invoice is in {x.Currency} but the statement in {st.Currency}.",
            });
        // One active invoice per statement — a second match would pay twice.
        if (await Db.SupplierInvoices.AnyAsync(o => o.Id != x.Id && o.StatementId == st.Id
                && o.Status != SupplierInvoiceStatus.Registered, ct))
            return Conflict(new
            {
                error = "statementAlreadyMatched",
                message = "Another supplier invoice is already matched against this statement.",
            });

        var diffs = BillingService.MatchInvoice(x.Lines, st.Lines, x.TotalAmount);
        x.StatementId = st.Id;
        x.MatchDiffJson = JsonSerializer.Serialize(diffs);
        x.Status = BillingService.MatchOutcome(diffs);
        await Db.SaveChangesAsync(ct);
        return Ok(new SupplierInvoiceDetailDto(ToDto(x), x.Lines.Select(ToDto).ToList(), diffs));
    }

    /// <summary>Approve a matched (or discrepancy-reviewed) invoice for payment.
    /// Approving a Discrepancy requires a note explaining why.</summary>
    [HttpPost("{id:guid}/approve")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Approve(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
        ApproveRequest? req,
        CancellationToken ct)
    {
        var x = await LoadAsync(id, ct);
        if (x is null) return NotFound();
        if (x.Status is not (SupplierInvoiceStatus.Matched or SupplierInvoiceStatus.Discrepancy))
            return BadRequest(new { error = $"Invoice must be matched first (is {x.Status})." });
        var note = req?.Note?.Trim();
        if (x.Status == SupplierInvoiceStatus.Discrepancy && string.IsNullOrEmpty(note))
            return BadRequest(new
            {
                error = "noteRequired",
                message = "Approving an invoice with a discrepancy requires a note.",
            });
        x.ApprovalNote = string.IsNullOrEmpty(note) ? null : note;
        x.Status = SupplierInvoiceStatus.Approved;
        await Db.SaveChangesAsync(ct);
        return Ok(ToDto(x));
    }

    [HttpPost("{id:guid}/pay")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Pay(Guid id, CancellationToken ct)
    {
        var x = await LoadAsync(id, ct);
        if (x is null) return NotFound();
        if (x.Status != SupplierInvoiceStatus.Approved)
            return BadRequest(new { error = $"Invoice must be approved first (is {x.Status})." });
        x.Status = SupplierInvoiceStatus.Paid;
        x.PaidUtc = DateTime.UtcNow;
        await Db.SaveChangesAsync(ct);
        return Ok(ToDto(x));
    }

    /// <summary>Registered/mismatched invoices can be removed; approved and
    /// paid ones stay as the audit trail.</summary>
    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var x = await Db.SupplierInvoices.FindAsync([id], ct);
        if (x is null) return NotFound();
        if (x.Status is SupplierInvoiceStatus.Approved or SupplierInvoiceStatus.Paid)
            return Conflict(new { error = "invoiceApproved", message = "Approved invoices can't be deleted." });
        Db.SupplierInvoices.Remove(x);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<SupplierInvoice?> LoadAsync(Guid id, CancellationToken ct) =>
        Db.SupplierInvoices.Include(x => x.Resource).Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
}
