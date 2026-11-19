using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One comment the roll picked, with its line written and its code.</summary>
/// <param name="Row">The edit's number, which the model's result answers by.</param>
/// <param name="Comment">The comment as read.</param>
/// <param name="Code">The link code make_ref.py minted for it.</param>
/// <param name="Line">The one line that goes under his words.</param>
public sealed record CommentEdit(int Row, OwnComment Comment, string Code, string Line);

/// <summary>What the cut and the roll did to a day's comments: the ones picked, in the order read, and every other with why.</summary>
/// <param name="Picked">The comments picked, at most the cap.</param>
/// <param name="Dropped">The rest, each with the rule that left it out or the roll that passed it by.</param>
/// <param name="Eligible">How many the skip rules let through to the roll.</param>
public sealed record CommentRoll(IReadOnlyList<OwnComment> Picked, IReadOnlyList<Dropped> Dropped, int Eligible);

/// <summary>
/// The comment-line lane's program half: it reads the model's comments.json, holds each comment to <see cref="CommentSkips"/>,
/// rolls about one in three of the rest, writes the line and checks the page's read-back. The roll is the program's, never
/// the model's: of n eligible comments it picks n / 3 rounded down, and one more with a chance of the remainder in three,
/// so the expected share is a third exactly at any n, and which ones is a shuffle. At most <see cref="Cap"/> a day.
/// </summary>
public static partial class CommentPicks
{
    /// <summary>The most edits one day.</summary>
    public const int Cap = 15;

    /// <summary>The site's review route the line links to, the code on the end.</summary>
    public const string Link = "https://theyard.stevenstout.biz/?view=review&r=";

    /// <summary>The edit run's worklist, in the run folder.</summary>
    public const string EditFile = "edits.tsv";

    /// <summary>The read run's comments, in the run folder.</summary>
    public const string ReadFile = "comments.json";

    private const string Opening = "I've got a portfolio that ";

    /// <summary>How far back a run reads, in hours: the last day live, and the last seven days on a dry run, which edits nothing.</summary>
    /// <param name="dry">True for a dry run.</param>
    public static int Window(bool dry) => dry ? 7 * 24 : 24;

