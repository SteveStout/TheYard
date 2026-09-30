namespace TestProject.Data;

// The shapes of the JSON bodies the page sends to the API. A path is relative to the home
// directory, uses forward slashes, and "" means home itself.

/// <summary>The JSON body of a move or copy request.</summary>
/// <param name="From">The file or folder to move or copy, relative to home.</param>
/// <param name="To">The full path it goes to, relative to home, new name included.</param>
public sealed record MoveRequest(string From, string To);
