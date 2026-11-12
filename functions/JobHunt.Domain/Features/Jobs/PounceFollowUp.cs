using System.Globalization;
using System.Text.RegularExpressions;
using JobHunt.Domain.Shared.Tracker;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>A code minted on a pounce note, as link-tokens.csv holds it: who it went to and for which caught role.</summary>
/// <param name="Code">The six-character code.</param>
/// <param name="Person">The person, or the words the program wrote when the name was found at send.</param>
/// <param name="Company">The company.</param>
/// <param name="Channel">The first touch's channel, LinkedIn or Email.</param>
/// <param name="Sent">When the code was minted.</param>
/// <param name="Key">The caught role's posting key, from the slot the code was minted in.</param>
public sealed record PounceCode(string Code, string Person, string Company, string Channel, DateTimeOffset Sent, long Key);

/// <summary>What the site says one code did: its opens and paths (/api/admin/links) and its highest review stop (/api/admin/review).</summary>
/// <param name="Code">The code.</param>
/// <param name="FirstOpen">Its first open in the window.</param>
/// <param name="LastOpen">Its last open in the window.</param>
/// <param name="Opens">How many opens.</param>
/// <param name="Paths">The paths opened, with a count each.</param>
/// <param name="Stop">The highest review stop reached, 0 for none.</param>
public sealed record CodeVisit(string Code, DateTimeOffset FirstOpen, DateTimeOffset LastOpen, int Opens, IReadOnlyDictionary<string, int> Paths, int Stop);

/// <summary>One follow-up the pass found due: the code, what it did, the rung it reached and the text.</summary>
/// <param name="Code">The code and who it went to.</param>
/// <param name="Visit">What the site says it did.</param>
/// <param name="Rung">The highest rung reached, 1 to 3.</param>
/// <param name="Text">The follow-up, as it would be sent.</param>
public sealed record FollowUp(PounceCode Code, CodeVisit Visit, int Rung, string Text);

/// <summary>
/// The second touch after a pounce note is opened. A code minted in a pounce slot (its lane is pounce- and the posting's
/// key) that reached rung 2 of the attention ladder, or was opened twice, earns one follow-up on the first touch's channel:
/// at most 60 words, one link to the next rung tagged with the same code, and one question a person can answer with no.
/// The rungs are read from what the site keeps: rung 1 is any open; rung 2 is a review stop past the first, About, the
/// recommendations or the resume; rung 3 is the code on GitHub. The next rung after 1 is the blog, after 2 the record of a
/// real review, and after 3 a call, through the About page. No link is ever the Admin tab. One follow-up per person per
/// role, and a person the tracker shows replied gets none from here.
/// </summary>
public static partial class PounceFollowUp
{
    /// <summary>The folder the one-per-person markers go to.</summary>
    public const string Folder = "nightly/pounce/followups";

    /// <summary>The follow-up pass's log on the share, one line a follow-up found due.</summary>
    public const string Log = "nightly/pounce/followups.log";

    /// <summary>The folder a dry run's report goes to.</summary>
    public const string DryFolder = "nightly/pounce/followup-dry";

    /// <summary>The share's tagged-link list, which opens_report.py alone writes; this only reads it.</summary>
    public const string Tokens = "link-tokens.csv";

    /// <summary>
    /// Where make_ref.py spools each new code until opens_report.py folds it into the list once a day; a code minted an
    /// hour ago is only here, so the pass reads the spool too, and only reads it.
    /// </summary>
    public const string Spool = "inbox/link-tokens";

    /// <summary>The longest a follow-up may be.</summary>
    public const int MaxWords = 60;

    /// <summary>The site, as a note prints it.</summary>
    public const string Site = "theyard.stevenstout.biz";

