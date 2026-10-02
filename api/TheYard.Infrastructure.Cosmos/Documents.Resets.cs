// The document shape of the resets container: one password reset link per document, under the
// GUID the link carries, expired by the store. One file per container, so what a container
// holds is one short file to open.
namespace TheYard.Infrastructure.Cosmos;

// #region documents
/// <summary>
/// One reset link, under the GUID the link carries, partitioned on that id.
/// <c>ttl</c> is the store's own field: the document expires that many
/// seconds after it is written, whatever else happens.
/// </summary>
public sealed class ResetLinkDocument
{
    /// <summary>The GUID the link carries, which is also the partition key (<c>id</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>The reset token the link stands for, handed back when the link is used (<c>token</c>).</summary>
    public string Token { get; set; } = "";

    /// <summary>When the link stops working, as a round-trip UTC timestamp, checked on read (<c>expires</c>).</summary>
    public string Expires { get; set; } = "";

    /// <summary>Seconds the store keeps the document before it deletes it on its own (<c>ttl</c>).</summary>
    public int Ttl { get; set; }
}
// #endregion documents
