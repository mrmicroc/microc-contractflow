using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContractFlow.Api.Controllers;

/// <summary>Reference metadata for dropdowns — no domain data, any signed-in
/// role may read it.</summary>
[ApiController]
[Route("api/meta")]
public class MetaController : ControllerBase
{
    /// <summary>Supported holiday calendars: code + English display name.</summary>
    [HttpGet("calendars")]
    public IActionResult Calendars() =>
        Ok(BusinessDays.SupportedCalendars
            .Select(kv => new { code = kv.Key, name = kv.Value }));
}
