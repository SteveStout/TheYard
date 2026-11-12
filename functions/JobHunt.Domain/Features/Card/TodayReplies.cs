using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>What a draft's .gate.md says up front, each part null when the file does not say it.</summary>
/// <param name="RedFlag">The <c>RED FLAG: </c> verdict with its top flag, the probe left off.</param>
/// <param name="Fit">The <c>FIT: </c> line.</param>
/// <param name="Unanswered">The <c>UNANSWERED IN WRITING: </c> must-answers.</param>
/// <param name="First">The file's first line of text, for a gate written before the verdict lines existed.</param>
public sealed record TodayGate(string? RedFlag, string? Fit, string? Unanswered, string First);

/// <summary>
/// The card's replies (fitgate1110 fix 2): every reply, drafted or held, opens with its red flag verdict and then its
/// fit line, so a sender the scan marked RED never reaches him as a plain send. The verdict comes from the gate file
/// beside the draft, or beside the draft a held item keeps, else from a held reason that opens with it; the fit line
/// from the queue's <c>Note: fit: </c>, else the gate's, else it says FIT: NOT CHECKED. A RED email draft is never a
/// CRITICAL send: it prints here, held for his word, with its full text. Each reply ends with its history line
/// (TodayHistory).
/// </summary>
public static partial class TodayReplies
{
    /// <summary>What a reply with no fit line on record prints in its place.</summary>
    public const string NotChecked = "FIT: NOT CHECKED";

    /// <summary>The draft an item names, or the one a held item says it keeps, from the share's root; empty when neither.</summary>
    /// <param name="item">The queue item.</param>
    public static string Kept(TodayItem item) =>
        item.Draft.Length > 0 ? item.Draft : KeptAt().Match(item.Held) is { Success: true } m ? TodayReads.ShareRoot(m.Groups[1].Value) : string.Empty;

    /// <summary>
    /// A gate file read for its verdict lines: the lines before its first heading (a heading at the very top skipped),
    /// each cut where a <c> | </c> opens the next RED FLAG, PROBE, FIT, UNANSWERED IN WRITING or HOLD part.
    /// </summary>
    /// <param name="text">The .gate.md's text.</param>
    public static TodayGate Gate(string text)
    {
        var lines = TodayParts.Lines(text).Select(l => l.Trim().TrimStart('-', ' ')).SkipWhile(l => l.Length == 0 || l.StartsWith('>') || l.StartsWith('#'))
            .TakeWhile(l => !l.StartsWith('#')).Where(l => l.Length > 0 && !l.StartsWith('>')).ToList();
        var parts = lines.SelectMany(l => Part().Split(l)).Select(p => p.Trim()).ToList();
        string? Find(string prefix) => parts.FirstOrDefault(p => p.StartsWith(prefix, StringComparison.Ordinal));
        return new TodayGate(Find("RED FLAG:"), Find("FIT:"), Find("UNANSWERED IN WRITING:"), lines.FirstOrDefault() ?? string.Empty);
    }

    /// <summary>The email drafts that go up as CRITICAL sends: section A, a draft on the share, and no RED verdict.</summary>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="replies">Section A's items still shown.</param>
    public static IReadOnlyList<TodayItem> Sends(TodayInputs inputs, IReadOnlyList<TodayItem> replies) =>
        [.. replies.Where(i => i.IsEmail && inputs.Drafts.ContainsKey(i.Draft) && !IsRed(inputs, i))];

    /// <summary>Each CRITICAL send, its verdict and fit line first, then its full text and where the draft is.</summary>
    /// <param name="body">The card so far.</param>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="sends">The sends, in their order.</param>
    public static void Critical(StringBuilder body, TodayInputs inputs, IReadOnlyList<TodayItem> sends)
    {
        int n = 0;
        foreach (var mail in sends)
        {
            n++;
            var gate = GateOf(inputs, mail);
            string lead = string.Join(". ", new[] { Verdict(mail, gate), Fit(mail, gate) }.OfType<string>().Select(s => s.TrimEnd('.')));
            body.Append(CultureInfo.InvariantCulture, $"{n}. {lead}. CRITICAL, an email under your name to {mail.Who}, a reply on {mail.Reference}. Full text:\n");
            Quote(body, inputs.Drafts[mail.Draft]);
            string? line = GateLine(gate);
            body.Append(CultureInfo.InvariantCulture, $"   {(line is null ? string.Empty : $"Gate: {line} ")}Draft: {mail.Draft}. Say \"send\", or edit it.\n");
        }
    }

