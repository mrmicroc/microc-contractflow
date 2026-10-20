using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Subcontractor statements (design §3, §6.4): the payable basis per
/// resource × month, generated from their approved timesheets. draft → final
/// → paid. A resource reads only its own statements (§10: nothing
/// customer-facing appears here — quantities are payable, never billable).
/// Statements expose agreement rates, so Project Managers — who never see
/// subcontractor rates (§2.1/§10) — have no access.
/// </summary>
[ApiController]
[Route("api/statements")]
public class StatementsController(AppDbContext db, ICurrentUser currentUser, BillingService billing)
    : ReportingControllerBase(db, currentUser)
{
    public record StatementDto(
        Guid Id, Guid ResourceId, string ResourceName, int Year, int Month,
        StatementStatus Status, string Currency, decimal TotalAmount,
        int LineCount, DateTime CreatedUtc, DateTime? PaidUtc, Guid? PaidByUserId,
        List<string> Warnings);

    public record StatementDetailDto(StatementDto Statement, List<LineDto> Lines);
    public record LineDto(
        Guid Id, StatementLineKind Kind, DateOnly? Date, string Description,
        string? Explanation, decimal Quantity, decimal UnitPrice, decimal Amount);

    public record GenerateRequest(Guid ResourceId, int Year, int Month);

    private static StatementDto ToDto(SubcontractorStatement s) => new(
        s.Id, s.ResourceId, s.Resource.DisplayName, s.Year, s.Month, s.Status,
        s.Currency, s.TotalAmount, s.Lines.Count, s.CreatedUtc, s.PaidUtc, s.PaidByUserId,
        BillingService.Warnings(s.WarningsJson));

    private static LineDto ToDto(StatementLine l) => new(
        l.Id, l.Kind, l.Date, l.Description, l.Explanation,
        l.Quantity, l.UnitPrice, l.Amount);

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? resourceId, [FromQuery] int? year, [FromQuery] int? month,
        [FromQuery] StatementStatus? status, CancellationToken ct)
    {
        var scoped = await ScopedResourceIdAsync(ct);
        var q = Db.SubcontractorStatements.Include(s => s.Resource).Include(s => s.Lines).AsQueryable();
        if (scoped.HasValue) q = q.Where(s => s.ResourceId == scoped.Value);
        else if (resourceId.HasValue) q = q.Where(s => s.ResourceId == resourceId);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (status.HasValue) q = q.Where(s => s.Status == status);
        var rows = await q.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var s = await LoadAsync(id, ct);
        if (s is null) return NotFound();
        var scoped = await ScopedResourceIdAsync(ct);
        if (scoped.HasValue && scoped.Value != s.ResourceId) return ForbidResult();
        return Ok(new StatementDetailDto(ToDto(s), s.Lines.OrderBy(l => l.Date).Select(ToDto).ToList()));
    }

    [HttpPost("generate")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Generate(GenerateRequest req, CancellationToken ct)
    {
        if (req.Month is < 1 or > 12 || req.Year is < 2000 or > 2100)
            return BadRequest(new { error = "Invalid year/month." });
        var me = await Me(ct);
        try
        {
            var s = await billing.GenerateStatementAsync(req.ResourceId, req.Year, req.Month, me?.Id, ct);
            return Ok(ToDto((await LoadAsync(s.Id, ct))!));
        }
        catch (BillingException ex)
        {
            return Conflict(new { error = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/finalize")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public Task<IActionResult> Finalize(Guid id, CancellationToken ct) =>
        Transition(id, StatementStatus.Draft, StatementStatus.Final, ct);

    [HttpPost("{id:guid}/pay")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Pay(Guid id, CancellationToken ct)
    {
        var me = await Me(ct);
        return await Transition(id, StatementStatus.Final, StatementStatus.Paid, ct, s =>
        {
            s.PaidUtc = DateTime.UtcNow;
            s.PaidByUserId = me?.Id;
        });
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var s = await Db.SubcontractorStatements.FindAsync([id], ct);
        if (s is null) return NotFound();
        if (s.Status != StatementStatus.Draft)
            return Conflict(new
            {
                error = "statementFinal",
                message = "Only draft statements can be deleted.",
            });
        Db.SubcontractorStatements.Remove(s);
        await Db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<IActionResult> Transition(
        Guid id, StatementStatus from, StatementStatus to, CancellationToken ct,
        Action<SubcontractorStatement>? apply = null)
    {
        var s = await LoadAsync(id, ct);
        if (s is null) return NotFound();
        if (s.Status != from)
            return BadRequest(new { error = $"Statement must be {from} first (is {s.Status})." });
        s.Status = to;
        apply?.Invoke(s);
        await Db.SaveChangesAsync(ct);
        return Ok(ToDto(s));
    }

    private Task<SubcontractorStatement?> LoadAsync(Guid id, CancellationToken ct) =>
        Db.SubcontractorStatements
            .Include(s => s.Resource).Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
}
