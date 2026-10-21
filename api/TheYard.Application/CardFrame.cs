// The slots every card on the Admin tab fills the same way (ADR: The onion cut into vertical slices,
// the CardShell): the one tone, the headline the rail prints, the thresholds as chips and a source on
// every summary line and row. Each card builds its own report; this completes it, so no card
// computes a slot the others compute differently.
using TheYard.Domain;

namespace TheYard.Application;

// #region card-frame
/// <summary>Completes a card's report with the slots every card shares.</summary>
public static class CardFrame
{
    /// <summary>How often the Admin tab reads a card, in seconds: the page's REFRESH_MS, which CardFrameTests holds to it.</summary>
    public const int RefreshSeconds = 30;

    /// <summary>The source a line names when it is this process's own reading of how fresh the card is.</summary>
    public const string ThisProcess = "this process";

    /// <summary>The source a cost line names.</summary>
    public const string TheBill = "the bill, Cost Management";

    /// <summary>The source of a figure read back from the minutes this site kept, a thirty-day 95th or the like.</summary>
    public const string KeptMinutes = "kept minutes";

    /// <summary>
    /// The report with its tone (the dials' and the rows'), headline and threshold chips filled in.
    /// Sources are the card's own: nothing here fills one in, so a line that names none reaches the
    /// wire without one and CardFrameTests refuses it.
    /// </summary>
    /// <param name="report">The card's own report.</param>
    /// <param name="specSource">Where the spec line was read.</param>
    /// <param name="hourly">True when the card's source is read once an hour; its tones are then not held for minutes, and its reading may be an hour old before it is unread.</param>
    public static CloudReport Complete(CloudReport report, string? specSource, bool hourly = false)
    {
        // The dials' held tones and the rows' own: a row that reads warning or alarm (a throttled
        // request, a stopped app, a capped hour) lifts the card the way a dial over its line does,
        // so the rail's dot shows what the card knows: a Cosmos DB card read "fine" beside its row
        // of seven hundred throttled requests before the rows counted.
        var tone = CardTones.Of(report.Dials.Select(dial => Level(dial.Tone)).Concat(report.Rows.Select(row => Level(row.Tone))), report.ReadAgeSeconds, hourly ? 3_600 : 60);
        bool held = !hourly;
        var first = report.Dials.Count > 0 ? report.Dials[0] : null;
        return report with
        {
            Tone = CardTones.Wire(tone),
            SpecSource = specSource,
            Headline = first is null || first.Percent is null ? null : first.Big,
            Thresholds = Chips(report.Dials, held),
        };
    }

    /// <summary>
    /// Every row (by its key) and readout line (by its label) that names no source, given the one
    /// the card's own list names for it. One list a card, written beside the card, so each line's
    /// source is a decision someone wrote down; a line the list leaves out stays unsourced, and the
    /// test that reads every card fails on it rather than a default saying the wrong thing.
    /// </summary>
    /// <param name="report">The card's report.</param>
    /// <param name="sources">Where each row and line is read, by row key or line label.</param>
    public static CloudReport Sourced(CloudReport report, IReadOnlyDictionary<string, string> sources) => report with
    {
        Rows = [.. report.Rows.Select(row => row.Source is null && sources.TryGetValue(row.Key, out var named) ? row with { Source = named } : row)],
        Readout = report.Readout with { Lines = [.. report.Readout.Lines.Select(line => line.Source is null && sources.TryGetValue(line.Label, out var named) ? line with { Source = named } : line)] },
    };

    /// <summary>The chips: each dial's amber and red line by the dial's short name, then the hold.</summary>
    /// <param name="dials">The card's dials.</param>
    /// <param name="held">Whether the tones are held.</param>
    public static IReadOnlyList<CloudChip> Chips(IReadOnlyList<CloudDial> dials, bool held)
    {
        var chips = new List<CloudChip>();
        foreach (var dial in dials)
        {
            string name = dial.Name.Split(',')[0].Trim();
            chips.Add(new CloudChip($"{name} amber {dial.Amber:0.##} %", "amber"));
            chips.Add(new CloudChip($"{name} red {dial.Red:0.##} %", "red"));
        }
        if (held)
        {
            chips.Add(new CloudChip($"held {PlanThresholds.Sustained} min", "held"));
        }
        return chips;
    }

    /// <summary>A wire tone as a held level: 2 over red, 1 over amber, 0 inside.</summary>
    /// <param name="tone">good, info, warning or alarm.</param>
    public static int Level(string tone) => tone switch
    {
        "alarm" => 2,
        "warning" => 1,
        _ => 0,
    };
}
// #endregion card-frame
