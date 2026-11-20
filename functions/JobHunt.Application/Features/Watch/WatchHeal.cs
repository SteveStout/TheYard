using System.Text.Json;
using JobHunt.Application.Shared.Mail;
using JobHunt.Domain.Features.Watch;
using JobHunt.Domain.Shared.Clock;
using JobHunt.Domain.Shared.Tracker;

namespace JobHunt.Application.Features.Watch;

/// <summary>
/// The heal ladder's writes for one tick in mode on (the year-end plan's 7b and 7c): reads what is already open,
/// HEALED.md, PLAYBOOK.md and policy.json's heal_mode, asks <see cref="HealLadder"/> what to do, then writes each heal
/// item and its fixture whole (a temp file replaced, so the dispatcher never reads half an item), appends HEALED.md,
/// moves a NOT-WORKING.md line whose test has passed twice to NOT-WORKING-cleared.md, and adds each new line once. A new
/// line goes under TODAY.md's title and in one mail to his own address, subject "NOT WORKING:", at most once a failure
/// a Central day; the mail is information, never a question. It sends nothing else.
/// </summary>
/// <param name="store">The share.</param>
/// <param name="mail">The card's own sender behind the no-email-day gate; null sends nothing.</param>
public sealed class WatchHeal(IMergeStore store, IMailSender? mail = null)
{
    /// <summary>His own address, the only one the NOT WORKING mail goes to (the card's, TodayMail.To).</summary>
    public const string Own = "stevenstout11@gmail.com";

    private const string RulesLine = "> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).";
    private const string Healed = "dispatch/HEALED.md";
    private const string NotWorking = "dispatch/NOT-WORKING.md";
    private const string Mailed = "dispatch/watch/not-working-mailed.tsv";
    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>Climbs the ladder for the tick's failures; answers the plan, its NOT WORKING lines the new ones only.</summary>
    /// <param name="results">The tick's readings.</param>
    /// <param name="changeLog">The day's change log with this tick's lines.</param>
    /// <param name="now">The tick's time.</param>
    /// <param name="ct">Stops the writes.</param>
    public async Task<HealPlan> RunAsync(IReadOnlyList<WatchResult> results, string changeLog, DateTimeOffset now, CancellationToken ct)
    {
        DateTime local = CentralClock.Local(now);
        var open = new HashSet<string>(StringComparer.Ordinal);
        foreach (WatchResult r in results.Where(r => r.Pass == false))
        {
            string name = HealLadder.Name($"{r.Test} {r.Name}", local);
            if (await store.ExistsAsync($"dispatch/ready/{name}", ct) || await store.ExistsAsync($"dispatch/heal-shadow/{name}", ct)
                || await store.ExistsAsync($"dispatch/running/{name[3..^3]}/prompt.md", ct))
            {
                open.Add(name);
            }
        }
        var plan = HealLadder.Plan(results, changeLog, await TextAsync(Healed, ct), await TextAsync("dispatch/PLAYBOOK.md", ct), open, await LiveAsync(ct), now);
        foreach (HealItem item in plan.Items)
        {
            await store.EnsureFolderAsync("dispatch/heal-fixtures", ct);
            await WholeAsync(item.Fixture, item.FixtureText, ct);
            await store.EnsureFolderAsync(item.Folder, ct);
            await WholeAsync($"{item.Folder}/{item.Name}", item.Text, ct);
        }
        string held = await TextAsync(NotWorking, ct);
        var cleared = HealLadder.Cleared(held, results, changeLog, local);
        var steps = plan.Healed.Concat(cleared.Select(l => string.Create(Inv, $"- {local:yyyy-MM-dd HH:mm} CST | {l["NOT WORKING: ".Length..l.IndexOf(" since ", StringComparison.Ordinal)]} | its test passed twice | 5 cleared | moved to NOT-WORKING-cleared.md"))).ToList();
        if (steps.Count > 0)
        {
            await AppendAsync(Healed, "HEALED: every step of the heal ladder, one line a step (time | task | reason | ladder step | result)", steps, ct);
        }
        if (cleared.Count > 0)
        {
            await AppendAsync("dispatch/NOT-WORKING-cleared.md", "NOT WORKING, cleared: each line the watch moved out when its test passed twice", [.. cleared.Select(l => string.Create(Inv, $"{l} Cleared {local:yyyy-MM-dd HH:mm} CST."))], ct);
            held = string.Concat(held.Split('\n').Where(l => !cleared.Contains(l.TrimEnd('\r'))).Select(l => l + "\n"))[..^1];
            await WholeAsync(NotWorking, held, ct);
        }
        var fresh = plan.NotWorking.Where(line => !held.Contains(line[..(line.IndexOf(" since ", StringComparison.Ordinal) + 7)], StringComparison.Ordinal)).Distinct().ToList();
        if (fresh.Count > 0)
        {
            await AppendAsync(NotWorking, "NOT WORKING: one line a failure the heal ladder could not fix (the source of the card's first lines)", fresh, ct);
            await TellAsync(fresh, local, ct);
        }
        return plan with { NotWorking = fresh };
    }

    /// <summary>A new line under TODAY.md's title, and one mail a failure a day to his own address.</summary>
    private async Task TellAsync(IReadOnlyList<string> fresh, DateTime local, CancellationToken ct)
    {
        if (await store.ExistsAsync("TODAY.md", ct))
        {
            await WholeAsync("TODAY.md", HealLadder.OnTheCard(await TextAsync("TODAY.md", ct), fresh), ct);
        }
        if (mail is null)
        {
            return;
        }
        string day = local.ToString("yyyy-MM-dd", Inv);
        string mailed = await TextAsync(Mailed, ct);
        foreach (string line in fresh)
        {
            string what = line[..line.IndexOf(" since ", StringComparison.Ordinal)];
            if (mailed.Contains($"{day}\t{what}\n", StringComparison.Ordinal))
            {
                continue;
            }
            string text = $"{line}\n\nThe watch wrote this to J:\\dispatch\\NOT-WORKING.md; J:\\dispatch\\HEALED.md holds every step the heal ladder took. Nothing waits on a reply.\n";
            try
            {
                await mail.SendAsync(new OutgoingMail(Own, what, text, $"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>"), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The line is on NOT-WORKING.md and the card either way; a mail that did not go is not a failed tick.
                continue;
            }
            await store.EnsureFolderAsync("dispatch/watch", ct);
            await store.AppendAsync(Mailed, TrackerCsvText.Encode($"{day}\t{what}\n"), ct);
        }
    }

    /// <summary>Whether policy.json says heal_mode on; anything else, or a file that will not read, is shadow.</summary>
    private async Task<bool> LiveAsync(CancellationToken ct)
    {
        try
        {
            using var policy = JsonDocument.Parse(await TextAsync("dispatch/policy.json", ct) is { Length: > 0 } text ? text : "{}");
            return policy.RootElement.TryGetProperty("heal_mode", out var mode) && mode.GetString() == "on";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task WholeAsync(string path, string text, CancellationToken ct)
    {
        await store.WriteAsync(MergePaths.Temp(path), TrackerCsvText.Encode(text), ct);
        await store.ReplaceAsync(MergePaths.Temp(path), path, ct);
    }

    private async Task AppendAsync(string path, string title, IReadOnlyList<string> lines, CancellationToken ct)
    {
        string head = await store.ExistsAsync(path, ct) ? string.Empty : $"{RulesLine}\n\n# {title}\n\n";
        await store.AppendAsync(path, TrackerCsvText.Encode(head + string.Concat(lines.Select(l => l + "\n"))), ct);
    }

    private async Task<string> TextAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(path, ct)) : string.Empty;
}
