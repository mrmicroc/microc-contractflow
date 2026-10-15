using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Services;

/// <summary>
/// Development identity: reads the X-Dev-User-Id header the dev role-switcher
/// sends; falls back to the first active Administrator so a fresh checkout
/// always lands on a working user. Production resolves Entra claims instead.
/// </summary>
public class DevCurrentUser(IHttpContextAccessor http, AppDbContext db) : ICurrentUser
{
    public const string UserIdHeader = "X-Dev-User-Id";

    public async Task<AppUser?> GetAsync(CancellationToken ct = default)
    {
        var header = http.HttpContext?.Request.Headers[UserIdHeader].FirstOrDefault();
        if (Guid.TryParse(header, out var id))
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
            if (user != null) return user;
        }

        return await db.Users
            .Where(u => u.IsActive && u.Role == AppRole.Administrator)
            .OrderBy(u => u.CreatedUtc)
            .FirstOrDefaultAsync(ct);
    }
}
