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

    // #region renewal
    /// <summary>
    /// How often a session is re-signed while it is in use: once a day. A
    /// session is a year long (Steve, 13 September: "keep logins permanent"),
    /// and a year-long token that was never re-issued would end on a day the
    /// visitor did not choose; a token re-issued on the first request of each
    /// day it is used lasts a year past the last visit instead. Once a day
    /// and not every request, because a Set-Cookie on every response is
    /// bandwidth and a write to the browser for nothing.
    /// </summary>
    public static readonly TimeSpan RenewAfter = TimeSpan.FromDays(1);

    /// <summary>
    /// Whether a token that expires at <paramref name="expiresAtUnixSeconds"/>
    /// has been in use long enough to re-issue: true once more than a day of
    /// its lifetime has gone. A token with no readable expiry is left alone;
    /// the pipeline validated its lifetime already.
    /// </summary>
    public bool ShouldRenew(string? expiresAtUnixSeconds, DateTimeOffset now)
    {
        if (!long.TryParse(expiresAtUnixSeconds, out long seconds))
        {
            return false;
        }

        var expires = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var issued = expires - _lifetime;
        return now - issued >= RenewAfter;
    }
    // #endregion renewal

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
    public string Issue(string userId, string email, string store) => Issue(userId, email, store, _lifetime);

    /// <summary>The same token with a lifetime of the caller's choosing; the tests use it to make a token that is already a few days old.</summary>
    public string Issue(string userId, string email, string store, TimeSpan lifetime)
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
            Expires = DateTime.UtcNow.Add(lifetime),
            SigningCredentials = _credentials,
        });
    }

    /// <summary>
    /// Secure when the browser reached us over TLS. Behind the edge this
    /// process is spoken to over plain HTTP, so `IsHttps` is false on a request
    /// that was HTTPS the whole way to the visitor; the forwarded header is
    /// what carries that fact across the hop (ADR: Edge deploy economics).
    /// </summary>
    // #region reset-tokens
    /// <summary>
    /// A password reset link's token: the same signature as a session, a
    /// different audience so the session pipeline refuses it, an hour of
    /// life, and a fingerprint of the password hash it was minted against so
    /// it dies the moment the password changes (ADR: Accounts and per-user
    /// bids, addendum). No table of tokens to keep, no token provider that
    /// needs a key ring the container does not have.
    /// </summary>
    private const string ResetAudience = "theyard-reset";
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    public string IssueReset(string userId, string store, string? passwordHash)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ResetAudience,
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(StoreClaim, store),
                new Claim("fingerprint", Fingerprint(passwordHash)),
            ]),
            Expires = DateTime.UtcNow.Add(ResetLifetime),
            SigningCredentials = _credentials,
        });
    }

    /// <summary>The claims of a valid, unexpired reset token, or null for anything else, a session token included.</summary>
    public async Task<(string UserId, string Store, string Fingerprint)?> ReadResetAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var handler = new JsonWebTokenHandler();
        var parameters = Validation.Clone();
        parameters.ValidAudience = ResetAudience;
        var result = await handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid)
        {
            return null;
        }

        // The handler writes the long claim type as its short name and, on
        // its own, reads it back short; the bearer pipeline maps it long. Both
        // spellings are read so this does not depend on which handler minted it.
        string? id = (result.ClaimsIdentity.FindFirst(ClaimTypes.NameIdentifier) ?? result.ClaimsIdentity.FindFirst("nameid"))?.Value;
        string? store = result.ClaimsIdentity.FindFirst(StoreClaim)?.Value;
        string? fingerprint = result.ClaimsIdentity.FindFirst("fingerprint")?.Value;
        return id is null || store is null || fingerprint is null ? null : (id, store, fingerprint);
    }

    /// <summary>Sixteen hex characters of the password hash's SHA-256; the hash itself never leaves the store.</summary>
    public static string Fingerprint(string? passwordHash) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash ?? "")).AsSpan(0, 8));
    // #endregion reset-tokens

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
