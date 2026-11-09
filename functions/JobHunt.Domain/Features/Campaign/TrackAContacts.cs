using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// The named people the night's jobs lane found for its ready roles, one tab-separated file a night
/// (nightly/track-a-contacts-&lt;date&gt;.tsv): the company and its registrable domain, the role and its posting, the
/// person named on a public page with that page, an address only when a page showed that exact address, and the
/// tagged code and links make_ref.py minted for the email. A row that does not read whole is skipped and counted.
/// </summary>
public static partial class TrackAContacts
{
    /// <summary>The columns, in order.</summary>
    public static readonly IReadOnlyList<string> Columns = ["Company", "CompanyKey", "RoleTitle", "PostingUrl", "FirstName", "ContactName", "ContactTitle", "NamePage", "ContactEmail", "EmailSource", "RefCode", "ResumeLink", "SiteLink", "GitHubLink", "LinkedInLink", "AboutLink", "ProofKey"];

    /// <summary>The file for a Central day.</summary>
    public static string File(DateOnly day) => $"nightly/track-a-contacts-{day:yyyy-MM-dd}.tsv";

    /// <summary>The rows that read whole, and how many did not.</summary>
    public static (IReadOnlyList<TrackAContact> Rows, int Unread) Parse(string text)
    {
        var rows = new List<TrackAContact>();
        int unread = 0;
        foreach (string raw in (text ?? string.Empty).Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.Trim().Length == 0 || raw.StartsWith("Company\t", StringComparison.Ordinal))
            {
                continue;
            }
            string[] c = raw.Split('\t').Select(cell => cell.Trim()).ToArray();
            if (c.Length < Columns.Count || c[0].Length == 0 || !Key().IsMatch(c[1]) || c[4].Length == 0 || c[5].Length == 0 || !c[7].StartsWith("https://", StringComparison.Ordinal))
            {
                unread++;
                continue;
            }
            string? Cell(int i) => c[i].Length == 0 ? null : c[i];
            rows.Add(new TrackAContact(c[0], c[1].ToLowerInvariant(), Cell(2), Cell(3), c[4], c[5], c[6], c[7], Cell(8), Cell(9), Cell(10),
                Cell(12) is null || Cell(10) is null ? null : new SpoolLinks(c[11], c[12], c[13], c[14], c[15]), Cell(16)));
        }
        return (rows, unread);
    }

    /// <summary>
    /// Whether an address is a shared mailbox and not a person's: info@, contact@, careers@ and the other boxes the lead
    /// run may write for Track B. A Track A email follows an application to the person who would hire, so a shared box
    /// is never its address.
    /// </summary>
    public static bool IsGeneralMailbox(string? address) =>
        address is { } a && a.IndexOf('@', StringComparison.Ordinal) is int at and > 0 && General().IsMatch(a[..at]);

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$", RegexOptions.IgnoreCase)]
    private static partial Regex Key();

    [GeneratedRegex(@"^(info|contact|contactus|hello|careers|jobs|talent|recruiting|recruitment|hr|information|press|pr|media|team|admin|office|support|sales|help)$", RegexOptions.IgnoreCase)]
    private static partial Regex General();
}

/// <summary>One named person for a ready role.</summary>
/// <param name="Company">The company as the posting names it.</param>
/// <param name="CompanyKey">Its registrable domain.</param>
/// <param name="RoleTitle">The role.</param>
/// <param name="PostingUrl">The posting.</param>
/// <param name="FirstName">The first name as the person uses it.</param>
/// <param name="ContactName">The full name.</param>
/// <param name="ContactTitle">The title as the page writes it.</param>
/// <param name="NamePage">The public page that named the person.</param>
/// <param name="ContactEmail">The address a public page showed, or null.</param>
/// <param name="EmailSource">That page, or null.</param>
/// <param name="RefCode">The tagged code make_ref.py minted, or null.</param>
/// <param name="Links">The five tagged links, or null.</param>
/// <param name="ProofKey">reliability, platform, leadership or ai, or null.</param>
public sealed record TrackAContact(string Company, string CompanyKey, string? RoleTitle, string? PostingUrl, string FirstName, string ContactName, string ContactTitle,
    string NamePage, string? ContactEmail, string? EmailSource, string? RefCode, SpoolLinks? Links, string? ProofKey);
