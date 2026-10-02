// Which request a SQL statement or store operation belongs to, read from the ambient HttpContext.
// Its own file because it is the one piece here that touches the web request, and what it leaves
// out (the query string) is a rule about what a public page may print.
using TheYard.Application;

namespace TheYard.Api;

// #region admin-observability
/// <summary>
/// Answers <see cref="ICurrentRequest"/> from the ambient HttpContext: the
/// method and the path, and deliberately not the query string.
///
/// <para>The query string would explain a statement better:
/// "GET /api/vehicles?make=Ford" says more than "GET /api/vehicles" does. It
/// is also the line a password-reset token, an email confirmation link or a
/// share link would arrive on, and this answer is printed on a public page.
/// Losing the filter is a smaller cost than being one feature away from
/// publishing a token (ADR: Reviewing my own work).</para>
/// </summary>
public sealed class HttpCurrentRequest(IHttpContextAccessor accessor) : ICurrentRequest
{
    /// <summary>The method and the path, the path cut to 200 characters; null outside a request.</summary>
    public string? Describe()
    {
        var context = accessor.HttpContext;
        if (context is null)
        {
            return null;
        }

        string path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
        return $"{context.Request.Method} {(path.Length > 200 ? path[..200] + "..." : path)}";
    }

    /// <summary>
    /// The request's trace identifier, which ties a statement to the request that sent it; null
    /// outside a request.
    /// </summary>
    public string? Identify() => accessor.HttpContext?.TraceIdentifier;
}
// #endregion admin-observability
