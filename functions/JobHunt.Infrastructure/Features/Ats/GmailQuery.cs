namespace JobHunt.Infrastructure.Features.Ats;

// #region gmail-query
/// <summary>
/// Writes a <see cref="MailSearch"/> in Gmail's search syntax, the one place that syntax is written. The words
/// go in a fixed order (in:inbox, from, subject, newer_than, -from:me), so every search the Function sends is
/// the same string it was when the use cases wrote it themselves; GmailQueryTests holds each one to that string.
/// </summary>
public static class GmailQuery
{
    /// <summary>The search as Gmail reads it.</summary>
    /// <param name="search">The search.</param>
    public static string Compose(MailSearch search)
    {
        var words = new List<string>();
        if (search.InboxOnly)
        {
            words.Add("in:inbox");
        }
        if (search.Senders.Count == 1)
        {
            words.Add("from:" + search.Senders[0]);
        }
        else if (search.Senders.Count > 1)
        {
            words.Add("from:(" + string.Join(" OR ", search.Senders) + ")");
        }
        if (search.SubjectPhrases.Count == 1)
        {
            words.Add(Subject(search.SubjectPhrases[0]));
        }
        else if (search.SubjectPhrases.Count > 1)
        {
            words.Add("(" + string.Join(" OR ", search.SubjectPhrases.Select(Subject)) + ")");
        }
        if (search.NewerThanDays is { } days)
        {
            words.Add("newer_than:" + days.ToString(System.Globalization.CultureInfo.InvariantCulture) + "d");
        }
        if (search.NotFromMe)
        {
            words.Add("-from:me");
        }
        return string.Join(' ', words);
    }

    private static string Subject(string phrase) => "subject:\"" + phrase + "\"";
}
// #endregion gmail-query
