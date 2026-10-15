using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Services;

/// <summary>
/// Resolves the person making the request. In Development/Testing the identity
/// comes from the X-Dev-User-Id header set by the client's role-switcher;
/// once Entra sign-in is added the same interface reads the token claims.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The resolved user, or null when the request carries no usable identity.</summary>
    Task<AppUser?> GetAsync(CancellationToken ct = default);
}
