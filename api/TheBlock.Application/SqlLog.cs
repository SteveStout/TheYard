namespace TheBlock.Application;

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

/// <summary>One parameter of a SQL statement, described but never valued.</summary>
public sealed record SqlParameterShape(string Name, string Type, int? Size);

/// <summary>
/// One SQL statement the application ran: its text, the shape of its
/// parameters, how long the database took, and the HTTP request that caused it.
/// </summary>
public sealed record SqlStatement(
    DateTimeOffset At,
    string Text,
    IReadOnlyList<SqlParameterShape> Parameters,
    long DurationMs,
    string Outcome,
    string? Request);

/// <summary>Port: where recent SQL statements are kept for the Admin tab.</summary>
public interface ISqlLog
{
    void Record(SqlStatement statement);
}

/// <summary>
/// Port: what request is in flight on this call path, as one short string like
/// "GET /api/vehicles". Infrastructure asks; the API answers from the ambient
/// HttpContext. Outside a request, such as at startup, the answer is null.
/// </summary>
public interface ICurrentRequest
{
    string? Describe();
}

/// <summary>The port wired to nothing, for the tests and the design-time tools.</summary>
public sealed class NullSqlLog : ISqlLog
{
    public static readonly NullSqlLog Instance = new();

    private NullSqlLog() { }

    public void Record(SqlStatement statement) { }
}

/// <summary>The port wired to nothing: no request, ever.</summary>
public sealed class NoCurrentRequest : ICurrentRequest
{
    public static readonly NoCurrentRequest Instance = new();

    private NoCurrentRequest() { }

    public string? Describe() => null;
}
// #endregion sql-log-port