    /// <summary>The codes minted on pounce notes: rows whose lane is pounce- and digits, test codes (zz) left out.</summary>
    /// <param name="csv">link-tokens.csv's text.</param>
    public static IReadOnlyList<PounceCode> Codes(string csv)
    {
        var rows = TrackerCsvText.Parse(csv);
        if (rows.Count == 0)
        {
            return [];
        }
        var at = rows[0].Select((name, i) => (Name: name.Trim().TrimStart('﻿'), i)).ToDictionary(c => c.Name, c => c.i, StringComparer.Ordinal);
        if (!new[] { "code", "person", "company", "channel", "sent", "lane" }.All(at.ContainsKey))
        {
            return [];
        }
        var codes = new List<PounceCode>();
        foreach (string[] row in rows.Skip(1).Where(r => r.Length == rows[0].Length))
        {
            string code = row[at["code"]].Trim();
            if (code.StartsWith("zz", StringComparison.Ordinal) || PounceLane().Match(row[at["lane"]].Trim()) is not { Success: true } lane
                || !DateTimeOffset.TryParse(row[at["sent"]].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var sent))
            {
                continue;
            }
            codes.Add(new PounceCode(code, row[at["person"]].Trim(), row[at["company"]].Trim(), row[at["channel"]].Trim(), sent, long.Parse(lane.Groups[1].Value, CultureInfo.InvariantCulture)));
        }
        return codes;
    }

    /// <summary>The highest rung a code reached: 3 for the code on GitHub, 2 for a stop past the first or a page past the first look, else 1.</summary>
    /// <param name="visit">What the code did.</param>
    public static int Rung(CodeVisit visit) =>
        visit.Paths.ContainsKey("/api/go/github") ? 3
        : visit.Stop >= 2 || visit.Paths.Keys.Any(p => p is "/about" or "/recommendations" || p.StartsWith("/resume", StringComparison.Ordinal)) ? 2
        : 1;

    /// <summary>True when the code earned its follow-up: rung 2 or more, or two opens.</summary>
    /// <param name="visit">What the code did.</param>
    public static bool Due(CodeVisit visit) => Rung(visit) >= 2 || visit.Opens >= 2;

    /// <summary>The one link a follow-up carries: the rung after the one reached, tagged with the same code, never the Admin tab.</summary>
    /// <param name="rung">The rung reached.</param>
    /// <param name="code">The code.</param>
    public static string NextLink(int rung, string code) => rung switch
    {
        1 => $"{Site}/?view=blog&r={code}",
        2 => $"{Site}/?doc=adr-coderabbit-one-rung-at-a-time&r={code}",
        _ => $"{Site}/about?r={code}",
    };

    /// <summary>The follow-up's text: a thank-you for the rung reached, the next rung's link and one no-oriented question.</summary>
    /// <param name="code">The code and who it went to.</param>
    /// <param name="rung">The rung reached.</param>
    public static string Text(PounceCode code, int rung)
    {
        string link = NextLink(rung, code.Code);
        string hello = FirstName(code.Person) is { } first ? $"{first}, thank you" : "Thank you";
        return rung switch
        {
            1 => $"{hello} for taking a look at the site. The story behind its quality page, how a ratchet keeps code an AI wrote honest, is a 4 minute read: {link}\n\nWould it be a bad idea to hear how your team checks code an AI wrote?\n\nSteve",
            2 => $"{hello} for the time on the site. The next step is a real review: a CodeRabbit finding, my answer and the rule it became, about 8 minutes: {link}\n\nWould it be a bad idea to compare it with how your team reviews pull requests?\n\nSteve",
            _ => $"{hello} for reading the code itself. The next step is a short call on what you have seen; who I am and how to reach me are here: {link}\n\nWould it be a bad idea to find 15 minutes this week?\n\nSteve",
        };
    }

    /// <summary>The words in a text, as the note's limit counts them.</summary>
    /// <param name="text">The text.</param>
    public static int Words(string text) => text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>The first name when the person cell holds a name, or null for the words the program writes when it has none.</summary>
    /// <param name="person">The person cell.</param>
    public static string? FirstName(string person) =>
        Name().IsMatch(person) && !person.StartsWith("Leader at", StringComparison.OrdinalIgnoreCase) ? person.Split(' ')[0] : null;

