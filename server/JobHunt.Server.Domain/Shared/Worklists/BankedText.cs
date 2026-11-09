using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One essay the apply lane wrote before typing it: the form's question and the answer it means to type.</summary>
/// <param name="Question">The question as the form shows it.</param>
/// <param name="Answer">The answer, or empty when the file holds none.</param>
public sealed record Essay(string Question, string Answer);

/// <summary>
/// The owner's answer on essays: the apply lane may answer one only from text he already approved, and adds no claim
/// about his history. Checkable as a lookup: every sentence of the answer appears word for word in a bank, read with
/// its markdown taken off and its spaces and quotes made plain. A sentence nowhere in a bank is new text, whatever it says.
/// </summary>
public static partial class BankedText
{
    /// <summary>True when the lane has finished the file: its last line reads END.</summary>
    /// <param name="text">The file's text.</param>
    public static bool Finished(string text) => text.Replace("\r", string.Empty).TrimEnd().EndsWith("\nEND", StringComparison.Ordinal);

    /// <summary>The essay a lane's file holds: a QUESTION: line, then an ANSWER: line with the answer under it, then END.</summary>
    /// <param name="text">The file's text.</param>
    public static Essay Parse(string text)
    {
        string body = text.Replace("\r", string.Empty).TrimEnd();
        body = Finished(body) ? body[..^"END".Length] : body;
        Match question = QuestionLine().Match(body);
        int answer = body.IndexOf("\nANSWER:", StringComparison.Ordinal);
        string start = body.StartsWith("ANSWER:", StringComparison.Ordinal) ? body : answer < 0 ? string.Empty : body[(answer + 1)..];
        return new Essay(question.Success ? question.Groups["q"].Value.Trim() : string.Empty, start.Length == 0 ? string.Empty : start["ANSWER:".Length..].Trim());
    }

    /// <summary>Text as the comparison reads it: no quote markers, bullets or emphasis, plain quotes, one space between words.</summary>
    /// <param name="text">The text.</param>
    public static string Plain(string text)
    {
        string flat = Markers().Replace(text.Replace("\r", string.Empty), string.Empty).Replace("*", string.Empty, StringComparison.Ordinal);
        flat = flat.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"');
        return Spaces().Replace(flat, " ").Trim();
    }

    /// <summary>The answer's sentences, each plain.</summary>
    /// <param name="answer">The answer.</param>
    public static IReadOnlyList<string> Sentences(string answer) =>
        [.. SentenceEnd().Split(Plain(answer)).Select(sentence => sentence.Trim()).Where(sentence => sentence.Length > 0)];

    /// <summary>The sentences of the answer that no bank holds word for word; empty when the answer is all banked text.</summary>
    /// <param name="answer">The answer.</param>
    /// <param name="banks">The banks' text, each already through <see cref="Plain"/>.</param>
    public static IReadOnlyList<string> Unbanked(string answer, IReadOnlyList<string> banks) =>
        [.. Sentences(answer).Where(sentence => !banks.Any(bank => bank.Contains(sentence, StringComparison.Ordinal)))];

    [GeneratedRegex(@"(?m)^QUESTION:(?<q>.*)$")]
    private static partial Regex QuestionLine();

    [GeneratedRegex(@"(?m)^[ \t]*(?:>[ \t]?)+|(?m)^[ \t]*[-*][ \t]+")]
    private static partial Regex Markers();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();
}
