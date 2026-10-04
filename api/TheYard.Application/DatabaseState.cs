// The answer the startup check gives about a store: usable or not, and why. It sits in the
// Application ring because both adapters (the relational store and the document store) answer
// with it and the host reads it, and the rule about what its sentence may carry is worth finding
// in one place.
namespace TheYard.Application;

/// <summary>
/// Whether the store is usable, and one sentence about why. The composition
/// root asks this before it registers anything, so a database that will not
/// open changes which adapters are wired rather than becoming a 500 on the
/// first request (ADR: The relational store).
/// </summary>
/// <param name="Ready">True when the store opened and can be used.</param>
/// <param name="Note">One sentence about why the store is or is not ready.</param>
/// <param name="Failure">The exception that kept the store from opening, or null when it opened.</param>
public sealed record DatabaseState(bool Ready, string Note, Exception? Failure = null)
{
    /// <summary>How long bringing the schema up, or checking it was there, took. For the Admin tab's comparison card.</summary>
    public long SchemaMs { get; init; }

    /// <summary>How long the first-boot seed took, zero when there was nothing to seed.</summary>
    public long SeedMs { get; init; }

    /// <summary>What the seed cost in request units, which only the document store can say.</summary>
    public double? SeedRequestUnits { get; init; }

    /// <summary>
    /// One sentence safe to put anywhere, including a public page.
    ///
    /// <para><see cref="Note"/> names the engine and, on a failure, the type of
    /// exception. It never carries the exception's message, because a provider
    /// writes the server name, the login name, the database name and the
    /// caller's IP address into that message, and because a caller who has one
    /// of these sentences cannot know where it will be printed. The message
    /// travels as <see cref="Failure"/> instead, so a logger can record it in
    /// full while a surface that must not publish it can take the type alone.</para>
    /// </summary>
    public string Note { get; } = Note;
}
