using System.Text.Json;
using JobHunt.Domain.Features.Watch;
using JobHunt.Domain.Shared.Clock;
using JobHunt.Domain.Shared.Tracker;

namespace JobHunt.Application.Features.Watch;

/// <summary>
/// The heal ladder's writes for one tick in mode on (the year-end plan's 7b): reads what is already open, HEALED.md,
/// PLAYBOOK.md and policy.json's heal_mode, asks <see cref="HealLadder"/> what to do, then writes each heal item and its
/// fixture whole (a temp file replaced, so the dispatcher never reads half an item), appends HEALED.md and adds each
/// NOT-WORKING.md line the file does not hold yet. It writes nothing else and sends nothing.
/// </summary>
/// <param name="store">The share.</param>
public sealed class WatchHeal(IMergeStore store)
{
    private const string RulesLine = "> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).";
    private const string Healed = "dispatch/HEALED.md";
    private const string NotWorking = "dispatch/NOT-WORKING.md";

    /// <summary>Climbs the ladder for the tick's failures; answers the heal items it opened.</summary>
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
        if (plan.Healed.Count > 0)
        {
            await AppendAsync(Healed, "HEALED: every step of the heal ladder, one line a step (time | task | reason | ladder step | result)", plan.Healed, ct);
        }
        string held = await TextAsync(NotWorking, ct);
        var fresh = plan.NotWorking.Where(line => !held.Contains(line[..(line.IndexOf(" since ", StringComparison.Ordinal) + 7)], StringComparison.Ordinal)).Distinct().ToList();
        if (fresh.Count > 0)
        {
            await AppendAsync(NotWorking, "NOT WORKING: one line a failure the heal ladder could not fix (the source of the card's first lines)", fresh, ct);
        }
        return plan;
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
