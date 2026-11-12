using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One person the feed lane found, as the model writes it to candidates.json.</summary>
/// <param name="Person">The leader's name, empty when the invites lane is to find it at send.</param>
/// <param name="Company">The company.</param>
/// <param name="Seat">The seat as read live on the profile this run.</param>
/// <param name="ProfileUrl">The profile's address.</param>
/// <param name="Hot">The bucket, A to E.</param>
/// <param name="Why">Why the company is hot, one line.</param>
/// <param name="CommonPoint">The one thing in common, or "none found".</param>
/// <param name="CommonSource">Where the common point was read.</param>
/// <param name="Band">The RULE 72 pay read: the number, its source and date.</param>
/// <param name="Text">The note or message, with theyard.stevenstout.biz/?r=[code].</param>
/// <param name="Action">invite or message.</param>
public sealed record FeedCandidate(
    [property: JsonPropertyName("person")] string? Person,
    [property: JsonPropertyName("company")] string? Company,
    [property: JsonPropertyName("seat")] string? Seat,
    [property: JsonPropertyName("profile_url")] string? ProfileUrl,
    [property: JsonPropertyName("hot")] string? Hot,
    [property: JsonPropertyName("why")] string? Why,
    [property: JsonPropertyName("common_point")] string? CommonPoint,
    [property: JsonPropertyName("common_source")] string? CommonSource,
    [property: JsonPropertyName("band")] string? Band,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("action")] string? Action);

/// <summary>The people and companies a decision of Steve's holds out of the feed.</summary>
/// <param name="People">Held people's names.</param>
/// <param name="Companies">Held companies: every row there, named or not.</param>
public sealed record FeedHolds(IReadOnlyList<string> People, IReadOnlyList<string> Companies)
{
    /// <summary>Nothing held.</summary>
    public static FeedHolds None { get; } = new([], []);

    /// <summary>True when the person, or anyone at the company, is held.</summary>
    /// <param name="person">The person, empty for an unnamed row.</param>
    /// <param name="company">The company.</param>
    public bool Holds(string person, string company) =>
        People.Any(name => person.Length > 0 && PersonGate.Name(name) == PersonGate.Name(person))
        || Companies.Any(held => ApplicationCap.Same(held, company));
}

/// <summary>Reads linkedin-proof/FEED-HOLD.md: "- Person, Company" holds a person, "- Company: Name" holds a company.</summary>
public static partial class FeedHold
{
    /// <summary>What the file holds.</summary>
    /// <param name="text">The file's text.</param>
    public static FeedHolds Read(string text) => new(
        [.. PersonLine().Matches(text).Select(match => match.Groups[1].Value.Trim()).Where(name => name.Length > 0 && !name.StartsWith("Company:", StringComparison.Ordinal))],
        [.. CompanyLine().Matches(text).Select(match => match.Groups[1].Value.Trim()).Where(name => name.Length > 0)]);

    [GeneratedRegex(@"(?m)^- ([^,\r\n]+),")]
    private static partial Regex PersonLine();

    [GeneratedRegex(@"(?m)^- Company:\s*([^,\r\n]+)")]
    private static partial Regex CompanyLine();
}

/// <summary>What the feed keeps, in queue order, and what it leaves out with the gate's reason.</summary>
/// <param name="Kept">The candidates the queue will hold.</param>
/// <param name="Dropped">The rest.</param>
public sealed record FeedCut(IReadOnlyList<FeedCandidate> Kept, IReadOnlyList<Dropped> Dropped);

