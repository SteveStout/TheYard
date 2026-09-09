using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TheYard.Api;

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

    // #region configured-key
    /// <summary>
    /// The fewest bytes HMAC-SHA256 will sign with. A shorter key is refused by
    /// the token handler on the first sign-in, which is a 500 on a page that
    /// was working a minute ago; better to treat it as no key at all.
    /// </summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// The configured signing key, or null when there is nothing usable in the
    /// setting: no value, a placeholder a failed deploy substitution left
    /// behind (anything starting with two underscores, the rule every other
    /// substituted setting follows), or a value too short to sign with. Null
    /// means the host invents a key and says so (ADR: Accounts and per-user
    /// bids) (ADR: Three readers with no memory of the project).
    /// </summary>
    public static string? ConfiguredKey(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured) || configured.StartsWith("__", StringComparison.Ordinal))
        {
            return null;
        }
        return Encoding.UTF8.GetByteCount(configured) < MinimumKeyBytes ? null : configured;
    }
    // #endregion configured-key

    /// <summary>
    /// The claim naming the store the session was opened on: "sql" or
    /// "cosmos", the same key the header uses. An account is a row or a
    /// document in one store, so a session is too; the bid endpoints read
    /// this rather than looking the account up on every bid (ADR: Three
    /// readers with no memory of the project).
    /// </summary>
    public const string StoreClaim = "store";

    // #region issue
    /// <summary>
    /// The claims are the user's id, the name to greet them by, and the store
    /// the account lives in, and nothing else. A token is sent on every
    /// request and is readable by anyone holding it, so anything in here is
    /// both bandwidth and disclosure; everything the application needs beyond
    /// identity it can look up.
    /// </summary>
    public string Issue(string userId, string email, string store)
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
                new Claim(StoreClaim, store),
            ]),
            Expires = DateTime.UtcNow.Add(_lifetime),
            SigningCredentials = _credentials,
        });
    }

    /// <summary>
    /// Secure when the browser reached us over TLS. Behind the edge this
    /// process is spoken to over plain HTTP, so `IsHttps` is false on a request
    /// that was HTTPS the whole way to the visitor; the forwarded header is
    /// what carries that fact across the hop (ADR: Edge deploy economics).
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

    // #region session-per-store
    /// <summary>
    /// Whether the session was opened on <paramref name="backend"/>. A token
    /// from one store used against the other, which the header allows in one
    /// request, would write a bid under an account the other store does not
    /// have: on the relational side the foreign key refused it as a 500, on
    /// the document side nothing refused it at all (ADR: Three readers with no
    /// memory of the project). A token with no store claim, which no token
    /// minted since 1.0.0.111 lacks, is on no store.
    /// </summary>
    public static bool SessionIsOn(this HttpContext context, Backend backend) =>
        string.Equals(context.User.FindFirstValue(TokenIssuer.StoreClaim), backend.Key, StringComparison.Ordinal);
    // #endregion session-per-store
}
