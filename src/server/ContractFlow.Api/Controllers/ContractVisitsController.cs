using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// The contract's visit counter: confirmed mobilisations of one resource to
/// packages marked as visits, numbered chronologically against the included
/// limit in the terms — within it travel is included, beyond it travel bills.
/// </summary>
[ApiController]
[Route("api/contracts/{contractId:guid}/visits")]
public class ContractVisitsController(AppDbContext db) : ControllerBase
{
    public record VisitDto(
        Guid PeriodId, int Number, bool WithinLimit, Guid ProjectId, string ProjectName,
        Guid ResourceId, string ResourceName, Guid WorkPackageId, string WorkPackageName,
        DateOnly StartDate, DateOnly EndDate);

    /// <summary><c>Included</c> = the limit in force today (null = unlimited).</summary>
    public record VisitCounterDto(int Used, int? Included, int? Remaining, List<VisitDto> Visits);

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Get(Guid contractId, CancellationToken ct)
    {
        if (!await db.Contracts.AnyAsync(c => c.Id == contractId, ct)) return NotFound();
        var numbering = await WorkPackageRules.NumberVisitsAsync(db, contractId, ct);
        var ids = numbering.Keys.ToList();
        var periods = await db.MobilisationPeriods
            .Include(p => p.Project).Include(p => p.Resource).Include(p => p.WorkPackage)
            .Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var visits = periods
            .Select(p => (p, v: numbering[p.Id]))
            .OrderBy(x => x.v.Number)
            .Select(x => new VisitDto(
                x.p.Id, x.v.Number, x.v.WithinLimit, x.p.ProjectId, x.p.Project.Name,
                x.p.ResourceId, x.p.Resource.DisplayName, x.p.WorkPackageId!.Value, x.p.WorkPackage!.Name,
                x.p.StartDate, x.p.EndDate))
            .ToList();

        var terms = await db.ContractTerms.Where(t => t.ContractId == contractId).ToListAsync(ct);
        var included = DayBilling.TermsAt(terms, contractId, DateOnly.FromDateTime(DateTime.UtcNow))?.IncludedVisits
            ?? terms.OrderByDescending(t => t.EffectiveFrom).FirstOrDefault()?.IncludedVisits;
        return Ok(new VisitCounterDto(visits.Count, included,
            included is { } n ? Math.Max(0, n - visits.Count) : null, visits));
    }
}
