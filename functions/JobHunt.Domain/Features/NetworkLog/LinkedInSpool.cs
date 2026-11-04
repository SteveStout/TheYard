using System.Globalization;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>One LinkedIn spool file, read: its rows, and the lines it refused with why.</summary>
/// <param name="Rows">The rows that passed.</param>
/// <param name="Refused">Each refused line's number and reason.</param>
public sealed record LinkedInBatch(IReadOnlyList<Conversation> Rows, IReadOnlyList<string> Refused);

/// <summary>
/// The file the 6 AM program writes from LinkedIn Messaging: a header, then one tab-separated row a thread where both
/// sides wrote (person, company, profile URL, thread id, last inbound, last outbound, first two-way), times in ISO 8601
/// with an offset. A row without a profile, a thread id, a first two-way time or an inbound time is refused and named;
/// a row is never fixed up.
/// </summary>
public static class LinkedInSpool
{
    /// <summary>The header the program writes.</summary>
    public const string Header = "Person\tCompany\tProfileUrl\tThreadId\tLastInbound\tLastOutbound\tFirstTwoWay";

    /// <summary>The file's rows as conversations, sourced to the file's name.</summary>
    public static LinkedInBatch Parse(string text, string fileName)
    {
        var rows = new List<Conversation>();
        var refused = new List<string>();
        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != Header)
        {
            return new LinkedInBatch([], ["line 1: not the LinkedIn spool's header"]);
        }
        for (int n = 1; n < lines.Length; n++)
        {
            if (lines[n].Trim().Length == 0)
            {
                continue;
            }
            string[] f = lines[n].Split('\t');
            string key = f.Length == 7 ? PersonKey.ForLinkedIn(f[2]) : "";
            if (f.Length != 7 || key.Length == 0 || f[3].Trim().Length == 0 || !When(f[6], out var first) || !When(f[4], out var lastIn))
            {
                refused.Add($"line {n + 1}: needs a person, a profile, a thread id, a last inbound and a first two-way time");
                continue;
            }
            DateTimeOffset? lastOut = When(f[5], out var o) ? o : null;
            string person = f[0].Trim();
            string company = f[1].Trim();
            rows.Add(new Conversation
            {
                PersonKey = key,
                MatchKey = MatchKey.Of(person, company),
                FirstName = MatchKey.FirstName(person),
                FullName = person,
                Company = company.Length > 0 ? company : null,
                Channel = Channel.LinkedIn,
                ProfileUrl = f[2].Trim(),
                ThreadIds = [f[3].Trim()],
                FirstTwoWayAt = first,
                LastInboundAt = lastIn,
                LastOutboundAt = lastOut,
                LastChannel = Channel.LinkedIn,
                State = ConversationState.Open,
                Source = fileName,
            });
        }
        return new LinkedInBatch(rows, refused);
    }

    private static bool When(string text, out DateTimeOffset at) =>
        DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out at);
}
