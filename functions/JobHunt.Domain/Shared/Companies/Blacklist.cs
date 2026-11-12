using System.Text.RegularExpressions;

namespace JobHunt.Domain.Shared.Companies;

/// <summary>
/// BLACKLIST.md as rules, the one reader every gate that honours the file asks: the night's picker, the leads filter,
/// the pounce and the 6 AM program's worklists. Each table row is one of three kinds, read from its Person cell. A cell
/// that opens "ALL OF" blocks a company: the Company cell up to its first parenthesis or comma, and every name the cell
/// says it includes ("includes Petvisor and Vetstoria"). A cell that says "JOB ONLY" or "job-level" blocks one posting:
/// the job ids (six digits or more) and posting addresses in its Address cell, and its company stays open. Any other
/// cell blocks one person by name, and that person's company stays open. Company names match as RULE 50 matches them
/// (ApplicationCap.Same); a person's name matches with case, punctuation and anything in parentheses set aside.
/// </summary>
public sealed partial class Blacklist
{
    /// <summary>The file on the share.</summary>
    public const string File = "BLACKLIST.md";

    private Blacklist(IReadOnlyList<BlacklistRow> rows) => Rows = rows;

    /// <summary>A list with no rows.</summary>
    public static Blacklist None { get; } = new([]);

    /// <summary>The rows, in the file's order.</summary>
    public IReadOnlyList<BlacklistRow> Rows { get; }

    /// <summary>The company names the company rows block, each once, in the file's order.</summary>
    public IReadOnlyList<string> Companies =>
        [.. Rows.Where(row => row.Kind == BlacklistKind.Company).SelectMany(row => row.Names).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The rows of the file's table: a row whose Added cell does not open with a date (the header, the rule) is not one.</summary>
    /// <param name="file">BLACKLIST.md's text.</param>
    public static Blacklist Read(string file)
    {
        var rows = new List<BlacklistRow>();
        foreach (string line in (file ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('|')))
        {
            string[] cells = [.. line.Trim('|').Split('|').Select(cell => cell.Trim())];
            if (cells.Length < 4 || Added().Match(cells[0]) is not { Success: true } added)
            {
                continue;
            }
            string person = cells[1], address = cells[2], company = cells[3];
            if (person.StartsWith("ALL OF ", StringComparison.Ordinal))
            {
                var names = new List<string> { company.Split('(', ',')[0].Trim() };
                if (Includes().Match(person) is { Success: true } includes)
                {
                    names.AddRange(Splitter().Split(includes.Groups[1].Value).Select(n => n.Trim()));
                }
                rows.Add(new BlacklistRow(added.Value, BlacklistKind.Company, [.. names.Where(n => n.Length > 0)], []));
            }
            else if (JobOnly().IsMatch(person))
            {
                rows.Add(new BlacklistRow(added.Value, BlacklistKind.Job, [company.Split('(', ',')[0].Trim()],
                    [.. JobId().Matches(address).Select(m => m.Value), .. Posting().Matches(address).Select(m => Bare(m.Value))]));
            }
            else if (Person(person).Length > 0)
            {
                rows.Add(new BlacklistRow(added.Value, BlacklistKind.Person, [person], []));
            }
        }
        return new Blacklist(rows);
    }

    /// <summary>
    /// The first row that blocks this company, this person or this posting, or null: a company row naming the company,
    /// a person row naming the person, or a job row whose job id or posting address the address carries.
    /// </summary>
    /// <param name="company">The company.</param>
    /// <param name="person">The person, empty for none.</param>
    /// <param name="url">The posting's address, empty for none.</param>
    public BlacklistRow? Hit(string company, string person = "", string url = "")
    {
        string name = Person(person ?? string.Empty);
        string bare = Bare(url ?? string.Empty);
        var ids = JobId().Matches(url ?? string.Empty).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        return Rows.FirstOrDefault(row => row.Kind switch
        {
            BlacklistKind.Company => (company ?? string.Empty).Length > 0 && row.Names.Any(n => ApplicationCap.Same(n, company!)),
            BlacklistKind.Person => name.Length > 0 && row.Names.Any(n => Person(n) == name),
            _ => bare.Length > 0 && row.Jobs.Any(job => ids.Contains(job) || (job.Contains('/', StringComparison.Ordinal) && bare.StartsWith(job, StringComparison.OrdinalIgnoreCase))),
        });
    }

    private static string Person(string cell) =>
        Space().Replace(NotLetter().Replace(Parenthesis().Replace(cell, " ").ToLowerInvariant(), " "), " ").Trim();

    private static string Bare(string address) => Scheme().Replace(address.Trim(), string.Empty).TrimEnd('/', '.', ',', ';');

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex Added();

    [GeneratedRegex(@"includes ([^;)]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Includes();

    [GeneratedRegex(@",|\band\b")]
    private static partial Regex Splitter();

    [GeneratedRegex(@"\bJOB ONLY\b|\bjob-level\b", RegexOptions.IgnoreCase)]
    private static partial Regex JobOnly();

    [GeneratedRegex(@"(?<!\d)\d{6,}(?!\d)")]
    private static partial Regex JobId();

    [GeneratedRegex(@"(https?://)?[a-z0-9.-]+\.[a-z]{2,}/\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Posting();

    [GeneratedRegex(@"^(https?://)?(www\.)?", RegexOptions.IgnoreCase)]
    private static partial Regex Scheme();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesis();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotLetter();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}

/// <summary>What a row blocks: one person, a whole company, or one posting.</summary>
public enum BlacklistKind
{
    /// <summary>One person, at any address.</summary>
    Person,

    /// <summary>A company and the names it includes.</summary>
    Company,

    /// <summary>One posting; the company stays open.</summary>
    Job,
}

/// <summary>One row of BLACKLIST.md.</summary>
/// <param name="Added">The day Steve added it, yyyy-MM-dd.</param>
/// <param name="Kind">What it blocks.</param>
/// <param name="Names">The person's name, or the company's names; a job row's company, which it does not block.</param>
/// <param name="Jobs">A job row's job ids and posting addresses, scheme and www. set aside.</param>
public sealed record BlacklistRow(string Added, BlacklistKind Kind, IReadOnlyList<string> Names, IReadOnlyList<string> Jobs)
{
    /// <summary>The reason a gate writes for a hit.</summary>
    public string Reason => $"blacklisted: {Added}";
}
