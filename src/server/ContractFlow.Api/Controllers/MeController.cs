using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/me")]
public class MeController(ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        if (user == null) return Unauthorized();

        return Ok(new
        {
            user.Id,
            user.DisplayName,
            user.Email,
            Role = user.Role.ToString(),
        });
    }
}