/// <summary>
/// Turns the feed lane's candidates into the day's queue. The model only proposes; every candidate runs the gates the
/// invites and messages lanes run (the person gate, the short checks, the closed-req gate), is deduped against the open
/// queue blocks and against itself by name and, for an unnamed row, by company, and is ordered by its bucket and then by
/// the model's own order. The program mints the code at send, so a text must carry [code] and no code of its own. The
/// queue renders in the shape the queue reader takes back whole. No I/O here: the feed lane reads and writes the files.
/// </summary>
public static partial class FeedQueue
{
    /// <summary>The candidates in candidates.json, which holds an array or an object with a "candidates" array.</summary>
    /// <param name="json">The file's text.</param>
    public static IReadOnlyList<FeedCandidate> Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement list = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("candidates", out JsonElement inner) ? inner : document.RootElement;
            return list.ValueKind == JsonValueKind.Array ? list.Deserialize<List<FeedCandidate>>() ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The candidates the gates keep, hottest first, and the rest with their reasons.</summary>
    /// <param name="candidates">The model's candidates, in its order.</param>
    /// <param name="gate">The person and company gates.</param>
    /// <param name="open">The open blocks of every queue already on the share.</param>
    /// <param name="held">People and companies that wait on Steve's yes (FeedHold), never queued by the feed.</param>
    /// <param name="budget">True for a caught role with the budget flag, whose text may ask the timeline question.</param>
    public static FeedCut Cut(IReadOnlyList<FeedCandidate> candidates, PersonGate gate, IReadOnlyList<QueueBlock> open, FeedHolds held, bool budget = false)
    {
        var kept = new List<FeedCandidate>();
        var dropped = new List<Dropped>();
        foreach (FeedCandidate raw in candidates)
        {
            FeedCandidate candidate = Clean(raw);
            string what = candidate.Person!.Length > 0 ? $"{candidate.Person}, {candidate.Company}" : candidate.Company!;
            string? reason = gate.Blacklisted(candidate.Person!, candidate.Company!)
                ?? (held.Holds(candidate.Person!, candidate.Company!)
                    ? "held for Steve's yes (linkedin-proof/FEED-HOLD.md)"
                    : Reason(candidate, gate, open, kept, budget));
            if (reason is null)
            {
                kept.Add(candidate);
            }
            else
            {
                dropped.Add(new Dropped(what.Length > 0 ? what : "a candidate with no company", reason));
            }
        }
        return new FeedCut([.. kept.Select((candidate, index) => (candidate, index)).OrderBy(pair => pair.candidate.Hot).ThenBy(pair => pair.index).Select(pair => pair.candidate)], dropped);
    }

    /// <summary>The queue file: the rules line, the day, then one block per kept candidate.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="written">When it was written, Central.</param>
    /// <param name="runFolder">The feed's run folder, named in the header.</param>
    /// <param name="kept">The kept candidates, in queue order.</param>
    /// <param name="role">For a caught role, its queue's name and title words; null for the morning's feed.</param>
    /// <param name="budget">True when every block carries the budget line.</param>
    public static string Render(DateOnly day, DateTime written, string runFolder, IReadOnlyList<FeedCandidate> kept, string? role = null, bool budget = false)
    {
        var queue = new StringBuilder();
        queue.AppendLine("> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).");
        queue.AppendLine();
        queue.AppendLine(role is null ? string.Create(CultureInfo.InvariantCulture, $"# QUEUE-hotmgr-{day:yyyy-MM-dd}: the hot-manager feed, {day:ddd M/d}") : $"# {role}");
        queue.AppendLine();
        queue.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Written {written:HH:mm} Central by JobHunt.Server's feed lane from {runFolder}/candidates.json. Every block passed the person gate, the short checks, the closed-req gate and check_aiisms.py before it was written; the program mints each code at send. Hottest first."));
        for (int i = 0; i < kept.Count; i++)
        {
            FeedCandidate block = kept[i];
            string who = block.Person!.Length > 0 ? $"{block.Person}, {block.Company}" : block.Company!;
            queue.AppendLine();
            queue.AppendLine(block.Action == "message" ? $"### {i + 1}. {who} ({block.Seat}): MESSAGE" : $"### {i + 1}. {who} ({block.Seat}). Code [code].");
            queue.AppendLine($"- Seat: {block.Seat}");
            if (block.ProfileUrl!.Length > 0)
            {
                queue.AppendLine($"- Profile: {block.ProfileUrl}");
            }
            queue.AppendLine($"- Hot: {block.Hot}");
            queue.AppendLine($"- Why: {block.Why}");
            queue.AppendLine($"- Common point: {block.CommonPoint} ({block.CommonSource})");
            queue.AppendLine($"- Band: {block.Band}");
            if (budget)
            {
                queue.AppendLine("- Budget: yes");
            }
            queue.AppendLine();
            queue.AppendLine("```");
            queue.AppendLine(block.Text);
            queue.AppendLine("```");
        }
        return queue.ToString();
    }

