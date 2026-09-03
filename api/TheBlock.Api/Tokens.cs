using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TheBlock.Api;

/// <summary>
/// The session, as a signed token in a cookie the browser cannot read
/// (ADR: Accounts and per-user bids).
///
/// The token is a JWT because the API is stateless and a signature is cheaper
/// than a session lookup. The cookie is httpOnly because the alternative,
/// localStorage, hands the token to any script that runs on the page, and the
/// whole point of this being a bearer token is that whoever holds it is the
/// user.
/// </summary>
public sealed class TokenIssuer
{
    public const string CookieName = "theyard_session";
    private const string Issuer = "theyard";
    private const string Audience = "theyard";

    private readonly SigningCredentials _credentials;
    private readonly TimeSpan _lifetime;

    public TokenIssuer(string signingKey, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _lifetime = lifetime;
        Validation = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            // The default is five minutes of slack on expiry, which is a
            // sensible allowance for clock drift between two servers and not
            // for a token this service both issues and reads.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    public TokenValidationParameters Validation { get; }

    public TimeSpan Lifetime => _lifetime;

    // #region issue
    /// <summary>
    /// The claims are the user's id and the name to greet them by, and nothing
    /// else. A token is sent on every request and is readable by anyone holding
    /// it, so anything in here is both bandwidth and disclosure; everything the
    /// application needs beyond identity it can look up.
    /// </summary>
    public string Issue(string userId, string email)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, email),
            ]),
            Expires = DateTime.UtcNow.Add(_lifetime),
            SigningCredentials = _credentials,
        });
    }

    /// <summary>
    /// Secure when the browser reached us over TLS. Behind the edge this
    /// process is spoken to over plain HTTP, so `IsHttps` is false on a request
    /// that was HTTPS the whole way to the visitor; the forwarded header is
    /// what carries that fact across the hop (ADR: Edge economics).
    /// </summary>
    public static CookieOptions CookieFor(HttpContext context, TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps
            || string.Equals(context.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase),
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = lifetime,
    };
    // #endregion issue
}

/// <summary>Who is asking, from the validated token the pipeline already read.</summary>
public static class Principals
{
    // #region who
    /// <summary>
    /// The caller's account id on an endpoint that required one. Throwing here
    /// would mean authorization let through a token with no subject, which is
    /// not a case to handle gracefully; it is a case to find out about.
    /// </summary>
    public static string UserId(this HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("An authorized request carried no account id");

    /// <summary>The caller's account id, or null where signing in is optional.</summary>
    public static string? UserIdOrNull(this HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    // #endregion who
}
