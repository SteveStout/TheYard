using System.Text.Json;
using TestProject.Controllers;

namespace TestProject.Composition;

/// <summary>
/// Registers the HTTP API. It adds the controllers, makes every JSON name snake_case in both
/// directions, and turns every expected failure into an RFC 9457 problem document so the page
/// always gets errors in one shape.
/// </summary>
public static class ApiRegistration
{
    /// <summary>
    /// Adds the controllers, the snake_case JSON settings, the problem details service and the
    /// exception handler that fills it in.
    /// </summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheShedApi(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers().AddJsonOptions(json =>
        {
            json.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            json.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        });
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<BrowserProblemHandler>();
    }
}
