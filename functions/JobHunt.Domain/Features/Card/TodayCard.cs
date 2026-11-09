using System.Globalization;
using System.Text;

namespace JobHunt.Domain.Features.Card;

/// <summary>Everything the morning card is drawn from, read off the share and the calendar before it is drawn.</summary>
/// <param name="At">When, in Central time.</param>
/// <param name="QueuePath">The newest real reply run's queue, from the share's root, or null when no run of the day wrote one.</param>
/// <param name="Queue">Its text.</param>
/// <param name="Drafts">Each draft the queue or the carried reads name that exists, by its path from the share's root.</param>
/// <param name="DoNotSend">The drafts with a DO-NOT-SEND note beside them.</param>
/// <param name="Gates">Each draft's .gate.md first line, by the draft's path.</param>
/// <param name="Status">dispatch/STATUS.md.</param>
/// <param name="LeftForSteve">dispatch/LEFT-FOR-STEVE.md.</param>
/// <param name="Answered">dispatch/ANSWERED.md.</param>
/// <param name="Walls">dispatch/WALLS.md.</param>
/// <param name="Decision">dispatch/DECISION.md.</param>
/// <param name="EssaysPath">The day's APPLY-ESSAYS file, or null when the 6 AM run wrote none.</param>
/// <param name="Essays">Its text.</param>
/// <param name="Previous">TODAY.md as it stood before this card.</param>
/// <param name="Calls">The next 72 hours' calls, each with its must-answers line.</param>
public sealed record TodayInputs(
    DateTime At,
    string? QueuePath,
    string Queue,
    IReadOnlyDictionary<string, string> Drafts,
    IReadOnlySet<string> DoNotSend,
    IReadOnlyDictionary<string, string> Gates,
    string Status,
    string LeftForSteve,
    string Answered,
    string Walls,
    string Decision,
    string? EssaysPath,
    string Essays,
    string Previous,
    IReadOnlyList<string> Calls);

/// <summary>
/// The one card Steve reads (PLAN-AUTOHUNT rung 4): J:\TODAY.md, rewritten whole at 7:30 Central by the Function with
/// no model and mailed to him. In order: the CRITICAL sends with their full text, the essays the 6 AM run submitted,
/// the replies with each draft in full, the calls in the next 72 hours with their must-answers, the lanes, what closed
/// lanes left for him, the walls (Mondays, or when a human there has replied), one decision, and the rejection reads
/// carried forward. Anything he has answered (ANSWERED.md) and any draft with a DO-NOT-SEND note beside it is never
/// shown. Capped at 15 minutes: over it, the held replies fold into one line.
/// </summary>
public static class TodayCard
{
    /// <summary>The card on the share.</summary>
    public const string Path = "TODAY.md";

    /// <summary>The minutes of his time the card may take.</summary>
    public const int CapMinutes = 15;

    /// <summary>The words a minute the cap is counted at.</summary>
    public const int WordsAMinute = 200;

    /// <summary>The line every .md on the share opens with.</summary>
    public const string RulesFirst = "> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).";

    /// <summary>The subject the mail carries, which triage keeps in the inbox.</summary>
    /// <param name="at">The card's time.</param>
    public static string Subject(DateTime at) => "TODAY, " + at.ToString("ddd M/d", CultureInfo.InvariantCulture);

    /// <summary>The card's text, folded to the cap when the whole card would run over it.</summary>
    /// <param name="inputs">What it is drawn from.</param>
    public static string Render(TodayInputs inputs)
    {
        string card = Draw(inputs, fold: false);
        return Minutes(card) <= CapMinutes ? card : Draw(inputs, fold: true);
    }

    /// <summary>Minutes to read a text at <see cref="WordsAMinute"/>, at least one.</summary>
    /// <param name="text">The text.</param>
    public static int Minutes(string text) =>
        Math.Max(1, (int)Math.Ceiling(text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length / (double)WordsAMinute));