    /// <summary>Section A on the card: each reply with its verdict and fit line first, held ones folded to one line when the card runs long.</summary>
    /// <param name="body">The card so far.</param>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="replies">Section A's items that need his word.</param>
    /// <param name="handled">Section A's items that need nothing from him (TodayHandled), one line after the rest.</param>
    /// <param name="fold">Whether the card is over its cap.</param>
    public static void Draw(StringBuilder body, TodayInputs inputs, IReadOnlyList<TodayItem> replies, IReadOnlyList<TodayItem> handled, bool fold)
    {
        var sends = Sends(inputs, replies);
        var held = replies.Where(r => !inputs.Drafts.ContainsKey(r.Draft)).ToList();
        foreach (var reply in replies.Where(r => !fold || inputs.Drafts.ContainsKey(r.Draft)))
        {
            One(body, inputs, reply, sends);
        }
        if (fold && held.Count > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"- {held.Count} held {(held.Count == 1 ? "reply" : "replies")} with no draft, each with its reason, in {inputs.QueuePath}.\n");
        }
        TodayHandled.Line(body, handled);
        if (replies.Count == 0 && handled.Count == 0)
        {
            body.Append(inputs.QueuePath is null ? "- Not read: no reply run of the day wrote linkedin-queue.md.\n" : "- None in the newest reply queue.\n");
        }
    }

    /// <summary>A draft's text quoted under its item, one quoted line a line.</summary>
    /// <param name="body">The card so far.</param>
    /// <param name="text">The draft.</param>
    public static void Quote(StringBuilder body, string text)
    {
        foreach (string line in TodayParts.Lines(text.Trim()))
        {
            body.Append("   > ").Append(line).Append('\n');
        }
    }

    private static void One(StringBuilder body, TodayInputs inputs, TodayItem reply, IReadOnlyList<TodayItem> sends)
    {
        var gate = GateOf(inputs, reply);
        string? verdict = Verdict(reply, gate);
        string fit = Fit(reply, gate);
        body.Append(CultureInfo.InvariantCulture, $"- **{reply.Who}**{Channel(reply)}: {verdict ?? fit}\n");
        if (verdict is not null)
        {
            body.Append("  ").Append(fit).Append('\n');
        }
        if (GateLine(gate) is { } line)
        {
            body.Append("  Gate: ").Append(line).Append('\n');
        }
        if (!inputs.Drafts.TryGetValue(reply.Draft, out string? draft))
        {
            string why = reply.Draft.Length > 0 ? $"the draft {reply.Draft} is not on the share" : verdict is not null && reply.Held.StartsWith("RED FLAG:", StringComparison.Ordinal) && reply.Held.Contains("; ", StringComparison.Ordinal) ? reply.Held[(reply.Held.IndexOf("; ", StringComparison.Ordinal) + 2)..] : reply.Held;
            body.Append("  Held: ").Append(why).Append('\n');
        }
        else if (IsRed(verdict, fit))
        {
            body.Append("  RED FLAG, held for your word. The draft, sent only on your word:\n");
            Quote(body, draft);
        }
        else if (sends.Contains(reply))
        {
            body.Append(CultureInfo.InvariantCulture, $"  The full text is CRITICAL send {sends.ToList().IndexOf(reply) + 1} above.\n");
        }
        else
        {
            body.Append("  The draft, typed live in the thread for your send once you say \"send\" or edit it:\n");
            Quote(body, draft);
        }
        body.Append("  ").Append(TodayHistory.Line(inputs, reply)).Append('\n');
    }

    private static bool IsRed(TodayInputs inputs, TodayItem item)
    {
        var gate = GateOf(inputs, item);
        return IsRed(Verdict(item, gate), Fit(item, gate));
    }

    private static bool IsRed(string? verdict, string fit) =>
        (verdict?.StartsWith("RED FLAG: RED", StringComparison.Ordinal) ?? false) || fit.Contains("RED FLAG RED", StringComparison.Ordinal);

    private static TodayGate? GateOf(TodayInputs inputs, TodayItem item) =>
        inputs.Gates.TryGetValue(Kept(item), out string? text) && text.Length > 0 ? Gate(text) : null;

    private static string? Verdict(TodayItem item, TodayGate? gate) =>
        gate?.RedFlag ?? (item.Held.StartsWith("RED FLAG:", StringComparison.Ordinal) ? item.Held.Split("; ")[0].Trim() : null);

    private static string Fit(TodayItem item, TodayGate? gate) =>
        item.Notes.Where(n => n.StartsWith("fit: ", StringComparison.Ordinal)).Select(n => n["fit: ".Length..].Trim()).FirstOrDefault() ?? gate?.Fit ?? NotChecked;

    // The must-answers line; a gate written before the verdict lines existed prints its first line, as it always did.
    private static string? GateLine(TodayGate? gate) =>
        gate is null ? null : gate.Unanswered ?? (gate.RedFlag is null && gate.Fit is null && gate.First.Length > 0 ? gate.First : null);

    private static string Channel(TodayItem item) =>
        item.IsEmail ? $", email ({item.Reference})"
        : item.Reference.StartsWith("thread: linkedin", StringComparison.Ordinal) ? $", LinkedIn ({item.Reference})"
        : item.Reference.Length > 0 ? $" ({item.Reference})" : string.Empty;

    [GeneratedRegex(@" \| (?=(?:RED FLAG|PROBE|FIT|UNANSWERED IN WRITING|HOLD):)")]
    private static partial Regex Part();

    [GeneratedRegex(@"draft kept at (\S+)")]
    private static partial Regex KeptAt();
}
