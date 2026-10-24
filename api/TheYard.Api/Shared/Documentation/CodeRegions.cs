// The repository's marked regions as a port the slices share (ADR-014, live samples): the documents expand
// them into their pages, and the nightly card reads the job hunt Function's summaries through the same reader,
// so a card's words can be the words in the code without one slice reaching into another. The Docs slice
// answers it with LiveSamples.
namespace TheYard.Api.Shared.Documentation;

// #region code-regions
/// <summary>Reads one marked region of one repository file as this build carries it.</summary>
public interface ICodeRegions
{
    /// <summary>
    /// The region as the live reader renders it: a fenced block of its lines and a line naming where it was
    /// read, or a one-line note beginning "*Sample unavailable" when the file or the region is not there.
    /// </summary>
    /// <param name="path">The file, relative to the repository's root.</param>
    /// <param name="region">The region's name.</param>
    string Read(string path, string region);
}
// #endregion code-regions
