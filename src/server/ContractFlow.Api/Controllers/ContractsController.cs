using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/contracts")]
public class ContractsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List([FromQuery] Guid? customerId, CancellationToken ct)
    {
        var query = db.Contracts.AsQueryable();
        if (customerId.HasValue)
            query = query.Where(c => c.CustomerId == customerId.Value);

        var contracts = await query
            .OrderBy(c => c.Name)
            .Select(c => new ContractSummaryDto(
                c.Id, c.CustomerId, c.Name, c.ExternalReference, c.Model,
                c.StartDate, c.EndDate, c.IsActive))
            .ToListAsync(ct);
        return Ok(contracts);
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var contract = await db.Contracts
            .Include(c => c.Customer)
            .Include(c => c.EndClient)
            .Include(c => c.TermsVersions)
            .Include(c => c.Projects)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null) return NotFound();

        return Ok(new ContractDetailDto(
            contract.Id, contract.CustomerId, contract.Customer.Name,
            contract.EndClientId, contract.EndClient?.Name,
            contract.Name, contract.ExternalReference, contract.Model,
            contract.StartDate, contract.EndDate, contract.IsActive,
            contract.TermsVersions
                .OrderByDescending(t => t.Version)
                .Select(ToDto).ToList(),
            contract.Projects
                .OrderBy(p => p.Name)
                .Select(p => new ProjectSummaryDto(p.Id, p.ContractId, p.Name, p.Code, p.IsActive))
                .ToList()));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(ContractRequest req, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == req.CustomerId, ct))
            return BadRequest(new { error = "Unknown customer." });
        if (req.EndClientId.HasValue &&
            !await db.EndClients.AnyAsync(e => e.Id == req.EndClientId.Value, ct))
            return BadRequest(new { error = "Unknown end client." });
        if (req.EndDate.HasValue && req.EndDate.Value < req.StartDate)
            return BadRequest(new { error = "End date cannot precede start date." });

        var contract = new Contract
        {
            CustomerId = req.CustomerId,
            EndClientId = req.EndClientId,
            Name = req.Name.Trim(),
            ExternalReference = req.ExternalReference,
            Model = req.Model,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            IsActive = req.IsActive,
        };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = contract.Id },
            new ContractSummaryDto(contract.Id, contract.CustomerId, contract.Name,
                contract.ExternalReference, contract.Model, contract.StartDate,
                contract.EndDate, contract.IsActive));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, ContractRequest req, CancellationToken ct)
    {
        var contract = await db.Contracts.FindAsync([id], ct);
        if (contract is null) return NotFound();
        if (!await db.Customers.AnyAsync(c => c.Id == req.CustomerId, ct))
            return BadRequest(new { error = "Unknown customer." });
        if (req.EndClientId.HasValue &&
            !await db.EndClients.AnyAsync(e => e.Id == req.EndClientId.Value, ct))
            return BadRequest(new { error = "Unknown end client." });
        if (req.EndDate.HasValue && req.EndDate.Value < req.StartDate)
            return BadRequest(new { error = "End date cannot precede start date." });

        contract.CustomerId = req.CustomerId;
        contract.EndClientId = req.EndClientId;
        contract.Name = req.Name.Trim();
        contract.ExternalReference = req.ExternalReference;
        contract.Model = req.Model;
        contract.StartDate = req.StartDate;
        contract.EndDate = req.EndDate;
        contract.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);

        return Ok(new ContractSummaryDto(contract.Id, contract.CustomerId, contract.Name,
            contract.ExternalReference, contract.Model, contract.StartDate,
            contract.EndDate, contract.IsActive));
    }

    // ── Terms versions — effective-dated, never rewritten (design §3) ────────

    [HttpGet("{id:guid}/terms")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> ListTerms(Guid id, CancellationToken ct)
    {
        if (!await db.Contracts.AnyAsync(c => c.Id == id, ct)) return NotFound();

        var terms = await db.ContractTerms
            .Where(t => t.ContractId == id)
            .OrderByDescending(t => t.Version)
            .ToListAsync(ct);
        return Ok(terms.Select(ToDto));
    }

    /// <summary>
    /// Resolves the terms version in force on a given date (yyyy-mm-dd) — the
    /// version with the latest EffectiveFrom ≤ that date. 404 when the contract
    /// has no version in force yet.
    /// </summary>
    [HttpGet("{id:guid}/terms/effective")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> EffectiveTerms(
        Guid id, [FromQuery] DateOnly? on, CancellationToken ct)
    {
        var onDate = on ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var terms = await db.ContractTerms
            .Where(t => t.ContractId == id && t.EffectiveFrom <= onDate)
            .OrderByDescending(t => t.EffectiveFrom)
            .ThenByDescending(t => t.Version)
            .FirstOrDefaultAsync(ct);
        if (terms is null) return NotFound();
        return Ok(ToDto(terms));
    }

    [HttpPost("{id:guid}/terms")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> CreateTerms(
        Guid id, ContractTermsRequest req, CancellationToken ct)
    {
        if (!await db.Contracts.AnyAsync(c => c.Id == id, ct)) return NotFound();

        var nextVersion = await db.ContractTerms
            .Where(t => t.ContractId == id)
            .Select(t => (int?)t.Version)
            .MaxAsync(ct) + 1 ?? 1;

        var terms = new ContractTerms
        {
            ContractId = id,
            Version = nextVersion,
            EffectiveFrom = req.EffectiveFrom,
            Currency = req.Currency.ToUpperInvariant(),
            PadRate = req.PadRate,
            HourlyRate = req.HourlyRate,
            SaturdayRate = req.SaturdayRate,
            SundayRate = req.SundayRate,
            HolidayRate = req.HolidayRate,
            OvertimeAfterHours = req.OvertimeAfterHours,
            OvertimePercent = req.OvertimePercent,
            MobilisationFee = req.MobilisationFee,
            ExpenseMarkupPercent = req.ExpenseMarkupPercent,
            PadHoursPerDay = req.PadHoursPerDay,
            MinPadPerBookedDay = req.MinPadPerBookedDay,
            CarryOverMaxPercent = req.CarryOverMaxPercent,
            CancellationDeadlineBusinessDays = req.CancellationDeadlineBusinessDays,
            HolidayCalendar = req.HolidayCalendar,
            TravelExpensePolicy = req.TravelExpensePolicy,
            Indexation = req.Indexation,
            Notes = req.Notes,
        };
        db.ContractTerms.Add(terms);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(terms));
    }

    private static ContractTermsDto ToDto(ContractTerms t) => new(
        t.Id, t.ContractId, t.Version, t.EffectiveFrom, t.CreatedUtc,
        t.Currency, t.PadRate, t.HourlyRate, t.SaturdayRate, t.SundayRate,
        t.HolidayRate, t.OvertimeAfterHours, t.OvertimePercent, t.MobilisationFee,
        t.ExpenseMarkupPercent, t.PadHoursPerDay, t.MinPadPerBookedDay,
        t.CarryOverMaxPercent, t.CancellationDeadlineBusinessDays,
        t.HolidayCalendar, t.TravelExpensePolicy, t.Indexation, t.Notes);
}
