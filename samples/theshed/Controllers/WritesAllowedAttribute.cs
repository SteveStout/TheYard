using Microsoft.AspNetCore.Mvc.Filters;
using TestProject.Application;

namespace TestProject.Controllers;

/// <summary>
/// Marks an action as a write. Before the action runs it reads Files:AllowWrites, and when writes
/// are off it refuses with a 403 problem document instead, so the action never touches the disk
/// and an upload's body is never read.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WritesAllowedAttribute : ActionFilterAttribute
{
    /// <summary>The rate limiter policy every write action names, registered in Composition/WriteLimitRegistration.cs.</summary>
    public const string LimitPolicy = "writes";

    /// <summary>What a visitor is told when writes are off, and what still works.</summary>
    public const string OffReason =
        "This public copy of the file browser is read only, so nobody can empty it for the next visitor. Browse, search and download all work; run it locally to try uploads, moves and deletes.";

    /// <summary>Refuses the write when the setting is off; otherwise lets it through.</summary>
    /// <param name="context">The action about to run.</param>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<FilesOptions>();
        if (!options.AllowWrites)
        {
            throw ApiRefusalException.Forbidden(OffReason);
        }
    }
}
