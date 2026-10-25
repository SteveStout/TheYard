using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Ats;

/// <summary>
/// The records run's ATS rules as code (BOOKKEEPING-LANE.md step 3): what a known sender's message is,
/// which company it is about, and what the sweep does with it. A receipt or a generic rejection gets a
/// tracker row unless one it can rest on exists, and is archived; a rejection that gives a reason gets
/// its row and stays for Steve; mail from before this search is archived with no row; anything the
/// rules cannot read for certain stays in the inbox and is listed, never guessed.
/// </summary>
public static partial class AtsRules
{
    /// <summary>The day this search began: mail before it belongs to an earlier search.</summary>
    public static readonly DateOnly SearchBegan = new(2026, 1, 1);

    /// <summary>What a message is.</summary>
    public static AtsKind Kind(InboxMessage message)
    {
        string text = message.Subject + "\n" + message.Text;
        bool rejection = Rejection().IsMatch(text);
        if (rejection)
        {
            return Reason().IsMatch(text) ? AtsKind.RejectionWithReason : AtsKind.Rejection;
        }
        return Receipt().IsMatch(text) && Received().IsMatch(text) && !Asks().IsMatch(text) ? AtsKind.Receipt : AtsKind.Unknown;
    }

    /// <summary>
    /// The company a message is about: the one company with an application on the master whose name appears
    /// in the sender's name, the subject or the opening of the text. None, or two different ones, is null.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="companies">The companies with an application on the master.</param>
    public static string? Company(InboxMessage message, IReadOnlyCollection<string> companies)
    {
        string text = Normal(message.Name + " " + message.Subject + " " + (message.Text.Length > 2000 ? message.Text[..2000] : message.Text));
        var found = companies
            .Where(company => Normal(company).Length >= 3 && !Mail.AtsDomains.Any(ats => Normal(ats).StartsWith(Normal(company), StringComparison.Ordinal)))
            .Where(company => Regex.IsMatch(text, @"(^|\W)" + Regex.Escape(Normal(company)) + @"($|\W)"))
            .Select(company => company.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // A longer name that holds a shorter one ("Acme Health" and "Acme") is the one meant.
        found = [.. found.Where(company => !found.Any(other => other.Length > company.Length && Normal(other).Contains(Normal(company), StringComparison.Ordinal)))];
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>The decision for one message, against the master's rows.</summary>
    /// <param name="message">The message.</param>
    /// <param name="threadSenders">Every From address in its thread.</param>
    /// <param name="master">The master's data rows.</param>
    public static AtsDecision Decide(InboxMessage message, IReadOnlyList<string> threadSenders, IReadOnlyList<string[]> master)
    {
        var person = threadSenders.FirstOrDefault(sender => !Mail.IsMachine(Mail.Address(sender)));
        if (person is not null)
        {
            return AtsDecision.Leave(message, AtsKind.Unknown, null, $"a person is in the thread ({Mail.Address(person)})");
        }
        var kind = Kind(message);
        var day = DateOnly.FromDateTime(CentralClock.Local(message.At));
        // A message a row already names by its Gmail id is logged: it rests there, whoever wrote the row.
        var named = master.FirstOrDefault(row => row.Length >= 7 && row[6].Contains(message.Id, StringComparison.Ordinal));
        // An interview, scheduling or account mail a row names is still mail Steve acts on: it stays.
        if (named is not null && kind == AtsKind.Unknown)
        {
            return AtsDecision.Leave(message, kind, named[2], $"a row names it (Count {named[0]}), but it is neither a receipt nor a rejection: interview, scheduling and account mail stays");
        }
        if (named is not null)
        {
            return new AtsDecision(message, kind, named[2], AtsAction.ArchiveOnly, named[0], null, $"already logged on Count {named[0]}");
        }
        if (day < SearchBegan && kind is AtsKind.Receipt or AtsKind.Rejection)
        {
            return new AtsDecision(message, kind, null, AtsAction.ArchiveOnly, null, null, "before this search began: archived with no row");
        }
        if (kind == AtsKind.Unknown)
        {
            return AtsDecision.Leave(message, kind, null, "the text reads as neither a receipt nor a rejection");
        }
        var applications = master.Where(TrackerRows.IsApplication).ToList();
        string? company = Company(message, [.. applications.Select(row => row[2])]);
        if (company is null)
        {
            return AtsDecision.Leave(message, kind, null, "no one company with an application on the master is named in it");
        }
        var mine = applications.Where(row => Same(row[2], company)).ToList();
        if (kind == AtsKind.Receipt)
        {
            var rest = mine.FirstOrDefault(row => Near(row[4], day));
            return rest is not null
                ? new AtsDecision(message, kind, company, AtsAction.ArchiveOnly, rest[0], null, $"rests on application Count {rest[0]}")
                : new AtsDecision(message, kind, company, AtsAction.RowAndArchive, null, Row(message, kind, company, day, mine), "a receipt with no application row within two days");
        }
        var logged = master.FirstOrDefault(row => row.Length >= 7 && Same(row[2], company) && Near(row[4], day) && row[6].TrimStart().StartsWith("REJECTION", StringComparison.OrdinalIgnoreCase));
        // Processed means archived (CONVENTIONS, 10/22): a rejection that gives a reason is logged with the
        // reason in its row and archived like any other; the row, not the inbox, is where Steve reads it.
        if (kind == AtsKind.RejectionWithReason)
        {
            return logged is not null
                ? new AtsDecision(message, kind, company, AtsAction.ArchiveOnly, logged[0], null, $"a rejection with a reason, rests on Count {logged[0]}")
                : new AtsDecision(message, kind, company, AtsAction.RowAndArchive, null, Row(message, kind, company, day, mine), "a rejection with a reason: logged with the reason and archived");
        }
        return logged is not null
            ? new AtsDecision(message, kind, company, AtsAction.ArchiveOnly, logged[0], null, $"rests on rejection Count {logged[0]}")
            : new AtsDecision(message, kind, company, AtsAction.RowAndArchive, null, Row(message, kind, company, day, mine), "a generic rejection with no rejection row within two days");
    }

    /// <summary>The six-column row the sweep drops in the spool: Person, Company, Communication, Date, Phone/Email, Notes.</summary>
    public static string[] Row(InboxMessage message, AtsKind kind, string company, DateOnly day, IReadOnlyList<string[]> applications)
    {
        string when = CentralClock.Local(message.At).ToString("M/d/yyyy h:mm tt", CultureInfo.InvariantCulture) + " Central";
        string head = kind switch
        {
            AtsKind.Receipt => "RECEIPT",
            AtsKind.RejectionWithReason => "REJECTION WITH A REASON",
            _ => "REJECTION, generic",
        };
        string on = applications.Count == 0 ? "no application row found for this company" : "applications on file: Counts " + string.Join(", ", applications.Select(row => row[0]));
        string notes = $"{head}, {when} (Gmail msg {message.Id}, from {message.Address}, subject '{message.Subject.Trim()}'): {on}. Logged by the Function's ATS sweep.";
        return ["NA", company, "Email", day.ToString("M/d/yyyy", CultureInfo.InvariantCulture), message.Address, notes];
    }

    /// <summary>A master Date cell (M/d/yyyy or yyyy-MM-dd) within two days of a day.</summary>
    public static bool Near(string cell, DateOnly day) =>
        DateOnly.TryParseExact((cell ?? string.Empty).Trim(), ["M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        && Math.Abs(date.DayNumber - day.DayNumber) <= 2;

    private static bool Same(string a, string b) => string.Equals(Normal(a), Normal(b), StringComparison.Ordinal);

    private static string Normal(string text) => Space().Replace((text ?? string.Empty).ToLowerInvariant(), " ").Trim();

    [GeneratedRegex(@"\b(not (to )?(move|moving) forward|decided (not to|to (move|pursue|proceed|go) (forward )?with (other|another|a different))|other candidates|will not be (moving|proceeding)|won't be moving|position has been filled|no longer (being )?consider|regret to inform|unable to offer|not selected)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Rejection();

    [GeneratedRegex(@"\b(locat(ed|ion)|reside|residen(ce|cy)|state of|time ?zone|sponsor|number of applications|applications? (limit|cap|in the last)|three applications|maximum of|eligib)", RegexOptions.IgnoreCase)]
    private static partial Regex Reason();

    [GeneratedRegex(@"\b(thank(s| you) for (applying|your application|your interest|submitting))\b", RegexOptions.IgnoreCase)]
    private static partial Regex Receipt();

    [GeneratedRegex(@"\b(received|we('| a)re reviewing|will review|review(ing)? your|successfully submitted|has been submitted|be in touch)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Received();

    [GeneratedRegex(@"\b(assessment|your availability|complete (the|your|this) (assessment|questionnaire|form|test)|verify your|action required|please (reply|respond|confirm)|schedule (a|your) (call|interview|time))\b", RegexOptions.IgnoreCase)]
    private static partial Regex Asks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}

/// <summary>What a known sender's message is.</summary>
public enum AtsKind
{
    /// <summary>The rules cannot tell; it stays in the inbox.</summary>
    Unknown,

    /// <summary>An application received.</summary>
    Receipt,

    /// <summary>A rejection that gives no reason.</summary>
    Rejection,

    /// <summary>A rejection that names a reason (a location, a cap, sponsorship): logged and left for Steve.</summary>
    RejectionWithReason,
}

/// <summary>What the sweep does with a message.</summary>
public enum AtsAction
{
    /// <summary>Nothing: it stays in the inbox and is listed.</summary>
    Leave,

    /// <summary>Archived, resting on a row already on the master, or from before this search.</summary>
    ArchiveOnly,

    /// <summary>A row dropped in the spool, then archived.</summary>
    RowAndArchive,
}

/// <summary>The sweep's decision on one message.</summary>
public sealed record AtsDecision(InboxMessage Message, AtsKind Kind, string? Company, AtsAction Action, string? RestsOn, string[]? Row, string Why)
{
    /// <summary>A message left in the inbox, with the reason.</summary>
    public static AtsDecision Leave(InboxMessage message, AtsKind kind, string? company, string why) => new(message, kind, company, AtsAction.Leave, null, null, why);
}
