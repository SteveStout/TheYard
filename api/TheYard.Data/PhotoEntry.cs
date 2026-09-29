namespace TheYard.Data;

/// <summary>
/// One photo in the vendored stock-photo manifest: its file name, the body
/// style pool it belongs to, and the source title (which reveals the make of
/// the vehicle pictured).
/// </summary>
/// <param name="File">The photo's file name.</param>
/// <param name="Style">The body style pool the photo belongs to.</param>
/// <param name="Title">The source title of the photo, which names the make pictured.</param>
public sealed record PhotoEntry(string File, string Style, string Title);

