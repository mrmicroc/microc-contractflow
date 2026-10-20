using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Services;

/// <summary>
/// Identity outside Development/Testing until Entra sign-in lands: nobody is
/// signed in, so every role-guarded endpoint answers 401. Keeps the dev
/// header (and its first-Administrator fallback) from ever reaching a
/// deployed environment (design §9.1).
/// </summary>
public class NoSignInCurrentUser : ICurrentUser
{
    public Task<AppUser?> GetAsync(CancellationToken ct = default) => Task.FromResult<AppUser?>(null);
}
