using Microsoft.AspNetCore.Mvc;
using TestProject.Domain;

namespace TestProject.Controllers;

/// <summary>
/// One route the host's health check calls: 200 when the home directory can be
/// read, 503 when it cannot. App Service restarts a container whose health
/// check fails, which is the whole reason to have one.
/// </summary>
[ApiController]
public sealed class HealthController(HomePath home) : ControllerBase
{
    /// <summary>GET /healthz: is the home directory there?</summary>
    [HttpGet("/healthz")]
    public IActionResult Healthz() =>
        Directory.Exists(home.Root)
            ? Ok(new { status = "healthy", home = home.Root })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unhealthy", home = home.Root });
}
