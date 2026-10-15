using ContractFlow.Api.Models.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ContractFlow.Api.Services;

/// <summary>
/// Role guard applied to every endpoint from day one (design §9.1): resolves
/// the caller via <see cref="ICurrentUser"/> — dev header now, Entra claims
/// later — and requires one of the listed roles. 401 without identity, 403
/// when the role doesn't match.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireRoleAttribute(params AppRole[] roles) : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        var user = await currentUser.GetAsync(context.HttpContext.RequestAborted);

        if (user is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (!roles.Contains(user.Role))
        {
            context.Result = new ObjectResult(new { error = "Forbidden for this role." })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }
}
