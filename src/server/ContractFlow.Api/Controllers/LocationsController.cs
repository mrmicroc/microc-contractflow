using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List([FromQuery] Guid? projectId, CancellationToken ct)
    {
        var query = db.Locations.AsQueryable();
        if (projectId.HasValue)
            query = query.Where(l => l.ProjectId == projectId.Value);

        var locations = await query
            .OrderBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.ProjectId, l.Name, l.Address, l.Notes))
            .ToListAsync(ct);
        return Ok(locations);
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(LocationRequest req, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });

        var location = new Location
        {
            ProjectId = req.ProjectId,
            Name = req.Name.Trim(),
            Address = req.Address,
            Notes = req.Notes,
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        return Ok(new LocationDto(location.Id, location.ProjectId, location.Name,
            location.Address, location.Notes));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, LocationRequest req, CancellationToken ct)
    {
        var location = await db.Locations.FindAsync([id], ct);
        if (location is null) return NotFound();
        if (!await db.Projects.AnyAsync(p => p.Id == req.ProjectId, ct))
            return BadRequest(new { error = "Unknown project." });

        location.ProjectId = req.ProjectId;
        location.Name = req.Name.Trim();
        location.Address = req.Address;
        location.Notes = req.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(new LocationDto(location.Id, location.ProjectId, location.Name,
            location.Address, location.Notes));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var location = await db.Locations.FindAsync([id], ct);
        if (location is null) return NotFound();

        db.Locations.Remove(location);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
