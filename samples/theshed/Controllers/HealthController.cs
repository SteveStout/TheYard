using Microsoft.AspNetCore.Mvc;
using TestProject.Application;

namespace TestProject.Controllers;

/// <summary>
/// The route the hosting platform's health check calls. It answers 200 when the home directory
/// exists and 503 when it does not. Azure App Service restarts a container whose health check
/// fails, so a missing home directory leads to a restart instead of a site that serves errors.
/// The reply never names the folder: the route answers anybody, and a disk path tells a stranger
/// how the server is laid out.
/// </summary>
[ApiController]
public sealed class HealthController(FileBrowser browser) : ControllerBase
{
    /// <summary>GET /healthz: reports whether the home directory exists.</summary>
    [HttpGet("/healthz")]
    public IActionResult Healthz() =>
        browser.HomeIsThere()
            ? Ok(new { status = "healthy" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unhealthy" });
}
