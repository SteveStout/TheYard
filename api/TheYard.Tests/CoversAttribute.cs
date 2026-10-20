namespace TheYard.Tests;

/// <summary>
/// Names the route and the status code an integration test proves (ADR: The onion cut into
/// vertical slices, rule 7). SliceTests reads every endpoint a slice maps and the status codes it
/// declares, and fails when one of them has no test that says it covers it.
/// </summary>
/// <param name="method">The HTTP method, upper case.</param>
/// <param name="route">The route as it is mapped, with its parameters in braces.</param>
/// <param name="status">The status code the test receives.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CoversAttribute(string method, string route, int status) : Attribute
{
    /// <summary>The HTTP method.</summary>
    public string Method { get; } = method;

    /// <summary>The route as it is mapped.</summary>
    public string Route { get; } = route;

    /// <summary>The status code.</summary>
    public int Status { get; } = status;
}
