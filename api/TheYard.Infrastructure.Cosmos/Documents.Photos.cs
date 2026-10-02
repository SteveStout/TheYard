// The document shape of the photos container: one photo manifest entry per document,
// partitioned on the body style. One file per container, so what a container holds is one
// short file to open; the mapping to the domain record is in Documents.cs.
using System.Text.Json.Serialization;

namespace TheYard.Infrastructure.Cosmos;

// #region documents
/// <summary>One photo manifest entry. Partitioned on <c>style</c>, which is how the loader groups them.</summary>
public sealed class PhotoDocument
{
    /// <summary>The file name, which is unique in the manifest and is the id for that reason.</summary>
    public string Id { get; set; } = "";

    /// <summary>The entry's place in the manifest, so a read can put it back in file order (<c>seq</c>).</summary>
    public int Seq { get; set; }

    /// <summary>The body style the photo shows, and the partition key (<c>style</c>).</summary>
    public string Style { get; set; } = "";

    /// <summary>The photo's caption (<c>title</c>).</summary>
    public string Title { get; set; } = "";

    /// <summary>The document's version, kept by the store (<c>_etag</c>).</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
// #endregion documents