    /// <summary>The comments in the model's file, empty when it does not parse.</summary>
    /// <param name="json">comments.json.</param>
    public static IReadOnlyList<OwnComment> Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement list = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("comments", out JsonElement inner) ? inner : document.RootElement;
            return list.ValueKind == JsonValueKind.Array ? list.Deserialize<List<OwnComment>>() ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The skip rules, then the roll.</summary>
    /// <param name="comments">The comments as read, newest first.</param>
    /// <param name="hours">How far back the run reads.</param>
    /// <param name="roll">The random source; the morning's is the system's, a test's is seeded.</param>
    /// <param name="cap">The most picks.</param>
    public static CommentRoll Roll(IReadOnlyList<OwnComment> comments, int hours, Random roll, int cap = Cap)
    {
        var eligible = new List<OwnComment>();
        var dropped = new List<Dropped>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OwnComment comment in comments)
        {
            string? why = !seen.Add($"{comment.PostAuthor}\n{Flat(comment.Text ?? string.Empty)}") ? "the same comment twice" : CommentSkips.Why(comment, hours);
            if (why is null)
            {
                eligible.Add(comment);
            }
            else
            {
                dropped.Add(new Dropped(Who(comment), why));
            }
        }
        int take = Math.Min(cap, (eligible.Count / 3) + (roll.Next(3) < eligible.Count % 3 ? 1 : 0));
        int[] order = [.. Enumerable.Range(0, eligible.Count)];
        roll.Shuffle(order);
        var chosen = order.Take(take).ToHashSet();
        string passed = string.Create(CultureInfo.InvariantCulture, $"not picked by the roll ({take} of {eligible.Count})");
        dropped.AddRange(eligible.Where((_, i) => !chosen.Contains(i)).Select(comment => new Dropped(Who(comment), passed)));
        return new CommentRoll([.. eligible.Where((_, i) => chosen.Contains(i))], dropped, eligible.Count);
    }

    /// <summary>The tie as one plain clause, or null when it is not one: no link, no line break, no dash, no question, at most 140 characters.</summary>
    /// <param name="tie">The model's tie.</param>
    public static string? Clause(string? tie)
    {
        string clause = (tie ?? string.Empty).Trim();
        if (clause.StartsWith(Opening, StringComparison.OrdinalIgnoreCase))
        {
            clause = clause[Opening.Length..];
        }
        clause = clause.TrimEnd('.', ',', ';', ':', '!', ' ');
        if (clause.Length is < 8 or > 140 || NotPlain().IsMatch(clause))
        {
            return null;
        }
        return char.IsUpper(clause[0]) && clause.Length > 1 && char.IsLower(clause[1]) ? char.ToLowerInvariant(clause[0]) + clause[1..] : clause;
    }

    /// <summary>The line that goes under his words.</summary>
    /// <param name="tie">The model's tie, which <see cref="Clause"/> passed.</param>
    /// <param name="code">The link code.</param>
    public static string Line(string tie, string code) => $"{Opening}{Clause(tie)}, worth a look: {Link}{code}";

    /// <summary>What the page's read-back lacks, empty when the edit stands: his words as they were, then the line once, last, and one Yard link.</summary>
    /// <param name="edit">The edit.</param>
    /// <param name="readBack">The comment's whole text as read after the reload.</param>
    public static IReadOnlyList<string> Missing(CommentEdit edit, string? readBack)
    {
        if (string.IsNullOrWhiteSpace(readBack))
        {
            return ["no read-back after the reload"];
        }
        string page = Flat(readBack), line = Flat(edit.Line);
        var missing = new List<string>();
        if (Count(page, "r=" + edit.Code) != 1)
        {
            missing.Add(string.Create(CultureInfo.InvariantCulture, $"the code shows {Count(page, "r=" + edit.Code)} times, not once"));
        }
        if (Count(page, "theyard.stevenstout.biz") != 1)
        {
            missing.Add("not exactly one Yard link");
        }
        if (!page.EndsWith(line, StringComparison.Ordinal))
        {
            missing.Add("the line is not the comment's last");
        }
        if (!page.StartsWith(Flat(edit.Comment.Text ?? string.Empty), StringComparison.Ordinal))
        {
            missing.Add("his words did not read back as they were");
        }
        return missing;
    }

    /// <summary>The edit-phase worklist: one header, one tab-separated line an edit; the comment is found by its post's author and its first words.</summary>
    /// <param name="edits">The edits.</param>
    public static string Render(IEnumerable<CommentEdit> edits)
    {
        var text = new StringBuilder("row\tpost_author\tpost_topic\tstarts_with\tcomment_url\tcode\tline\n");
        foreach (CommentEdit edit in edits)
        {
            string words = Flat(edit.Comment.Text ?? string.Empty);
            text.AppendJoin('\t', edit.Row, Cell(edit.Comment.PostAuthor), Cell(edit.Comment.Topic), Cell(words[..Math.Min(words.Length, 80)]), Cell(edit.Comment.CommentUrl), edit.Code, Cell(edit.Line)).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>The comment named for the DONE note: the post's author and its topic.</summary>
    /// <param name="comment">The comment.</param>
    public static string Who(OwnComment comment) => $"{comment.PostAuthor}, {comment.Topic}".Trim(' ', ',');

    private static int Count(string text, string part) => part.Length == 0 ? 0 : (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;

    private static string Flat(string text) =>
        Space().Replace(text.Replace("…see more", string.Empty, StringComparison.Ordinal).Replace("https://", string.Empty, StringComparison.Ordinal), " ").Trim();

    private static string Cell(string? text) => (text ?? string.Empty).Replace('\t', ' ').ReplaceLineEndings(" ");

    [GeneratedRegex(@"https?:|www\.|\.(com|biz|io)\b|[\r\n?\u2013\u2014]|\s-\s")]
    private static partial Regex NotPlain();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
