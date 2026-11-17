namespace TheYard.Application.Shared.Stores;

// #region sql-log-port
// The Admin tab shows the raw SQL this application runs (ADR: What the database
// is actually doing). Two things about the shape of these types are deliberate.
//
// First, a statement carries the request that caused it. A list of SELECTs with
// no cause is a screensaver; the same list beside "GET /api/vehicles?make=Ford"
// is an explanation.
//
// Second, there is no place to put a parameter value. Not a redacted one, not a
// masked one: the type has no field for it. The Admin tab is public and the
// parameters of a registration carry an email address. A rule that says "redact
// this column" is one new column away from being wrong; a type with nowhere to
// write the value cannot leak it whatever anyone adds later.

/// <summary>One parameter of a statement, described but never valued.</summary>
/// <param name="Name">The parameter name, such as <c>@p0</c>.</param>
/// <param name="Type">The parameter's database type.</param>
/// <param name="Size">The declared size of the parameter, or null when it has none.</param>
public sealed record ParameterShape(string Name, string Type, int? Size);

/// <summary>
/// One statement the application ran on the relational store: its text, the shape of its
/// parameters, how long the database took, and the HTTP request that caused it.
/// </summary>
/// <param name="At">When the statement ran.</param>
/// <param name="Text">The statement's text as sent to the database.</param>
/// <param name="Parameters">The shape of each parameter: name, type and size, never the value.</param>
/// <param name="DurationMs">How long the database took, in milliseconds.</param>
/// <param name="Outcome">How the statement ended, such as success or the failure it met.</param>
/// <param name="Request">The HTTP request that caused the statement, like "GET /api/vehicles", or null when none did.</param>
public sealed record StoreStatement(
    DateTimeOffset At,
    string Text,
    IReadOnlyList<ParameterShape> Parameters,
    long DurationMs,
    string Outcome,
    string? Request);

/// <summary>Port: where recent statements are kept for the Admin tab.</summary>
public interface IStatementLog
{
    void Record(StoreStatement statement);
}

/// <summary>
/// Port: what request is in flight on this call path, as one short string like
/// "GET /api/vehicles". Infrastructure asks; the API answers from the ambient
/// HttpContext. Outside a request, such as at startup, the answer is null.
/// </summary>
public interface ICurrentRequest
{
    string? Describe();

    /// <summary>
    /// Something that tells this request apart from the next one to the same
    /// path, for the store log's per-request arithmetic. The trace identifier,
    /// on the API; null outside a request. Never shown on a page.
    /// </summary>
    string? Identify();
}

/// <summary>The port wired to nothing, for the tests and the design-time tools.</summary>
public sealed class NullStatementLog : IStatementLog
{
    public static readonly NullStatementLog Instance = new();

    private NullStatementLog() { }

    public void Record(StoreStatement statement) { }
}

/// <summary>The port wired to nothing: no request, ever.</summary>
public sealed class NoCurrentRequest : ICurrentRequest
{
    public static readonly NoCurrentRequest Instance = new();

    private NoCurrentRequest() { }

    public string? Describe() => null;

    public string? Identify() => null;
}
// #endregion sql-log-port
