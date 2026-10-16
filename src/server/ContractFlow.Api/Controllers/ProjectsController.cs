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
            return Ok(await db.Projects.Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => new ProjectSummaryDto(p.Id, p.ContractId, p.Name, p.Code, p.IsActive))
                .ToListAsync(ct));

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

        return Ok(await db.Projects
            .Where(p => ids.Contains(p.Id) && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new ProjectSummaryDto(p.Id, p.ContractId, p.Name, p.Code, p.IsActive))
            .ToListAsync(ct));
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

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Contract).ThenInclude(c => c.Customer)
            .Include(p => p.WorkPackages)
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

    private static ProjectDetailDto ToDto(Project p) => new(
        p.Id, p.ContractId, p.Contract.Name, p.Contract.Customer.Name,
        p.Name, p.Code, p.Description, p.IsActive,
        p.WorkPackages.OrderBy(l => l.Name)
            .Select(l => new WorkPackageDto(l.Id, l.ProjectId, l.Name, l.Address, l.Notes))
            .ToList(),
        p.EstimatedQuantities
            .Select(q => new EstimatedQuantityDto(
                q.Id, q.ProjectId, q.Label, q.Quantity, q.Unit, q.Notes))
            .ToList());
}
