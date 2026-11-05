using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>
/// One role the night's picker weighs: a posting the board scan read from the employer's own board tonight
/// (Confirmed "feed") or a requisition from a job alert (Confirmed "alert", which the jobs lane confirms on the
/// employer's board or leaves unconfirmed). Posted is the day the board says it went up, or the day it was first
/// seen when the board says nothing. The band is whole dollars a year, or null when none was published.
/// </summary>
/// <param name="Source">Where it came from: "feed:greenhouse", "alert:LinkedIn digest" and the like.</param>
/// <param name="Company">The company as its source names it.</param>
/// <param name="Title">The title as written.</param>
/// <param name="Url">The address the source gave.</param>
/// <param name="BoardKind">greenhouse, lever, ashby, or the alert's board.</param>
/// <param name="Posted">Posted, or first seen.</param>
/// <param name="Location">The location as written.</param>
/// <param name="RemoteStance">Remote, Hybrid, Onsite or Unknown.</param>
/// <param name="BandMax">The band's top a year, or null.</param>
/// <param name="Confirmed">feed or alert.</param>
/// <param name="Text">The description's text, empty for an alert.</param>
public sealed record Candidate(string Source, string Company, string Title, string Url, string BoardKind, DateOnly? Posted, string Location, string RemoteStance, int? BandMax, string Confirmed, string Text)
{
    /// <summary>The band's low end a year, or null.</summary>
    public int? BandMin { get; init; }

    /// <summary>The level the title names.</summary>
    public string Level => PostingLevel.Of(Title);
}

/// <summary>A candidate as the gates left it: the gate it failed (null when it passed) and what each gate read.</summary>
/// <param name="Candidate">The role.</param>
/// <param name="Failed">The gate it failed and the number, or null.</param>
/// <param name="BandFlag">PASS, FLAG, SKIP or NONE (PayFloor).</param>
/// <param name="Remote">What the remote gate read: remote, St. Louis hybrid 1 day, hybrid days unknown.</param>
/// <param name="Stack">dotnet, azure, ai lead, or unread for an alert with no text.</param>
/// <param name="TrackerNote">The company's applications in 60 days by Count, or "no applications in 60 days".</param>
/// <param name="DaysOld">Days since posted or first seen.</param>
public sealed record Verdict(Candidate Candidate, string? Failed, string BandFlag, string Remote, string Stack, string TrackerNote, int DaysOld)
{
    /// <summary>The gates it passed, in one line, for the candidates file's WhyPassed column.</summary>
    public string WhyPassed => Failed is null ? $"{Remote}; {Candidate.Level}; {Stack}; band {BandFlag}; {DaysOld} days" : "";
}

/// <summary>
/// The night's deterministic gates (PROMPT-JOBHUNT-NIGHTLY-JOBS rung 3 and decisions D5 to D9), in code so the model
/// only judges: not a staffing firm placing an unnamed client and not a former employer; remote, or St. Louis
/// hybrid at one day a week at most (hybrid with no day count passes to Steve's table); posted or first seen 21
/// days ago or less; Principal, Staff, Lead, Architect, an engineering Manager or Director (a plain Senior only
/// with a band topping PayFloor.Pass); a band whose top is under PayFloor.Flag skipped with its number; a stack
/// that touches C#, .NET or Azure, or an AI-native lead role; not applied to the same role; and under RULE 50's
/// three applications a company in 60 days. Every failure names its gate, so nothing is dropped unseen.
/// </summary>
public static partial class JobsGates
{
    /// <summary>The oldest a role may be, in days since posted or first seen.</summary>
    public const int MaxDays = 21;

    /// <summary>The levels that pass on the title alone.</summary>
    public static readonly IReadOnlyList<string> Levels = [PostingLevel.Principal, PostingLevel.Staff, PostingLevel.Lead, PostingLevel.Architect, PostingLevel.Manager, PostingLevel.Director];

    /// <summary>Every gate in order; the first failure is the one written.</summary>
    public static Verdict Judge(Candidate c, IReadOnlyList<string[]> master, IReadOnlyList<DoNotApply> list, DateOnly today)
    {
        int days = c.Posted is { } posted ? Math.Max(0, today.DayNumber - posted.DayNumber) : 0;
        string flag = PayFloor.Judge(c.BandMax);
        string remote = RemoteRead(c);
        string stack = StackRead(c);
        var applications = master.Where(TrackerRows.IsApplication).ToList();
        var recent = ApplicationCap.Applications(c.Company, master, today);
        string note = recent.Count == 0 ? "no applications in 60 days" : $"{recent.Count} in 60 days (Counts {string.Join(", ", recent.Select(r => r.Count))})";
        string? failed = Staffing().IsMatch(c.Company) ? "staffing firm placing an unnamed client"
            : FormerEmployers.Of(c.Company, null) is not null ? "former employer"
            : remote.StartsWith("not remote", StringComparison.Ordinal) || remote.StartsWith("hybrid outside", StringComparison.Ordinal) || remote == "onsite" ? remote
            : days > MaxDays ? $"posted {days} days ago"
            : LevelFailure(c)
            ?? (flag == "SKIP" ? $"band tops at ${c.BandMax!.Value.ToString("N0", CultureInfo.InvariantCulture)}, under ${PayFloor.Flag.ToString("N0", CultureInfo.InvariantCulture)}" : null)
            ?? (stack == "none" ? "stack names no C#, .NET or Azure and is not an AI-native lead role" : null)
            ?? Applied(c, applications)
            ?? (ApplicationCap.Check(c.Company, master, list, today) is { Allowed: false } cap ? "RULE 50: " + cap.Why : null);
        return new Verdict(c, failed, flag, remote, stack, note, days);
    }

