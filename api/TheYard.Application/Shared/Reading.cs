// One answer of an Admin query or command (ADR-115's fourth rung), shared by every slice whose handler answers a
// card: the operations, the costs, the proof and the documents. Shared because four slices answer with it and
// none owns it; a slice that reached into another's folder for it would break rule 1 of the slices.
namespace TheYard.Application.Shared;

// #region admin-reading
/// <summary>How a reading answers: the body, or why there is none.</summary>
public enum ReadingKind
{
    /// <summary>The reading, 200.</summary>
    Read,

    /// <summary>The request named something the reading cannot be for (a window, a card, a kind), 400.</summary>
    Refused,

    /// <summary>Nothing to read, 404.</summary>
    Missing,

    /// <summary>A run was started, 202.</summary>
    Started,

    /// <summary>A run is already going or ran a moment ago, 409.</summary>
    Busy,
}

/// <summary>One answer of an Admin query or command: the body when there is one, else what to say.</summary>
/// <param name="Kind">How it answers.</param>
/// <param name="Body">The reading, or the text a file reading is.</param>
/// <param name="Title">The problem's title when there is no body.</param>
/// <param name="Detail">The problem's detail when there is no body.</param>
public sealed record Reading(ReadingKind Kind, object? Body = null, string? Title = null, string? Detail = null)
{
    /// <summary>A reading.</summary>
    public static Reading Of(object body) => new(ReadingKind.Read, body);

    /// <summary>The window the Admin cards read in, refused when it is not one of the three.</summary>
    public static Reading BadWindow() => new(ReadingKind.Refused, Title: "The window could not be read", Detail: "window is one of 24h, 7d or 30d.");

    /// <summary>True when the window is absent or one of 24h, 7d and 30d.</summary>
    public static bool WindowReads(string? window) => window is null or "24h" or "7d" or "30d";
}
// #endregion admin-reading
