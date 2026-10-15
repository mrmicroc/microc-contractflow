using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/endclients")]
public class EndClientsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var clients = await db.EndClients
            .OrderBy(e => e.Name)
            .Select(e => new EndClientDto(e.Id, e.Name, e.Notes))
            .ToListAsync(ct);
        return Ok(clients);
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(EndClientRequest req, CancellationToken ct)
    {
        var client = new EndClient { Name = req.Name.Trim(), Notes = req.Notes };
        db.EndClients.Add(client);
        await db.SaveChangesAsync(ct);
        return Ok(new EndClientDto(client.Id, client.Name, client.Notes));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, EndClientRequest req, CancellationToken ct)
    {
        var client = await db.EndClients.FindAsync([id], ct);
        if (client is null) return NotFound();

        client.Name = req.Name.Trim();
        client.Notes = req.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(new EndClientDto(client.Id, client.Name, client.Notes));
    }
}