    /// <summary>What the remote gate reads (D7).</summary>
    public static string RemoteRead(Candidate c)
    {
        string where = c.Location ?? string.Empty;
        bool stl = StLouis().IsMatch(where);
        bool oneDay = OneDay().IsMatch(c.Text) || OneDay().IsMatch(where);
        if (c.RemoteStance == "Remote" || RemoteWord().IsMatch(where) || Country().IsMatch(where.Trim()) || RemoteWord().IsMatch(c.Title))
        {
            return "remote";
        }
        if (stl)
        {
            return oneDay ? "St. Louis hybrid, 1 day" : "hybrid, days unknown";
        }
        return c.RemoteStance switch
        {
            "Hybrid" => "hybrid outside St. Louis: " + where,
            "Onsite" => "onsite",
            _ => "not remote: " + where,
        };
    }

    /// <summary>What the stack gate reads: dotnet, azure, ai lead, unread (an alert with no text) or none.</summary>
    public static string StackRead(Candidate c)
    {
        string text = c.Title + " " + c.Text;
        if (DotNet().IsMatch(text)) { return "dotnet"; }
        if (Azure().IsMatch(text)) { return "azure"; }
        if (Ai().IsMatch(text) && Levels.Contains(c.Level)) { return "ai lead"; }
        return c.Text.Length == 0 ? "unread" : "none";
    }

    private static string? LevelFailure(Candidate c) => c.Level switch
    {
        PostingLevel.Manager or PostingLevel.Director when !Engineering().IsMatch(c.Title) => $"level {c.Level}, not an engineering title",
        var level when Levels.Contains(level) => null,
        PostingLevel.Senior when c.BandMax is null => "senior, no band",
        PostingLevel.Senior when c.BandMax < PayFloor.Pass => $"senior, band tops at ${c.BandMax.Value.ToString("N0", CultureInfo.InvariantCulture)}",
        PostingLevel.Senior => null,
        var level => $"level {level}",
    };

    private static string? Applied(Candidate c, List<string[]> applications)
    {
        var row = applications.FirstOrDefault(r => ApplicationCap.Same(r[2], c.Company)
            && (r[6].Contains(c.Title, StringComparison.OrdinalIgnoreCase) || (c.Url.Length > 0 && r[6].Contains(c.Url, StringComparison.OrdinalIgnoreCase))));
        return row is null ? null : $"applied already, Count {row[0]}";
    }

    [GeneratedRegex(@"\b(staffing|recruit(ing|ment|ers)?|talent (solutions|marketplace|partners)|kforce|tecla|yourcode|robert half|teksystems|insight global|randstad|apex systems|motion recruitment|jobot|cybercoders|toptal|actalent|aerotek|experis|manpower|lensa)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Staffing();

    [GeneratedRegex(@"\b(st\.? ?louis|saint louis|stl|clayton|chesterfield|creve coeur|maryland heights|town and country|berkeley, mo|o'fallon, mo|st\.? charles)\b|\b63\d{3}\b", RegexOptions.IgnoreCase)]
    private static partial Regex StLouis();

    [GeneratedRegex(@"\b(1|one) day (a|per) week\b|\bonce a week\b|\b1 day in office\b", RegexOptions.IgnoreCase)]
    private static partial Regex OneDay();

    [GeneratedRegex(@"\b(remote|anywhere)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteWord();

    [GeneratedRegex(@"^(United States( of America)?|US|USA|U\.S\.|Anywhere in (the )?(US|USA|United States))$", RegexOptions.IgnoreCase)]
    private static partial Regex Country();

    [GeneratedRegex(@"(?<![A-Za-z])(C#|\.NET|dotnet|ASP\.NET|C Sharp)(?![A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex DotNet();

    [GeneratedRegex(@"\bAzure\b", RegexOptions.IgnoreCase)]
    private static partial Regex Azure();

    [GeneratedRegex(@"\b(AI|ML|LLMs?|(?i:agentic|generative|machine learning))\b")]
    private static partial Regex Ai();

    [GeneratedRegex(@"\b(engineering|software|development|technology|platform|technical)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Engineering();
}
