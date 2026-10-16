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
    private static EndClientDto ToDto(EndClient e) =>
        new(e.Id, e.Name, e.Country, e.AddressLine, e.PostalCode, e.City, e.Notes);

    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var clients = await db.EndClients
            .OrderBy(e => e.Name)
            .ToListAsync(ct);
        return Ok(clients.Select(ToDto));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(EndClientRequest req, CancellationToken ct)
    {
        var client = new EndClient
        {
            Name = req.Name.Trim(),
            Country = req.Country?.ToUpperInvariant(),
            AddressLine = req.AddressLine,
            PostalCode = req.PostalCode,
            City = req.City,
            Notes = req.Notes,
        };
        db.EndClients.Add(client);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(client));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, EndClientRequest req, CancellationToken ct)
    {
        var client = await db.EndClients.FindAsync([id], ct);
        if (client is null) return NotFound();

        client.Name = req.Name.Trim();
        client.Country = req.Country?.ToUpperInvariant();
        client.AddressLine = req.AddressLine;
        client.PostalCode = req.PostalCode;
        client.City = req.City;
        client.Notes = req.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(client));
    }

    /// <summary>Delete is safe — contracts referencing the end client keep working (FK SetNull).</summary>
    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var client = await db.EndClients.FindAsync([id], ct);
        if (client is null) return NotFound();

        db.EndClients.Remove(client);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
