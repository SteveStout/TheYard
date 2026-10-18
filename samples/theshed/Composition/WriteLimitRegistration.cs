using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TestProject.Application;
using TestProject.Controllers;

namespace TestProject.Composition;

/// <summary>
/// Registers the limit on write requests: each visitor gets a fixed number of uploads, new
/// folders, deletes, moves and copies a minute, and the next one is refused with a 429 problem
/// document until the minute turns over. Reads are never limited.
/// </summary>
public static class WriteLimitRegistration
{
    /// <summary>Adds the write policy, partitioned per visitor, with the problem document a refusal answers.</summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheShedWriteLimits(this WebApplicationBuilder builder)
    {
        // #region write-limits
        int perMinute = builder.Configuration.GetSection(FilesOptions.Section).GetValue("WritesPerMinute", new FilesOptions().WritesPerMinute);
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(WritesAllowedAttribute.LimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                VisitorOf(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            limiter.OnRejected = async (rejected, cancellation) =>
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many writes",
                    Detail = $"This site takes {perMinute} writes a minute from one visitor. Wait a minute and try again.",
                };
                await rejected.HttpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellation);
            };
        });
        // #endregion write-limits
    }

    /// <summary>
    /// Who a request counts against. Behind App Service the connection comes from its front end,
    /// so every visitor would share one address; the front end appends the visitor's own address
    /// as the last entry of X-Forwarded-For, which is the one a visitor cannot write for
    /// themselves. A request with no such header, a local run or a test, counts against the
    /// connection's address.
    /// </summary>
    /// <param name="context">The request being counted.</param>
    public static string VisitorOf(HttpContext context)
    {
        string? forwarded = context.Request.Headers["X-Forwarded-For"].LastOrDefault();
        string? last = forwarded?.Split(',').Select(part => part.Trim()).LastOrDefault(part => part.Length > 0);
        return last ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
