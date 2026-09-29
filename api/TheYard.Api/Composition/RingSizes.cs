namespace TheYard.Api;

/// <summary>
/// How much the in-memory rings behind the Admin tab hold: the request ring, and
/// the machine samples, an hour at a quarter of a minute each, which is also the
/// number of rows the relational store's own view keeps (ADR: What the machines are doing).
/// </summary>
public static class RingSizes
{
    /// <summary>An hour of machine samples, four a minute.</summary>
    public const int MachineSamples = 240;

    /// <summary>The requests the timing card reads.</summary>
    public const int RequestRing = 500;
}
