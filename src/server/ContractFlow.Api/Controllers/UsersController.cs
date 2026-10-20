using ContractFlow.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

/// <summary>
/// Lists users — feeds the dev role-switcher, so it only exists where the
/// dev identity does (Development/Testing). Outside those it answers 404 —
/// a user list must never let an anonymous caller pick an identity. Admin
/// user management arrives with Entra sign-in.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!env.IsDevelopment() && !env.IsEnvironment("Testing")) return NotFound();
        var users = await db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName, u.Email, Role = u.Role.ToString() })
            .ToListAsync(ct);
        return Ok(users);
    }
}
