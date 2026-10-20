namespace TheYard.Api;

/// <summary>
/// Whether the per-visitor rows (the visitor table and the kept log) are served
/// at all, from Admin:VisitorRows; off by default on the owner's rule
/// (ADR: Site activity, and the line an address does not cross, sixth addendum).
/// </summary>
/// <param name="VisitorRows">True when the two endpoints answer to the operator's key; false, a 404 to everybody.</param>
public sealed record AdminSettings(bool VisitorRows);