    /// <summary>Every draft path the card may show: the queue's, and the ones the carried rejection reads name.</summary>
    /// <param name="queue">The reply queue.</param>
    /// <param name="previous">TODAY.md before this card.</param>
    public static IReadOnlyList<string> DraftPaths(string queue, string previous) =>
        [.. TodayParts.Items(queue).Select(i => i.Draft).Concat(TodayReads.Carried(previous).Select(TodayReads.DraftOf)).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal)];

    private static string Draw(TodayInputs inputs, bool fold)
    {
        var answered = TodayParts.Answered(inputs.Answered);
        var items = TodayParts.Items(inputs.Queue).Where(i => Shown(i, inputs, answered)).ToList();
        var emails = items.Where(i => i.Section == "A" && i.IsEmail && inputs.Drafts.ContainsKey(i.Draft)).ToList();
        var left = TodayParts.Left(inputs.LeftForSteve, inputs.At).Where(s => s.Closed > inputs.At.AddDays(-1) && s.Closed <= inputs.At).ToList();
        var critical = left.SelectMany(s => s.Blocks.Where(Critical).Select(b => (s.Lane, Block: b))).Where(c => !TodayParts.IsAnswered(c.Block, answered)).ToList();
        var reads = TodayReads.Rebuild(inputs, items.Where(i => i.Section == "E").ToList(), answered);
        var body = new StringBuilder();

        body.Append("## CRITICAL sends\n\n");
        int n = 0;
        foreach (var mail in emails)
        {
            n++;
            body.Append(CultureInfo.InvariantCulture, $"{n}. CRITICAL, an email under your name to {mail.Who}, a reply on {mail.Reference}. Full text:\n");
            Quote(body, inputs.Drafts[mail.Draft]);
            body.Append(CultureInfo.InvariantCulture, $"   {Gate(inputs, mail.Draft)}Draft: {mail.Draft}. Say \"send\", or edit it.\n");
        }
        foreach (var (lane, block) in critical)
        {
            body.Append(block).Append(CultureInfo.InvariantCulture, $" ({lane})\n");
        }
        int asks = reads.Count(r => r.Contains("CRITICAL", StringComparison.Ordinal));
        if (asks > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"- {asks} feedback {(asks == 1 ? "ask" : "asks")}, each in full under Rejection reads at the end.\n");
        }
        if (n == 0 && critical.Count == 0 && asks == 0)
        {
            body.Append("- None.\n");
        }

        if (inputs.EssaysPath is not null)
        {
            body.Append(CultureInfo.InvariantCulture, $"\n## Essays the 6 AM run submitted ({inputs.EssaysPath})\n\n");
            body.Append(string.Join("\n", TodayParts.Lines(inputs.Essays).Where(l => !l.StartsWith("> RULES FIRST", StringComparison.Ordinal))).Trim()).Append('\n');
        }

        body.Append(CultureInfo.InvariantCulture, $"\n## Replies ({inputs.QueuePath ?? "no reply run of the day wrote a queue"}, section A)\n\n");
        Replies(body, inputs, items.Where(i => i.Section == "A").ToList(), fold);

        body.Append("\n## Calls in the next 72 hours (Google Calendar; must-answers from NOTES-<COMPANY>.md)\n\n");
        Bullets(body, inputs.Calls, "No interview or call on the calendar in the next 72 hours.");

        body.Append(CultureInfo.InvariantCulture, $"\n## The lanes (dispatch/STATUS.md, {TodayParts.Written(inputs.Status) ?? "no time line"})\n\n");
        Bullets(body, TodayParts.Lanes(inputs.Status), "Nothing running, held or proposed.");

        body.Append("\n## Left for you by lanes that closed in the last 24 hours (dispatch/LEFT-FOR-STEVE.md)\n\n");
        var sections = left.Select(s => (s, Blocks: s.Blocks.Where(b => !Critical(b) && !TodayParts.IsAnswered(b, answered)).ToList())).Where(x => x.Blocks.Count > 0).ToList();
        if (sections.Count == 0)
        {
            body.Append("- None.\n");
        }
        foreach (var (section, blocks) in sections)
        {
            body.Append(CultureInfo.InvariantCulture, $"### {section.Lane}, closed {section.Closed:ddd M/d HH:mm}\n\n");
            foreach (string block in blocks)
            {
                body.Append(block).Append('\n');
            }
            body.Append('\n');
        }

        bool monday = inputs.At.DayOfWeek == DayOfWeek.Monday;
        var walls = TodayParts.Bullets(inputs.Walls).Where(w => (monday || w.Contains("replied", StringComparison.OrdinalIgnoreCase)) && !TodayParts.IsAnswered(w, answered)).ToList();
        if (monday || walls.Count > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"\n## Walls (dispatch/WALLS.md; {(monday ? "Monday, the whole list" : "only where a human has replied")})\n\n");
            Bullets(body, walls, "None.");
        }

        body.Append("\n## One decision (dispatch/DECISION.md)\n\n");
        Bullets(body, TodayParts.Bullets(inputs.Decision).Where(d => !TodayParts.IsAnswered(d, answered)).Take(1).ToList(), "None.");

        body.Append("\n## Rejection reads\n\n");
        Bullets(body, [], reads.Count == 0 ? "None." : null);
        foreach (string read in reads)
        {
            body.Append(read).Append('\n');
        }

        string text = body.ToString();
        int minutes = Minutes(text);
        var head = new StringBuilder();
        head.Append(RulesFirst).Append("\n\n");
        head.Append(CultureInfo.InvariantCulture, $"# TODAY, {inputs.At:dddd M/d}\n\n");
        head.Append(CultureInfo.InvariantCulture, $"Written {inputs.At:HH:mm} Central by the job hunt's Function, no model; each heading names the file it was read from. About {minutes} {(minutes == 1 ? "minute" : "minutes")} to read{(fold ? ", the held replies folded to stay near the 15-minute cap" : string.Empty)}{(minutes > CapMinutes ? $", over the {CapMinutes}-minute cap" : string.Empty)}.\n\n");
        return head.Append(text).ToString();
    }

    private static void Replies(StringBuilder body, TodayInputs inputs, IReadOnlyList<TodayItem> replies, bool fold)
    {
        int mail = 0;
        var held = replies.Where(r => !inputs.Drafts.ContainsKey(r.Draft)).ToList();
        foreach (var reply in replies.Where(r => inputs.Drafts.ContainsKey(r.Draft)))
        {
            if (reply.IsEmail)
            {
                mail++;
                body.Append(CultureInfo.InvariantCulture, $"- **{reply.Who}**, email: the full text is CRITICAL send {mail} above.\n");
                continue;
            }
            body.Append(CultureInfo.InvariantCulture, $"- **{reply.Who}**, LinkedIn ({reply.Reference}). {Gate(inputs, reply.Draft)}The draft, typed live in the thread for your send once you say \"send\" or edit it:\n");
            Quote(body, inputs.Drafts[reply.Draft]);
        }
        if (fold && held.Count > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"- {held.Count} held {(held.Count == 1 ? "reply" : "replies")} with no draft, each with its reason, in {inputs.QueuePath}.\n");
        }
        else
        {
            foreach (var reply in held)
            {
                body.Append(CultureInfo.InvariantCulture, $"- **{reply.Who}**, held: {(reply.Draft.Length > 0 ? $"the draft {reply.Draft} is not on the share" : reply.Held)}\n");
            }
        }
        if (replies.Count == 0)
        {
            body.Append(inputs.QueuePath is null ? "- Not read: no reply run of the day wrote linkedin-queue.md.\n" : "- None in the newest reply queue.\n");
        }
    }

    private static bool Shown(TodayItem item, TodayInputs inputs, IReadOnlyList<string> answered)
    {
        if (TodayParts.IsAnswered(item.Who + " " + item.Held, answered) || inputs.DoNotSend.Contains(item.Draft))
        {
            return false;
        }
        if (item.Draft.Length == 0)
        {
            return true;
        }
        return !inputs.Drafts.TryGetValue(item.Draft, out string? text) || !TodayParts.IsAnswered(text, answered);
    }

    private static bool Critical(string block) => block.TrimStart('-', ' ', '*').StartsWith("CRITICAL", StringComparison.Ordinal);

    private static string Gate(TodayInputs inputs, string draft) =>
        inputs.Gates.TryGetValue(draft, out string? gate) && gate.Length > 0 ? $"Gate: {gate} " : string.Empty;

    private static void Quote(StringBuilder body, string text)
    {
        foreach (string line in TodayParts.Lines(text.Trim()))
        {
            body.Append("   > ").Append(line).Append('\n');
        }
    }

    private static void Bullets(StringBuilder body, IReadOnlyList<string> lines, string? none)
    {
        foreach (string line in lines)
        {
            body.Append("- ").Append(line).Append('\n');
        }
        if (lines.Count == 0 && none is not null)
        {
            body.Append("- ").Append(none).Append('\n');
        }
    }
}
