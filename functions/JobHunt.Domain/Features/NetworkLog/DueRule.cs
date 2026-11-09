namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>
/// When a conversation is owed a follow-up: Open, a TopicLine to write it from, and 21 to 45 days since the newer
/// of the two last exchanges. A second follow-up is owed only after a new exchange since the first. A Monday drafts
/// at most fifteen, the newest exchanges first, and the rest roll to the next Monday.
/// </summary>
public static class DueRule
{
    /// <summary>The days of silence before a follow-up is owed.</summary>
    public const int Days = 21;

    /// <summary>The oldest silence a follow-up is still written for: "a few weeks" stops being true after it.</summary>
    public const int StaleDays = 45;

    /// <summary>The most drafts one Monday builds.</summary>
    public const int MondayCap = 15;

    /// <summary>True when the row is owed a follow-up at <paramref name="now"/>.</summary>
    public static bool IsDue(Conversation row, DateTimeOffset now)
    {
        if (row.State != ConversationState.Open || string.IsNullOrWhiteSpace(row.TopicLine) || row.LastExchangeAt is not { } last)
        {
            return false;
        }
        if (now < last.AddDays(Days) || now > last.AddDays(StaleDays))
        {
            return false;
        }
        return row.FollowUpCount == 0 || row.DraftedAt is not { } drafted || last > drafted;
    }

    /// <summary>True when an Open row's newest exchange is older than the stale line: it closes, with no draft.</summary>
    public static bool IsStale(Conversation row, DateTimeOffset now) =>
        row.State == ConversationState.Open && row.LastExchangeAt is { } last && now > last.AddDays(StaleDays);

    /// <summary>The rows one Monday drafts: the due ones, newest exchange first, at most the cap.</summary>
    public static IReadOnlyList<Conversation> Batch(IEnumerable<Conversation> rows, DateTimeOffset now) =>
        [.. rows.Where(row => IsDue(row, now)).OrderByDescending(row => row.LastExchangeAt).ThenBy(row => row.PersonKey, StringComparer.Ordinal).Take(MondayCap)];
}
