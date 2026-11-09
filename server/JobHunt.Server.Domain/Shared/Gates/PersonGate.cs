using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Gates;

/// <summary>
/// The company and person gates a worklist row must pass: the company blocks, the do-not-apply list, Rule 25, Rule 50,
/// the agency and former-employer skips and the people no template ever goes to. Each answer is the reason a row is
/// out, or null, so the DONE note can say why in the gate's own words.
/// </summary>
public sealed partial class PersonGate
{
    private const int LiveProcessDays = 30;
    private const int RecentDays = 30;

    private readonly IReadOnlyList<string[]> rows;
    private readonly IReadOnlyList<string> blocks;
    private readonly IReadOnlyList<DoNotApply> doNotApply;
    private readonly IReadOnlyList<string> agencies;
    private readonly IReadOnlySet<string> noTemplate;
    private readonly DateOnly today;

    /// <summary>A gate over one day's reads.</summary>
    /// <param name="sources">What the gate reads.</param>
    /// <param name="today">The Central day.</param>
    public PersonGate(GateSources sources, DateOnly today)
    {
        rows = sources.Rows;
        blocks = CompanyBlocks(sources.Limits);
        doNotApply = ApplicationCap.DoNotApplyList(sources.Rules);
        agencies = sources.Agencies;
        noTemplate = NoTemplate(sources.Responders);
        this.today = today;
    }

    /// <summary>The employers under a company block in outreach-limits.md.</summary>
    /// <param name="limits">outreach-limits.md.</param>
    public static IReadOnlyList<string> CompanyBlocks(string limits)
    {
        int at = limits.IndexOf("## COMPANY BLOCKS", StringComparison.Ordinal);
        if (at < 0)
        {
            throw new FormatException("outreach-limits.md has no COMPANY BLOCKS section");
        }
        int end = limits.IndexOf("\n## ", at + 3, StringComparison.Ordinal);
        string section = end < 0 ? limits[at..] : limits[at..end];
        return [.. Block().Matches(section).Select(m => m.Groups[1].Value.Trim())];
    }

