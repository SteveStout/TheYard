namespace JobHunt.Application.Features.Ats;

// #region mail-search
/// <summary>
/// A search of the mailbox as data: whose mail, which subjects, how recent, and whether only what is still in
/// the inbox. The use cases build one from what the Domain knows (a board's senders, the tracking systems'
/// domains); the adapter that owns the mail service turns it into that service's search syntax, so no rule or
/// use case writes a search language.
/// </summary>
/// <param name="Senders">The addresses or domains the mail is from; none means anybody.</param>
/// <param name="SubjectPhrases">Phrases the subject carries, any one of them; none means any subject.</param>
/// <param name="InboxOnly">True to find only mail still in the inbox.</param>
/// <param name="NewerThanDays">Only mail younger than this many days, or null for any age.</param>
/// <param name="NotFromMe">True to leave out mail Steve sent.</param>
public sealed record MailSearch(
    IReadOnlyList<string> Senders,
    IReadOnlyList<string> SubjectPhrases,
    bool InboxOnly = false,
    int? NewerThanDays = null,
    bool NotFromMe = false)
{
    /// <summary>Everything still in the inbox.</summary>
    public static MailSearch WholeInbox { get; } = new([], [], InboxOnly: true);

    /// <summary>Mail from these senders still in the inbox.</summary>
    /// <param name="senders">The addresses or domains.</param>
    /// <param name="subjects">Phrases the subject carries, any one of them.</param>
    public static MailSearch InInboxFrom(IReadOnlyList<string> senders, IReadOnlyList<string>? subjects = null) =>
        new(senders, subjects ?? [], InboxOnly: true);
}
// #endregion mail-search
