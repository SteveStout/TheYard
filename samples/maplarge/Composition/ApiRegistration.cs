using System.Text.Json;
using TestProject.Controllers;

namespace TestProject.Composition;

/// <summary>
/// The HTTP surface: controllers, a snake_case wire end to end, and every failure as an RFC 9457
/// problem document (ADR-004).
/// </summary>
public static class ApiRegistration
{
    /// <summary>Registers the controllers, the wire format and the problem handler.</summary>
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
