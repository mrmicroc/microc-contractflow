using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(AppDbContext db) : ControllerBase
{
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
            .Include(p => p.Locations)
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

    private static ProjectDetailDto ToDto(Project p) => new(
        p.Id, p.ContractId, p.Contract.Name, p.Contract.Customer.Name,
        p.Name, p.Code, p.Description, p.IsActive,
        p.Locations.OrderBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.ProjectId, l.Name, l.Address, l.Notes))
            .ToList(),
        p.EstimatedQuantities
            .Select(q => new EstimatedQuantityDto(
                q.Id, q.ProjectId, q.Label, q.Quantity, q.Unit, q.Notes))
            .ToList());
}