    private static string? Reason(FeedCandidate candidate, PersonGate gate, IReadOnlyList<QueueBlock> open, List<FeedCandidate> kept, bool budget)
    {
        bool invite = candidate.Action == "invite";
        string person = candidate.Person!, company = candidate.Company!, text = candidate.Text!;
        if (company.Length == 0)
        {
            return "no company";
        }
        if (!invite && candidate.Action != "message")
        {
            return $"action \"{candidate.Action}\" is neither invite nor message";
        }
        if (candidate.Seat!.Length == 0)
        {
            return "no seat read live this run (Rule 68)";
        }
        if (!invite && (person.Length == 0 || candidate.ProfileUrl!.Length == 0))
        {
            return "a message needs the person and the profile address";
        }
        if (TextCheck.Code(text).Length > 0 || !text.Contains("?r=[code]", StringComparison.Ordinal))
        {
            return "the text must carry ?r=[code]; the program mints the code at send";
        }
        if (person.Length > 0 && gate.OnAnyRow(person))
        {
            return "already on a tracker row, at this company or another: one touch per person (Rule 25)";
        }
        if (ClosedReqs.Mentions(text))
        {
            return "the text speaks of applying; a note to the reporting line says seat, hire or posting";
        }
        string? gated = invite ? gate.Invite(person, company) : gate.Message(person, company);
        if (gated is not null)
        {
            return gated;
        }
        var problems = TextCheck.Problems(text, invite, budget);
        if (problems.Count > 0)
        {
            return "text failed: " + string.Join(", ", problems);
        }
        if (gate.ClosedReq(company, text) is { } closed)
        {
            return closed;
        }
        if (open.FirstOrDefault(block => Same(block.Person, block.Company, person, company)) is { } queued)
        {
            return $"already in an open queue block ({queued.Source})";
        }
        return kept.Any(other => Same(other.Person!, other.Company!, person, company)) ? "already on this feed" : null;
    }

    private static bool Same(string person, string company, string otherPerson, string otherCompany) =>
        person.Length > 0 && otherPerson.Length > 0
            ? PersonGate.Name(person) == PersonGate.Name(otherPerson)
            : ApplicationCap.Same(company, otherCompany);

    private static FeedCandidate Clean(FeedCandidate raw)
    {
        string hot = (raw.Hot ?? string.Empty).Trim().ToUpperInvariant();
        return new FeedCandidate(
            Name(Suffix().Replace(raw.Person ?? string.Empty, string.Empty)),
            Name(raw.Company),
            Name(raw.Seat),
            Profile().Match(raw.ProfileUrl?.Trim() ?? string.Empty) is { Success: true } profile ? profile.Groups["page"].Value : string.Empty,
            hot.Length == 1 && "ABCDE".Contains(hot, StringComparison.Ordinal) ? hot : "E",
            Line(raw.Why),
            Line(raw.CommonPoint) is { Length: > 0 } point ? point : "none found",
            Line(raw.CommonSource),
            Line(raw.Band) is { Length: > 0 } band ? band : "not read",
            (raw.Text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Trim(),
            (raw.Action ?? string.Empty).Trim().ToLowerInvariant());
    }

    private static string Name(string? cell) => Space().Replace(Heading().Replace(cell ?? string.Empty, " "), " ").Trim();

    private static string Line(string? cell) => Space().Replace(cell ?? string.Empty, " ").Trim();

    [GeneratedRegex(@"[,:()\[\]#`|]")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    [GeneratedRegex(@"^(?<page>https://www\.linkedin\.com/in/[\w%-]+/?)(?:[?#].*)?$")]
    private static partial Regex Profile();

    [GeneratedRegex(@"\s*\([^)]*\)|,.*$")]
    private static partial Regex Suffix();
}
