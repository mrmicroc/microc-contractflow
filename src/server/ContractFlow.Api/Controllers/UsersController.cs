using ContractFlow.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Lists users — feeds the dev role-switcher and, later, admin user management.
/// Access control arrives with Entra sign-in in Phase 2.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var users = await db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName, u.Email, Role = u.Role.ToString() })
            .ToListAsync(ct);
        return Ok(users);
    }
}
