using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Projects the caller may report on — for a Resource: projects
    /// they hold access to or an active booking on; admin/PM get all active
    /// projects (they pick the target resource separately).</summary>
    [HttpGet("mine")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var me = await currentUser.GetAsync(ct);
        if (me!.Role != AppRole.Resource)
            return Ok(await WithCurrencyAsync(await db.Projects.Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => new ProjectSummaryDto(p.Id, p.ContractId, p.Name, p.Code, p.IsActive, null))
                .ToListAsync(ct), ct));

        var resourceId = await db.Resources
            .Where(r => r.UserId == me.Id).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (resourceId is null) return Ok(Array.Empty<ProjectSummaryDto>());

        var accessible = await db.ProjectAccesses
            .Where(a => a.ResourceId == resourceId).Select(a => a.ProjectId).ToListAsync(ct);
        var booked = await db.Bookings
            .Where(b => b.ResourceId == resourceId
                && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Rescheduled)
            .Select(b => b.ProjectId).Distinct().ToListAsync(ct);
        var ids = accessible.Concat(booked).Distinct().ToList();

        return Ok(await WithCurrencyAsync(await db.Projects
            .Where(p => ids.Contains(p.Id) && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new ProjectSummaryDto(p.Id, p.ContractId, p.Name, p.Code, p.IsActive, null))
            .ToListAsync(ct), ct));
    }

    /// <summary>Adds the currency of each project's contract terms in force
    /// today (latest version if none has started yet).</summary>
    private async Task<List<ProjectSummaryDto>> WithCurrencyAsync(
        List<ProjectSummaryDto> projects, CancellationToken ct)
    {
        var contractIds = projects.Select(p => p.ContractId).Distinct().ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var terms = await db.ContractTerms.Where(t => contractIds.Contains(t.ContractId))
            .Select(t => new { t.ContractId, t.EffectiveFrom, t.Version, t.Currency })
            .ToListAsync(ct);
        var currency = terms.GroupBy(t => t.ContractId).ToDictionary(g => g.Key, g =>
            (g.Where(t => t.EffectiveFrom <= today).OrderByDescending(t => t.EffectiveFrom)
                 .ThenByDescending(t => t.Version).FirstOrDefault()
             ?? g.OrderByDescending(t => t.Version).First()).Currency);
        return projects.Select(p => p with { Currency = currency.GetValueOrDefault(p.ContractId) }).ToList();
    }

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List([FromQuery] Guid? contractId, CancellationToken ct)
    {
        var query = db.Projects.AsQueryable();
        if (contractId.HasValue)
            query = query.Where(p => p.ContractId == contractId.Value);

        var projects = await query
            .OrderBy(p => p.Name)
            .Select(p => new ProjectSummaryDto(p.Id, p.ContractId, p.Name, p.Code, p.IsActive))
            .ToListAsync(ct);
        return Ok(projects);
    }

    /// <summary>Work packages of a project — also readable by a Resource who
    /// may report on it (access-listed or holding a non-terminal booking), so
    /// the week report can offer the package picker on unbooked rows.</summary>
    [HttpGet("{id:guid}/work-packages")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance, AppRole.Resource)]
    public async Task<IActionResult> ListWorkPackages(Guid id, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return NotFound();
        var me = await currentUser.GetAsync(ct);
        if (me!.Role == AppRole.Resource)
        {
            var resourceId = await db.Resources
                .Where(r => r.UserId == me.Id).Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(ct);
            if (resourceId is null) return Forbidden();
            var gated = await db.ProjectAccesses.AnyAsync(a => a.ProjectId == id, ct);
            var allowed = !gated
                || await db.ProjectAccesses.AnyAsync(
                    a => a.ProjectId == id && a.ResourceId == resourceId, ct)
                || await db.Bookings.AnyAsync(b => b.ProjectId == id
                    && b.ResourceId == resourceId
                    && b.Status != BookingStatus.Cancelled
                    && b.Status != BookingStatus.Rescheduled, ct);
            if (!allowed) return Forbidden();
        }
        return Ok(await db.WorkPackages.Where(w => w.ProjectId == id)
            .Include(w => w.InfoItems)
            .OrderBy(w => w.Name)
            .Select(w => new WorkPackageDto(w.Id, w.ProjectId, w.Name, w.Notes,
                w.InfoItems.Select(i => new WorkPackageInfoDto(i.Id, i.Type, i.Description)).ToList(),
                w.PricingMode, w.CountsAsVisit))
            .ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Contract).ThenInclude(c => c.Customer)
            .Include(p => p.WorkPackages).ThenInclude(w => w.InfoItems)
            .Include(p => p.EstimatedQuantities)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();

        return Ok(ToDto(project));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(ProjectRequest req, CancellationToken ct)
    {
        if (!await db.Contracts.AnyAsync(c => c.Id == req.ContractId, ct))
            return BadRequest(new { error = "Unknown contract." });

        var project = new Project
        {
            ContractId = req.ContractId,
            Name = req.Name.Trim(),
            Code = req.Code,
            Description = req.Description,
            ContractedBaselinePad = req.ContractedBaselinePad,
            IsActive = req.IsActive,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = project.Id },
            new ProjectSummaryDto(project.Id, project.ContractId, project.Name,
                project.Code, project.IsActive));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, ProjectRequest req, CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([id], ct);
        if (project is null) return NotFound();
        if (!await db.Contracts.AnyAsync(c => c.Id == req.ContractId, ct))
            return BadRequest(new { error = "Unknown contract." });

        project.ContractId = req.ContractId;
        project.Name = req.Name.Trim();
        project.Code = req.Code;
        project.Description = req.Description;
        project.ContractedBaselinePad = req.ContractedBaselinePad;
        project.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);

        return Ok(new ProjectSummaryDto(project.Id, project.ContractId, project.Name,
            project.Code, project.IsActive));
    }

    /// <summary>
    /// Deletes the project, cascading workPackages/quantities and its PAD ledger.
    /// Blocked while bookings exist — they anchor the audit trail; deactivate instead.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([id], ct);
        if (project is null) return NotFound();
        var hasBookings = await db.Bookings.AnyAsync(b => b.ProjectId == id, ct);
        var hasReporting = await db.TimeEntries.AnyAsync(x => x.ProjectId == id, ct)
            || await db.TravelRecords.AnyAsync(x => x.ProjectId == id, ct)
            || await db.Expenses.AnyAsync(x => x.ProjectId == id, ct)
            || await db.AllowanceClaims.AnyAsync(x => x.ProjectId == id, ct)
            || await db.MobilisationPeriods.AnyAsync(x => x.ProjectId == id, ct);
        if (hasBookings || hasReporting)
            return Conflict(new
            {
                error = "hasDependents",
                message = "Project has bookings or reported time — they anchor the audit trail and cannot be deleted.",
            });
        if (await db.ScheduledCharges.AnyAsync(c => c.ProjectId == id, ct))
            return Conflict(new
            {
                error = "hasDependents",
                message = "Scheduled charges bill on this project — move or delete them first.",
            });

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Self-reporting access list ─────────────────────────────────────────

    /// <summary>Resources allowed to self-report on this project. An empty
    /// list means every resource may report (unchanged legacy behaviour).</summary>
    [HttpGet("{id:guid}/access")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> ListAccess(Guid id, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return NotFound();
        var rows = await db.ProjectAccesses
            .Where(a => a.ProjectId == id).Include(a => a.Resource)
            .OrderBy(a => a.Resource.DisplayName).ToListAsync(ct);
        return Ok(rows.Select(a =>
            new ProjectAccessDto(a.ResourceId, a.Resource.DisplayName, a.CreatedUtc)));
    }

    /// <summary>Grants a resource self-reporting access (idempotent).</summary>
    [HttpPost("{id:guid}/access")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> GrantAccess(Guid id, ProjectAccessRequest req, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (!await db.Resources.AnyAsync(r => r.Id == req.ResourceId && r.IsActive, ct))
            return BadRequest(new { error = "Unknown or inactive resource." });
        if (!await db.ProjectAccesses.AnyAsync(
                a => a.ProjectId == id && a.ResourceId == req.ResourceId, ct))
            db.ProjectAccesses.Add(new ProjectAccess { ProjectId = id, ResourceId = req.ResourceId });
        await db.SaveChangesAsync(ct);
        var row = await db.ProjectAccesses.Include(a => a.Resource)
            .FirstAsync(a => a.ProjectId == id && a.ResourceId == req.ResourceId, ct);
        return Ok(new ProjectAccessDto(row.ResourceId, row.Resource.DisplayName, row.CreatedUtc));
    }

    [HttpDelete("{id:guid}/access/{resourceId:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> RevokeAccess(Guid id, Guid resourceId, CancellationToken ct)
    {
        var row = await db.ProjectAccesses
            .FirstOrDefaultAsync(a => a.ProjectId == id && a.ResourceId == resourceId, ct);
        if (row is null) return NotFound();
        db.ProjectAccesses.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Project resource roster ─────────────────────────────────────────────

    /// <summary>Resources staffed on this project — the commercial roster that
    /// feeds rate-matrix selectors, with per-resource pending review counts.</summary>
    [HttpGet("{id:guid}/resources")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> ListResources(Guid id, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return NotFound();
        var members = await db.ProjectResources.Include(m => m.Resource)
            .Where(m => m.ProjectId == id)
            .OrderBy(m => m.Resource.DisplayName).ToListAsync(ct);
        var pending = await db.WorkPackageRates
            .Where(r => r.WorkPackage.ProjectId == id && r.NeedsReview)
            .GroupBy(r => r.ResourceId)
            .Select(g => new { ResourceId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var pendingOf = pending.ToDictionary(x => x.ResourceId, x => x.Count);
        return Ok(members.Select(m => new ProjectResourceDto(
            m.ResourceId, m.Resource.DisplayName, m.Resource.Type.ToString(),
            m.CreatedUtc, pendingOf.GetValueOrDefault(m.ResourceId))));
    }

    /// <summary>
    /// Adds a resource to the project roster. For every work package this
    /// snapshots the currently effective rates (latest package-wide override
    /// merged over the latest contract terms) into a resource-specific matrix
    /// cell marked NeedsReview — an admin confirms each one before the
    /// inherited values are trusted.
    /// </summary>
    [HttpPost("{id:guid}/resources")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> AddResource(Guid id, ProjectResourceRequest req, CancellationToken ct)
    {
        var project = await db.Projects.Include(p => p.WorkPackages)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();
        if (!await db.Resources.AnyAsync(r => r.Id == req.ResourceId && r.IsActive, ct))
            return BadRequest(new { error = "Unknown or inactive resource." });
        if (await db.ProjectResources.AnyAsync(
                m => m.ProjectId == id && m.ResourceId == req.ResourceId, ct))
            return Conflict(new { error = "Resource is already on this project." });

        db.ProjectResources.Add(new ProjectResource
        {
            ProjectId = id, ResourceId = req.ResourceId,
        });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var terms = await db.ContractTerms
            .Where(t => t.ContractId == project.ContractId && t.EffectiveFrom <= today)
            .OrderByDescending(t => t.EffectiveFrom).ThenByDescending(t => t.Version)
            .FirstOrDefaultAsync(ct)
            ?? await db.ContractTerms.Where(t => t.ContractId == project.ContractId)
            .OrderByDescending(t => t.EffectiveFrom).ThenByDescending(t => t.Version)
            .FirstOrDefaultAsync(ct);
        var generic = await db.WorkPackageRates
            .Where(r => r.WorkPackage.ProjectId == id && r.ResourceId == null
                && r.EffectiveFrom <= today)
            .ToListAsync(ct);
        var genOf = generic.GroupBy(r => r.WorkPackageId)
            .ToDictionary(g => g.Key,
                g => g.OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.CreatedUtc).First());

        foreach (var wp in project.WorkPackages)
        {
            genOf.TryGetValue(wp.Id, out var gen);
            db.WorkPackageRates.Add(new WorkPackageRate
            {
                WorkPackageId = wp.Id,
                ResourceId = req.ResourceId,
                NeedsReview = true,
                EffectiveFrom = today,
                HourlyRate = gen?.HourlyRate ?? terms?.HourlyRate,
                SaturdayRate = gen?.SaturdayRate ?? terms?.SaturdayRate,
                SundayRate = gen?.SundayRate ?? terms?.SundayRate,
                HolidayRate = gen?.HolidayRate ?? terms?.HolidayRate,
                DayRate = gen?.DayRate ?? terms?.DayRate,
                SaturdayDayRate = gen?.SaturdayDayRate ?? terms?.SaturdayDayRate,
                SundayDayRate = gen?.SundayDayRate ?? terms?.SundayDayRate,
                HolidayDayRate = gen?.HolidayDayRate ?? terms?.HolidayDayRate,
                HalfDayRate = gen?.HalfDayRate ?? terms?.HalfDayRate,
                HalfDayMaxHours = gen?.HalfDayMaxHours ?? terms?.HalfDayMaxHours,
                MobilisationFee = gen?.MobilisationFee ?? terms?.MobilisationFee,
                Notes = "Inherited from contract on adding resource — needs review",
            });
        }
        await db.SaveChangesAsync(ct);

        var member = await db.ProjectResources.Include(m => m.Resource)
            .FirstAsync(m => m.ProjectId == id && m.ResourceId == req.ResourceId, ct);
        return Ok(new ProjectResourceDto(member.ResourceId, member.Resource.DisplayName,
            member.Resource.Type.ToString(), member.CreatedUtc, project.WorkPackages.Count));
    }

    /// <summary>Removes a resource from the roster. Rate rows stay — they are
    /// billing history — but the resource leaves the selectors.</summary>
    [HttpDelete("{id:guid}/resources/{resourceId:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager)]
    public async Task<IActionResult> RemoveResource(Guid id, Guid resourceId, CancellationToken ct)
    {
        var row = await db.ProjectResources
            .FirstOrDefaultAsync(m => m.ProjectId == id && m.ResourceId == resourceId, ct);
        if (row is null) return NotFound();
        db.ProjectResources.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>403 body — <c>Forbid()</c> needs an authentication scheme,
    /// which the dev identity doesn't register (it would throw → 500).</summary>
    private ObjectResult Forbidden() =>
        new(new { error = "Forbidden." }) { StatusCode = StatusCodes.Status403Forbidden };

    private static ProjectDetailDto ToDto(Project p) => new(
        p.Id, p.ContractId, p.Contract.Name, p.Contract.Customer.Name,
        p.Name, p.Code, p.Description, p.IsActive,
        p.WorkPackages.OrderBy(l => l.Name)
            .Select(l => new WorkPackageDto(l.Id, l.ProjectId, l.Name, l.Notes,
                l.InfoItems.Select(i => new WorkPackageInfoDto(i.Id, i.Type, i.Description)).ToList(),
                l.PricingMode, l.CountsAsVisit))
            .ToList(),
        p.EstimatedQuantities
            .Select(q => new EstimatedQuantityDto(
                q.Id, q.ProjectId, q.Label, q.Quantity, q.Unit, q.Notes))
            .ToList(),
        p.ContractedBaselinePad);
}
