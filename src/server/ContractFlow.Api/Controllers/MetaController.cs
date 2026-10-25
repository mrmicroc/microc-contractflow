using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContractFlow.Api.Controllers;

/// <summary>Reference metadata for dropdowns — no domain data, any signed-in
/// role may read it.</summary>
[ApiController]
[Route("api/meta")]
public class MetaController : ControllerBase
{
    /// <summary>Supported holiday calendars: code + English display name.
    /// Regional variants use ISO 3166-2 codes (GB-SCT etc.).</summary>
    [HttpGet("calendars")]
    public IActionResult Calendars() =>
        Ok(BusinessDays.SupportedCalendars
            .Select(kv => new { code = kv.Key, name = kv.Value }));

    /// <summary>ISO 3166-1 alpha-2 countries: code + English display name.</summary>
    [HttpGet("countries")]
    public IActionResult Countries() => Ok(RegionCatalog.Countries);

    /// <summary>ISO 4217 currencies: code + English display name.</summary>
    [HttpGet("currencies")]
    public IActionResult Currencies() => Ok(RegionCatalog.Currencies);

    /// <summary>Product features switched on for this installation —
    /// <c>pad</c>: the PAD (pre-allocated day) model. Off hides PAD in the
    /// client; the server logic stays and only acts on terms with PAD
    /// enabled. Configured under <c>Features</c> in appsettings.</summary>
    [HttpGet("features")]
    public IActionResult Features([FromServices] IConfiguration config) =>
        Ok(new { pad = config.GetValue("Features:Pad", false) });
}
