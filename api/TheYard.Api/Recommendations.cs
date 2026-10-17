using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheYard.Api;

// #region recommendation-shape
/// <summary>
/// One recommendation as LinkedIn shows it, in the one shape every entry of
/// data/recommendations.json has. Adding a recommendation is adding one more
/// object to that file with these six fields; nothing in the code changes.
/// </summary>
/// <param name="Name">The writer's name, as LinkedIn shows it.</param>
/// <param name="Title">The writer's title when they wrote it, as LinkedIn shows it.</param>
/// <param name="Relationship">How the writer worked with Steve, in LinkedIn's words.</param>
/// <param name="When">The month and year LinkedIn dates it, as words.</param>
/// <param name="Paragraphs">The recommendation word for word, one string per paragraph.</param>
/// <param name="Pull">One sentence of it, as written, for a place with room for one line per person.</param>
public sealed record Recommendation(
    string Name,
    string Title,
    string Relationship,
    string When,
    IReadOnlyList<string> Paragraphs,
    string Pull);

/// <summary>What /api/recommendations answers: where the words came from, and every recommendation in the order the file lists them.</summary>
/// <param name="Source">The public page the recommendations were copied from.</param>
/// <param name="Recommendations">Every recommendation, in the order they are shown.</param>
public sealed record RecommendationsReply(string Source, IReadOnlyList<Recommendation> Recommendations);
// #endregion recommendation-shape

// #region recommendations-file
/// <summary>
/// Reads data/recommendations.json once per file. The file is small and never changes inside a
/// container, so the parsed answer is kept until its length or write time moves, the way the test
/// summary is kept. The same class says what shape an entry has to be in, so the file, the test
/// that holds it and the answer a page reads all agree on one definition.
/// </summary>
public static partial class RecommendationsFile
{
    /// <summary>The file's keys are snake_case, the wire's own spelling, so one set of options reads the file and writes the answer.</summary>
    public static readonly JsonSerializerOptions Format = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly object Gate = new();
    private static (string Path, long Length, DateTime WrittenUtc)? _read;
    private static RecommendationsReply? _reply;

    /// <summary>The recommendations in the file at <paramref name="path"/>, cached until the file changes.</summary>
    public static RecommendationsReply Of(string path)
    {
        var info = new FileInfo(path);
        var stamp = (path, info.Length, info.LastWriteTimeUtc);
        lock (Gate)
        {
            if (_reply is not null && _read == stamp)
            {
                return _reply;
            }

            var read = JsonSerializer.Deserialize<RecommendationsReply>(File.ReadAllText(path), Format)
                ?? throw new InvalidDataException($"{path} holds no recommendations object");
            _reply = read;
            _read = stamp;
            return read;
        }
    }

    /// <summary>
    /// Everything an entry has to get right, one line per slip: every field present and not blank,
    /// the month written as words with its year, the pull sentence a line the writer wrote, and no
    /// two entries from the same person. Empty when the file is in shape. The pages show the file
    /// as it is, so this is what the gate reads before a version ships, and what the endpoint
    /// reads before it answers.
    /// </summary>
    public static IReadOnlyList<string> Problems(RecommendationsReply? reply)
    {
        var problems = new List<string>();
        if (reply is null)
        {
            problems.Add("the file holds no recommendations object");
            return problems;
        }

        if (string.IsNullOrWhiteSpace(reply.Source) || !reply.Source.StartsWith("https://", StringComparison.Ordinal))
        {
            problems.Add("source should be the public page the words were copied from");
        }

        if (reply.Recommendations is null || reply.Recommendations.Count == 0)
        {
            problems.Add("the file lists no recommendations");
            return problems;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entry, index) in reply.Recommendations.Select((entry, index) => (entry, index)))
        {
            string who = string.IsNullOrWhiteSpace(entry.Name) ? $"entry {index + 1}" : entry.Name;
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                problems.Add($"{who}: name is blank");
            }
            else if (!names.Add(entry.Name))
            {
                problems.Add($"{who}: a second entry from the same person");
            }

            if (string.IsNullOrWhiteSpace(entry.Title))
            {
                problems.Add($"{who}: title is blank");
            }

            if (string.IsNullOrWhiteSpace(entry.Relationship))
            {
                problems.Add($"{who}: relationship is blank");
            }

            if (entry.When is null || !MonthAndYear().IsMatch(entry.When))
            {
                problems.Add($"{who}: when should be a month and a year in words, such as April 2024");
            }

            if (entry.Paragraphs is null || entry.Paragraphs.Count == 0 || entry.Paragraphs.Any(string.IsNullOrWhiteSpace))
            {
                problems.Add($"{who}: paragraphs should hold the recommendation, one string per paragraph, none blank");
            }

            if (string.IsNullOrWhiteSpace(entry.Pull))
            {
                problems.Add($"{who}: pull is blank");
            }
            else if (entry.Paragraphs is not null && !entry.Paragraphs.Any(paragraph => paragraph?.Contains(entry.Pull, StringComparison.Ordinal) == true))
            {
                problems.Add($"{who}: pull is not a sentence from the paragraphs, as written");
            }
        }

        return problems;
    }

    [GeneratedRegex(@"^[A-Z][a-z]+ 20\d\d$")]
    private static partial Regex MonthAndYear();
}
// #endregion recommendations-file
