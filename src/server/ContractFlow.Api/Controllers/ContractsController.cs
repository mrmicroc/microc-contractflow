using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/contracts")]
public class ContractsController(AppDbContext db, BillingService billing) : ControllerBase
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List([FromQuery] Guid? customerId, CancellationToken ct)
    {
        var query = db.Contracts.AsQueryable();
        if (customerId.HasValue)
            query = query.Where(c => c.CustomerId == customerId.Value);

        var contracts = await query
            .Include(c => c.TermsVersions).Include(c => c.ScheduledCharges)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(contracts.Select(c => new ContractSummaryDto(
            c.Id, c.CustomerId, c.Name, c.ExternalReference,
            ComponentsOf(c, today),
            c.StartDate, c.EndDate, c.IsActive)));
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var contract = await db.Contracts
            .Include(c => c.Customer)
            .Include(c => c.EndClient)
            .Include(c => c.TermsVersions).Include(c => c.ScheduledCharges)
            .Include(c => c.Projects)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null) return NotFound();

        return Ok(new ContractDetailDto(
            contract.Id, contract.CustomerId, contract.Customer.Name,
            contract.EndClientId, contract.EndClient?.Name, contract.EndClientReference,
            contract.Name, contract.ExternalReference,
            ComponentsOf(contract, DateOnly.FromDateTime(DateTime.UtcNow)),
            contract.StartDate, contract.EndDate, contract.IsActive,
            contract.TermsVersions
                .OrderByDescending(t => t.Version)
                .Select(ToDto).ToList(),
            contract.Projects
                .OrderBy(p => p.Name)
                .Select(p => new ProjectSummaryDto(p.Id, p.ContractId, p.Name, p.Code, p.IsActive))
                .ToList(),
            contract.Representatives
                .Select(r => new AuthorisedRepresentativeDto(r.Name, r.Email, r.Phone, r.Authority))
                .ToList()));
    }

    /// <summary>All work packages across the contract's projects — the scope
    /// picker's options (e.g. agreement scoping), labelled by project.</summary>
    [HttpGet("{id:guid}/work-packages")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> ListWorkPackages(Guid id, CancellationToken ct)
    {
        if (!await db.Contracts.AnyAsync(c => c.Id == id, ct)) return NotFound();
        var packages = await db.WorkPackages
            .Where(w => w.Project.ContractId == id)
            .OrderBy(w => w.Project.Name).ThenBy(w => w.Name)
            .Select(w => new ContractWorkPackageDto(w.Id, w.Name, w.ProjectId, w.Project.Name))
            .ToListAsync(ct);
        return Ok(packages);
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(ContractRequest req, CancellationToken ct)
    {
        if (await ValidatePartiesAsync(req, ct) is { } invalid) return invalid;

        var contract = new Contract
        {
            CustomerId = req.CustomerId,
            EndClientId = EndClientOf(req),
            EndClientReference = EndClientOf(req) is null ? null : req.EndClientReference,
            Name = req.Name.Trim(),
            ExternalReference = req.ExternalReference,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            IsActive = req.IsActive,
            Representatives = Representatives(req),
        };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = contract.Id },
            new ContractSummaryDto(contract.Id, contract.CustomerId, contract.Name,
                contract.ExternalReference, [], contract.StartDate,
                contract.EndDate, contract.IsActive));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, ContractRequest req, CancellationToken ct)
    {
        var contract = await db.Contracts.FindAsync([id], ct);
        if (contract is null) return NotFound();
        if (await ValidatePartiesAsync(req, ct) is { } invalid) return invalid;

        contract.CustomerId = req.CustomerId;
        contract.EndClientId = EndClientOf(req);
        contract.EndClientReference = EndClientOf(req) is null ? null : req.EndClientReference;
        contract.Name = req.Name.Trim();
        contract.ExternalReference = req.ExternalReference;
        contract.StartDate = req.StartDate;
        contract.EndDate = req.EndDate;
        contract.IsActive = req.IsActive;
        contract.Representatives = Representatives(req);
        await db.SaveChangesAsync(ct);

        var fresh = await db.Contracts.Include(c => c.TermsVersions).Include(c => c.ScheduledCharges)
            .FirstAsync(c => c.Id == id, ct);
        return Ok(new ContractSummaryDto(fresh.Id, fresh.CustomerId, fresh.Name,
            fresh.ExternalReference,
            ComponentsOf(fresh, DateOnly.FromDateTime(DateTime.UtcNow)),
            fresh.StartDate, fresh.EndDate, fresh.IsActive));
    }

    /// <summary>
    /// Deletes the contract and cascades its terms/projects/workPackages/quantities
    /// and PAD ledger rows. Blocked while bookings (audit anchors) or
    /// compensation agreements reference it — deactivate instead.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var contract = await db.Contracts.FindAsync([id], ct);
        if (contract is null) return NotFound();
        if (await db.Bookings.AnyAsync(b => b.Project.ContractId == id, ct))
            return Conflict(new
            {
                error = "hasDependents",
                message = "Contract has bookings — they anchor the PAD audit trail and cannot be deleted.",
            });
        if (await db.CompensationAgreements.AnyAsync(a => a.ContractId == id, ct))
            return Conflict(new
            {
                error = "hasDependents",
                message = "Contract is referenced by compensation agreements — remove them first.",
            });

        db.Contracts.Remove(contract);
        await db.SaveChangesAsync(ct);
        return NoContent();
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
        if (!BusinessDays.SupportedCalendars.ContainsKey(req.HolidayCalendar.ToUpperInvariant()))
            return BadRequest(new { error = "Unsupported holiday calendar." });
        if (req.Currency.Length != 3 || !req.Currency.All(char.IsAsciiLetter))
            return BadRequest(new { error = "Currency must be a three-letter code." });
        if (req.CancellationSteps.Any(s => s.ChargeDayFraction is < 0m or > 1m))
            return BadRequest(new { error = "A cancellation day fraction must lie between 0 and 1." });
        if (req.ExpenseMarkupError() is { } markupError)
            return BadRequest(new { error = markupError });
        // Effective-dated, never rewritten (§3): a version may not reach back
        // into a month a non-draft specification already billed.
        if (await billing.TermsChangeBlockedAsync(id, req.EffectiveFrom, null, ct) is { } why)
            return Conflict(new { error = "billedHistory", message = why });

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
            IncludedVisits = req.IncludedVisits,
            ExpenseMarkupPercent = req.ExpenseMarkupPercent,
            TimeBilling = req.TimeBilling,
            PadEnabled = req.PadEnabled,
            DayRate = req.DayRate,
            SaturdayDayRate = req.SaturdayDayRate,
            SundayDayRate = req.SundayDayRate,
            HolidayDayRate = req.HolidayDayRate,
            HalfDayRate = req.HalfDayRate,
            HalfDayMaxHours = req.HalfDayMaxHours,
            PadHoursPerDay = req.PadHoursPerDay,
            CarryOverMonths = req.CarryOverMonths,
            CancellationSteps = req.CancellationSteps
                .Select(s => new CancellationStep
                {
                    MinBusinessDaysBefore = s.MinBusinessDaysBefore,
                    ChargeHours = s.ChargeHours,
                    ChargeAmount = s.ChargeAmount,
                    ChargeDayFraction = s.ChargeDayFraction,
                })
                .OrderByDescending(s => s.MinBusinessDaysBefore)
                .ToList(),
            MinPadPerBookedDay = req.MinPadPerBookedDay,
            CarryOverMaxPercent = req.CarryOverMaxPercent,
            CancellationDeadlineBusinessDays = req.CancellationDeadlineBusinessDays,
            HolidayCalendar = req.HolidayCalendar.ToUpperInvariant(),
            MinBillableHoursPerDay = req.MinBillableHoursPerDay,
            TravelExpensePolicy = req.TravelExpensePolicy,
            Indexation = req.Indexation,
            Notes = req.Notes,
        };
        req.ApplyPadRules(terms);
        req.ApplyExpenseMarkups(terms);
        db.ContractTerms.Add(terms);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(terms));
    }

    /// <summary>Removes a single terms version (e.g. entered by mistake).</summary>
    [HttpDelete("{id:guid}/terms/{termId:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> DeleteTerms(Guid id, Guid termId, CancellationToken ct)
    {
        var terms = await db.ContractTerms
            .FirstOrDefaultAsync(t => t.Id == termId && t.ContractId == id, ct);
        if (terms is null) return NotFound();
        if (await billing.TermsChangeBlockedAsync(id, terms.EffectiveFrom, terms.Id, ct) is { } why)
            return Conflict(new { error = "billedHistory", message = why });

        db.ContractTerms.Remove(terms);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Party and end client both come from the organisation
    /// register; an end client equal to the party means a direct contract.</summary>
    private async Task<IActionResult?> ValidatePartiesAsync(ContractRequest req, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == req.CustomerId, ct))
            return BadRequest(new { error = "Unknown customer." });
        if (EndClientOf(req) is { } endClientId &&
            !await db.Customers.AnyAsync(c => c.Id == endClientId, ct))
            return BadRequest(new { error = "Unknown end client." });
        if (req.EndDate.HasValue && req.EndDate.Value < req.StartDate)
            return BadRequest(new { error = "End date cannot precede start date." });
        return null;
    }

    /// <summary>Stored end client — null on a direct contract (none given, or
    /// the party itself picked).</summary>
    private static List<AuthorisedRepresentative> Representatives(ContractRequest req) =>
        req.Representatives
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new AuthorisedRepresentative
            {
                Name = r.Name.Trim(), Email = r.Email, Phone = r.Phone, Authority = r.Authority,
            })
            .ToList();

    private static Guid? EndClientOf(ContractRequest req) =>
        req.EndClientId == req.CustomerId ? null : req.EndClientId;

    /// <summary>Billing-component keys active on the terms version in force on
    /// <paramref name="date"/> — "hourly" / "dailyRate" / "pad", plus "scheduledCharges"
    /// when the contract carries one-off dated amounts (needs ScheduledCharges loaded).
    /// Empty when no version is in force yet.</summary>
    public static List<string> ComponentsOf(Contract contract, DateOnly date)
    {
        var t = contract.TermsVersions
            .Where(x => x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version)
            .FirstOrDefault();
        if (t is null) return [];
        var list = new List<string>();
        if (t.TimeBilling == TimeBilling.Hourly) list.Add("hourly");
        if (t.TimeBilling == TimeBilling.DailyRate) list.Add("dailyRate");
        if (t.PadEnabled) list.Add("pad");
        if (contract.ScheduledCharges.Count > 0) list.Add("scheduledCharges");
        return list;
    }

    private static ContractTermsDto ToDto(ContractTerms t) => new(
        t.Id, t.ContractId, t.Version, t.EffectiveFrom, t.CreatedUtc,
        t.Currency, t.TimeBilling, t.PadEnabled,
        t.PadRate, t.HourlyRate, t.SaturdayRate, t.SundayRate,
        t.HolidayRate, t.OvertimeAfterHours, t.OvertimePercent, t.MobilisationFee,
        t.ExpenseMarkupPercent,
        t.DayRate, t.SaturdayDayRate, t.SundayDayRate, t.HolidayDayRate,
        t.HalfDayRate, t.HalfDayMaxHours,
        t.PadHoursPerDay, t.MinPadPerBookedDay,
        t.CarryOverMaxPercent, t.CarryOverMonths,
        t.CancellationDeadlineBusinessDays,
        t.CancellationSteps
            .Select(s => new CancellationStepDto(
                s.MinBusinessDaysBefore, s.ChargeHours, s.ChargeAmount, s.ChargeDayFraction))
            .ToList(),
        t.HolidayCalendar, t.MinBillableHoursPerDay,
        t.TravelExpensePolicy, t.Indexation, t.Notes, t.IncludedVisits,
        PadRulesDto.From(t),
        t.ExpenseMarkups.Select(m => new ExpenseMarkupDto(m.Category, m.Percent)).ToList());
}
