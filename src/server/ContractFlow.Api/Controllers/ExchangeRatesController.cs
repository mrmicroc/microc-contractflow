using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Month rate table (design §11 Q4): finance enters one rate per currency pair
/// and month; invoice specifications and statements convert foreign-currency
/// rows at the rate of their own month. A rate that priced a document beyond
/// draft is history and can no longer change.
/// </summary>
[ApiController]
[Route("api/exchange-rates")]
public class ExchangeRatesController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.Finance, AppRole.ProjectManager)]
    public async Task<IActionResult> List([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var q = db.ExchangeRates.AsQueryable();
        if (year.HasValue) q = q.Where(r => r.Year == year);
        if (month.HasValue) q = q.Where(r => r.Month == month);
        var rows = await q.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
            .ThenBy(r => r.FromCurrency).ThenBy(r => r.ToCurrency).ToListAsync(ct);
        var result = new List<ExchangeRateDto>();
        foreach (var r in rows) result.Add(ToDto(r, await InUseAsync(r, ct)));
        return Ok(result);
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Create(ExchangeRateRequest req, CancellationToken ct)
    {
        var (from, to) = (req.FromCurrency.ToUpperInvariant(), req.ToCurrency.ToUpperInvariant());
        if (from == to) return BadRequest(new { error = "From and to currency must differ." });
        if (await db.ExchangeRates.AnyAsync(r => r.Year == req.Year && r.Month == req.Month
                && r.FromCurrency == from && r.ToCurrency == to, ct))
            return Conflict(new { error = "duplicate", message = "A rate for that pair and month already exists." });
        var me = await currentUser.GetAsync(ct);
        var row = new ExchangeRate
        {
            Year = req.Year, Month = req.Month, FromCurrency = from, ToCurrency = to,
            Rate = req.Rate, Note = req.Note?.Trim(), CreatedByUserId = me?.Id,
        };
        db.ExchangeRates.Add(row);
        await db.SaveChangesAsync(ct);
        return Created($"/api/exchange-rates/{row.Id}", ToDto(row, false));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Update(Guid id, ExchangeRateRequest req, CancellationToken ct)
    {
        var row = await db.ExchangeRates.FindAsync([id], ct);
        if (row is null) return NotFound();
        if (await InUseAsync(row, ct)) return InUse();
        var (from, to) = (req.FromCurrency.ToUpperInvariant(), req.ToCurrency.ToUpperInvariant());
        if (from == to) return BadRequest(new { error = "From and to currency must differ." });
        if (await db.ExchangeRates.AnyAsync(r => r.Id != id && r.Year == req.Year && r.Month == req.Month
                && r.FromCurrency == from && r.ToCurrency == to, ct))
            return Conflict(new { error = "duplicate", message = "A rate for that pair and month already exists." });
        row.Year = req.Year;
        row.Month = req.Month;
        row.FromCurrency = from;
        row.ToCurrency = to;
        row.Rate = req.Rate;
        row.Note = req.Note?.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(row, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.Finance)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var row = await db.ExchangeRates.FindAsync([id], ct);
        if (row is null) return NotFound();
        if (await InUseAsync(row, ct)) return InUse();
        db.ExchangeRates.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>True when a specification or statement of that month, beyond
    /// draft, converted a line between the two currencies (either direction).</summary>
    private async Task<bool> InUseAsync(ExchangeRate r, CancellationToken ct)
    {
        string[] pair = [r.FromCurrency, r.ToCurrency];
        var onSpec = await db.InvoiceLines.AnyAsync(l =>
            l.OriginalCurrency != null && pair.Contains(l.OriginalCurrency)
            && pair.Contains(l.Invoice.Currency)
            && l.Invoice.Year == r.Year && l.Invoice.Month == r.Month
            && l.Invoice.Status != InvoiceStatus.Draft, ct);
        if (onSpec) return true;
        return await db.StatementLines.AnyAsync(l =>
            l.OriginalCurrency != null && pair.Contains(l.OriginalCurrency)
            && pair.Contains(l.Statement.Currency)
            && l.Statement.Year == r.Year && l.Statement.Month == r.Month
            && l.Statement.Status != StatementStatus.Draft, ct);
    }

    private ConflictObjectResult InUse() => Conflict(new
    {
        error = "rateInUse",
        message = "The rate priced a document beyond draft — it is history and can't change.",
    });

    private static ExchangeRateDto ToDto(ExchangeRate r, bool inUse) => new(
        r.Id, r.Year, r.Month, r.FromCurrency, r.ToCurrency, r.Rate, r.Note, r.CreatedUtc, inUse);
}
