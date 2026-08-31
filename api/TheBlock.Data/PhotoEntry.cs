namespace TheBlock.Data;

/// <summary>
/// One photo in the vendored stock-photo manifest: its file name, the body
/// style pool it belongs to, and the source title (which reveals the make of
/// the vehicle pictured).
/// </summary>
public sealed record PhotoEntry(string File, string Style, string Title);

