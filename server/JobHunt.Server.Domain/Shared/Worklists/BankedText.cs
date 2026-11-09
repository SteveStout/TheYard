using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One essay the apply lane wrote before typing it: the form's question and the answer it means to type.</summary>
/// <param name="Question">The question as the form shows it.</param>
/// <param name="Answer">The answer, or empty when the file holds none.</param>
public sealed record Essay(string Question, string Answer);

/// <summary>
/// The owner's answer on essays: the apply lane may answer one only from text he already approved, and adds no claim
/// about his history. Checkable as a lookup: every sentence of the answer is a whole sentence of a bank, word for word,
/// read with its markdown taken off and its spaces and quotes made plain. A fragment of a banked sentence is not a banked
/// sentence, and a sentence written about him rather than in his voice is a note, not his text.
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

    /// <summary>
    /// Every whole sentence a bank holds, plain, read paragraph by paragraph: a blank line, a heading or a table row ends a
    /// paragraph, so a heading never joins the sentence under it and a sentence wrapped across lines stays whole.
    /// </summary>
    /// <param name="bank">The bank's text as it sits on the share.</param>
    public static IEnumerable<string> BankSentences(string bank)
    {
        var paragraph = new List<string>();
        foreach (string line in bank.Replace("\r", string.Empty).Split('\n'))
        {
            string start = line.TrimStart();
            if (Plain(line).Length > 0 && !start.StartsWith('#') && !start.StartsWith('|'))
            {
                paragraph.Add(line);
                continue;
            }
            foreach (string sentence in Sentences(string.Join('\n', paragraph)).Concat(Sentences(line)))
            {
                yield return sentence;
            }
            paragraph.Clear();
        }
        foreach (string sentence in Sentences(string.Join('\n', paragraph)))
        {
            yield return sentence;
        }
    }

    /// <summary>
    /// The sentences of the answer that are not whole banked sentences, or are written about him; empty when the answer is
    /// all banked text in his voice. An answer with no sentence at all answers nothing and is named whole.
    /// </summary>
    /// <param name="answer">The answer.</param>
    /// <param name="banked">The banks' whole sentences, from <see cref="BankSentences"/>.</param>
    public static IReadOnlyList<string> Unbanked(string answer, IReadOnlySet<string> banked)
    {
        var sentences = Sentences(answer);
        return sentences.Count == 0 ? [answer] : [.. sentences.Where(sentence => !banked.Contains(sentence) || AboutHim().IsMatch(sentence))];
    }

    [GeneratedRegex(@"(?m)^QUESTION:(?<q>.*)$")]
    private static partial Regex QuestionLine();

    [GeneratedRegex(@"(?m)^[ \t]*(?:>[ \t]?)+|(?m)^[ \t]*[-*][ \t]+")]
    private static partial Regex Markers();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\b(?:he|his|him|Steve|Steve's)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AboutHim();
}