    /// <summary>
    /// The people in responders.md's two no-template tables, struck-through rows included: a person whose row closed
    /// is still nobody a template goes to.
    /// </summary>
    /// <param name="responders">responders.md.</param>
    public static IReadOnlySet<string> NoTemplate(string responders)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string heading in new[] { "## Live conversations", "## Never send template outreach" })
        {
            int at = responders.IndexOf(heading, StringComparison.Ordinal);
            if (at < 0)
            {
                throw new FormatException($"responders.md has no section {heading}");
            }
            foreach (string line in responders[at..].Split('\n').Skip(1).SkipWhile(l => !l.StartsWith('|')).TakeWhile(l => l.StartsWith('|')).Skip(2))
            {
                string name = Parenthesis().Replace(line.Trim('|').Split('|')[0], string.Empty).Replace("*", string.Empty).Replace("~", string.Empty).Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
        }
        return names;
    }

    /// <summary>Why this person may not get an invite, or null.</summary>
    /// <param name="person">The person, empty when the lane will find the name.</param>
    /// <param name="company">The company.</param>
    public string? Invite(string person, string company) =>
        Outreach(person, company) ?? (Touched(person, TrackerKinds.IsInvite) ? "already invited" : Unnamed(person, company));

    /// <summary>Why this person may not get a message, or null.</summary>
    /// <param name="person">The person.</param>
    /// <param name="company">The company.</param>
    public string? Message(string person, string company) =>
        Outreach(person, company) ?? (Touched(person, TrackerKinds.IsMessage) ? "Rule 25: already messaged" : null);

    /// <summary>Why this text may not go because the application it speaks of is over or not on file, or null.</summary>
    /// <param name="company">The company.</param>
    /// <param name="text">The drafted text.</param>
    public string? ClosedReq(string company, string text) => ClosedReqs.Why(company, text, rows);

    /// <summary>Why this company may not get an application, or null.</summary>
    /// <param name="company">The company.</param>
    /// <param name="link">The posting's link, to catch the same posting applied to already.</param>
    /// <param name="role">The role's title, for an application row that holds no link.</param>
    public string? Apply(string company, string link, string role)
    {
        string? employer = Employer(company);
        if (employer is not null)
        {
            return employer;
        }
        if (LooksLikeStaffing().IsMatch(company))
        {
            return "skipped: staffing firm";
        }
        CapVerdict verdict = ApplicationCap.Check(company, rows, doNotApply, today);
        if (!verdict.Allowed)
        {
            return "Rule 50: " + verdict.Why;
        }
        return Applied(company, link, role) ? "already applied" : null;
    }

    /// <summary>The people the tracker has on any row at this company, for a lane that picks a name itself.</summary>
    /// <param name="company">The company.</param>
    public IReadOnlyList<string> KnownAt(string company) =>
        [.. rows.Where(row => row.Length >= 7 && ApplicationCap.Same(row[2], company) && Name(row[1]).Length > 0 && Name(row[1]) != "na" && !ApplicationCap.Same(row[1], company))
            .Select(row => row[1].Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The company the tracker has for a person, empty when it has none.</summary>
    /// <param name="person">The person.</param>
    public string CompanyOf(string person) =>
        rows.Where(row => row.Length >= 7 && TrackerKinds.IsLinkedIn(row) && Name(row[1]) == Name(person)).Select(row => row[2].Trim()).LastOrDefault() ?? string.Empty;

    private string? Outreach(string person, string company)
    {
        if (Employer(company) is { } employer)
        {
            return employer;
        }
        if (person.Length > 0 && noTemplate.Contains(person))
        {
            return "responders.md: no template";
        }
        return LiveProcess(company) ? "live process at the company" : null;
    }

    private string? Employer(string company)
    {
        if (blocks.Any(block => ApplicationCap.Same(block, company)))
        {
            return "company block";
        }
        if (agencies.Any(agency => ApplicationCap.Same(agency, company)))
        {
            return "agency-submitted";
        }
        return FormerEmployers.Of(company, null) is { } former ? $"former employer: {former.Name}" : null;
    }

    private bool LiveProcess(string company) =>
        rows.Any(row => row.Length >= 7 && ApplicationCap.Same(row[2], company)
            && TrackerKinds.Day(row) is { } day && today.DayNumber - day.DayNumber is >= 0 and < LiveProcessDays
            && (Interview().IsMatch(row[3]) || InterviewNote().IsMatch(row[6])));

    private bool Touched(string person, Func<string[], bool> kind)
    {
        string name = Name(person);
        return name.Length > 0 && rows.Any(row => row.Length >= 7 && kind(row)
            && (Name(row[1]) == name || Name(row[1]).EndsWith(" " + name, StringComparison.Ordinal) || row[6].Contains(person.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private string? Unnamed(string person, string company) =>
        person.Length == 0 && rows.Any(row => row.Length >= 7 && TrackerKinds.IsInvite(row) && ApplicationCap.Same(row[2], company)
            && TrackerKinds.Day(row) is { } day && today.DayNumber - day.DayNumber is >= 0 and < RecentDays)
            ? $"already invited someone there within {RecentDays} days"
            : null;

    private bool Applied(string company, string link, string role)
    {
        string id = PostingId().Matches(link).Select(m => m.Value).LastOrDefault() ?? link.TrimEnd('/');
        string title = ClosedReqs.Normal(TitleEnd().Split(role)[0]);
        return rows.Any(row => row.Length >= 7 && ((id.Length > 0 && row[6].Contains(id, StringComparison.OrdinalIgnoreCase))
            || (title.Length > 0 && (TrackerRows.IsApplication(row) || !TrackerKinds.IsLinkedIn(row)) && ApplicationCap.Same(row[2], company) && $" {ClosedReqs.Normal(row[2] + " " + row[6])} ".Contains($" {title} ", StringComparison.Ordinal))));
    }

    /// <summary>A person's name however the tracker wrote it: no title, no parenthesis, no initial, nothing after a comma.</summary>
    /// <param name="cell">A Person cell.</param>
    public static string Name(string cell) =>
        Space().Replace(Initial().Replace(Title().Replace(Parenthesis().Replace(cell.Split(',')[0], string.Empty), string.Empty), " "), " ").Trim().ToLowerInvariant();

    [GeneratedRegex(@"(?m)^\|\s*\*\*([^*|]+)\*\*\s*\|")]
    private static partial Regex Block();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    [GeneratedRegex(@"\s+[-|]\s+")]
    private static partial Regex TitleEnd();

    [GeneratedRegex(@"\s[A-Za-z]\.?\s")]
    private static partial Regex Initial();

    [GeneratedRegex(@"^\s*(Dr|Mr|Mrs|Ms)\.?\s+", RegexOptions.IgnoreCase)]
    private static partial Regex Title();

    [GeneratedRegex(@"\d{6,}|[0-9a-f]{8}-[0-9a-f-]{27}", RegexOptions.IgnoreCase)]
    private static partial Regex PostingId();

    [GeneratedRegex(@"^(Interview|Video Interview|Screening Call|Calendly)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Interview();

    [GeneratedRegex(@"^\W*(INTERVIEW (BOOKED|CONFIRMED|SCHEDULED)|ROUND \d)", RegexOptions.IgnoreCase)]
    private static partial Regex InterviewNote();

    [GeneratedRegex(@"\b(staffing|consulting|recruit(ing|ers?|ment)|talent)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeStaffing();
}

/// <summary>What the gates read, one day's worth.</summary>
/// <param name="Rows">Tracker rows, master and spool, seven cells each.</param>
/// <param name="Limits">outreach-limits.md.</param>
/// <param name="Rules">systemRules.md, for the do-not-apply list.</param>
/// <param name="Responders">responders.md, read only.</param>
/// <param name="Agencies">The agency-submitted names out of the queues.</param>
public sealed record GateSources(IReadOnlyList<string[]> Rows, string Limits, string Rules, string Responders, IReadOnlyList<string> Agencies);
