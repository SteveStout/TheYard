using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Sheet;

/// <summary>
/// The tracker gate's BANNED verdict (check_tracker.py in the job-hunt folder, the rule "the tracker
/// is a communication log"), ported line for line so the mirror never adds a row the gate would take
/// out: a row where nothing reached the counterparty, or a row with no counterparty at all. A real
/// send or receive verb in the note outranks the first; the gate's SUSPECT verdict is a question for
/// Steve, not a ban, so the mirror adds those rows as the records run did.
/// </summary>
public static partial class TrackerGate
{
    /// <summary>Why the row is banned, or null when it is not.</summary>
    /// <param name="row">Count, Person, Company, Communication, Date, Phone/Email, Notes.</param>
    public static string? Banned(IReadOnlyList<string> row)
    {
        if (row.Count < 7)
        {
            return "malformed row, fewer than 7 columns";
        }
        string notes = row[6] ?? string.Empty;
        string headline = notes.Length > 130 ? notes[..130] : notes;
        string person = (row[1] ?? string.Empty).Trim().ToUpperInvariant();
        string company = (row[2] ?? string.Empty).Trim().ToUpperInvariant();
        var real = Communication().Match(notes);
        var nothing = NothingPassed().Match(headline);
        if (nothing.Success && !(real.Success && real.Index < 130))
        {
            return $"the headline says nothing reached the counterparty (\"{nothing.Value}\")";
        }
        if (person is "" or "NA" && company is "" or "NA")
        {
            return "Person and Company are both NA, so there is no counterparty";
        }
        return null;
    }

    [GeneratedRegex(@"\bapplication submitted\b|\bsubmitted and confirmed\b|\bapplied\b|\bmessage sent\b|\binvite sent\b|\bconnection request sent\b|\bfollow-?up (message )?sent\b|\bemail sent\b|\bsent by (claude|steve)\b|\bshe replied\b|\bhe replied\b|\breplied\b|\breply received\b|\brejection\b|\binterview (held|booked|scheduled|invitation)\b|\boffer\b|\brecruiter (reachout|outreach)\b|\bthey wrote\b|\binbound\b|\bscreen(ing)? (held|call)\b|\bwithdrew\b|\bdeclined\b", RegexOptions.IgnoreCase)]
    private static partial Regex Communication();

    [GeneratedRegex(@"\bnot submitted\b|\bfound closed on the page\b|\bno application possible\b|\brequisition (found )?closed\b|\bnothing was sent\b|\bfilled but not\b|\bfilled and deliberately not\b", RegexOptions.IgnoreCase)]
    private static partial Regex NothingPassed();
}