    /// <summary>
    /// True when the tracker shows the person answered after the code went out: a row for the same name, written since the
    /// code was minted, whose note opens with INBOUND or with REPLY that is not his own (REPLY SENT, REPLY DRAFTED).
    /// </summary>
    /// <param name="touches">The tracker rows written since the code was minted.</param>
    /// <param name="code">The code and who it went to.</param>
    public static bool Replied(IEnumerable<TrackerTouch> touches, PounceCode code)
    {
        string who = Who(code.Person);
        return who.Length > 0 && touches.Any(t => t.WrittenAt >= code.Sent && t.Cells.Count >= 7
            && Who(t.Cells[1]) == who && Answer().IsMatch(t.Cells[6].TrimStart()));
    }

    /// <summary>
    /// The code with the person it reached: its own name when make_ref.py was given one, else the name the box's send row
    /// recorded for it, the tracker row written since the code was minted whose Notes carry the code. Null while no such
    /// row names a person, so an unnamed note waits for its send's row and never stands for a person by its code.
    /// </summary>
    /// <param name="touches">The tracker rows written since the code was minted.</param>
    /// <param name="code">The code and who it went to.</param>
    public static PounceCode? Recipient(IEnumerable<TrackerTouch> touches, PounceCode code)
    {
        if (FirstName(code.Person) is not null)
        {
            return code;
        }
        string named = $"Code {code.Code}.", linked = $"r={code.Code}";
        var row = touches.FirstOrDefault(t => t.WrittenAt >= code.Sent && t.Cells.Count >= 7 && FirstName(t.Cells[1].Trim()) is not null
            && (t.Cells[6].Contains(named, StringComparison.Ordinal) || t.Cells[6].Contains(linked, StringComparison.Ordinal)));
        return row is null ? null : code with { Person = row.Cells[1].Trim() };
    }

    /// <summary>The marker that keeps it to one follow-up per person per role: the role's key and the person's letters, from a resolved recipient.</summary>
    /// <param name="code">The code, its person resolved (Recipient).</param>
    public static string Marker(PounceCode code) => $"{Folder}/{code.Key}-{Who(code.Person)}.md";

    /// <summary>Whole minutes from the code's first open to the instant given.</summary>
    /// <param name="visit">What the code did.</param>
    /// <param name="at">The follow-up's instant.</param>
    public static int Minutes(CodeVisit visit, DateTimeOffset at) => (int)Math.Floor((at - visit.FirstOpen).TotalMinutes);

    /// <summary>One log line: the Central time, the code, who and where, how far it climbed, what the pass did and the minutes from its first open.</summary>
    /// <param name="local">The pass's Central time.</param>
    /// <param name="followUp">The follow-up.</param>
    /// <param name="did">What the pass did with it.</param>
    /// <param name="at">The pass's instant.</param>
    public static string LogLine(DateTime local, FollowUp followUp, string did, DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture, $"{local:yyyy-MM-dd HH:mm:ss} follow-up: {followUp.Code.Code} {followUp.Code.Person}, {followUp.Code.Company} (role {followUp.Code.Key}, {followUp.Code.Channel}) | rung {followUp.Rung}, {followUp.Visit.Opens} opens, stop {followUp.Visit.Stop} | {did} | {Minutes(followUp.Visit, at)} min from the first open");

    private static string Who(string person) => TrackerCsvText.Letters(person).ToLowerInvariant();

    [GeneratedRegex(@"^pounce-(\d{1,18})$")]
    private static partial Regex PounceLane();

    [GeneratedRegex(@"^[\p{L}][\p{L}'\-\.]*(?: [\p{L}][\p{L}'\-\.]*)+$")]
    private static partial Regex Name();

    [GeneratedRegex(@"^(?:INBOUND\b|REPLY\b(?!\s+(?:SENT|DRAFT)))")]
    private static partial Regex Answer();
}
