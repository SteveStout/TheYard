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

    /// <summary>The report with its tone, headline, threshold chips and sources filled in.</summary>
    /// <param name="report">The card's own report.</param>
    /// <param name="source">Where the card's readings come from, for every line and row that does not name its own.</param>
    /// <param name="specSource">Where the spec line was read.</param>
    /// <param name="hourly">True when the card's source is read once an hour; its tones are then not held for minutes, and its reading may be an hour old before it is unread.</param>
    public static CloudReport Complete(CloudReport report, string source, string? specSource, bool hourly = false)
    {
        var tone = CardTones.Of(report.Dials.Select(dial => Level(dial.Tone)), report.ReadAgeSeconds, hourly ? 3_600 : 60);
        bool held = !hourly;
        var first = report.Dials.Count > 0 ? report.Dials[0] : null;
        return report with
        {
            Tone = CardTones.Wire(tone),
            SpecSource = specSource,
            Headline = first is null || first.Percent is null ? null : first.Big,
            Thresholds = Chips(report.Dials, held),
            Rows = [.. report.Rows.Select(row => row.Source is null ? row with { Source = source } : row)],
            Readout = report.Readout with { Lines = [.. report.Readout.Lines.Select(line => line.Source is null ? line with { Source = SourceOf(line.Label, source) } : line)] },
        };
    }

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

    /// <summary>A line's source: the bill for its cost, this process for its live read, the card's own otherwise.</summary>
    /// <param name="label">The line's label.</param>
    /// <param name="source">The card's source.</param>
    public static string SourceOf(string label, string source) =>
        label.Contains("cost", StringComparison.OrdinalIgnoreCase) ? TheBill
            : label.Equals("Live read", StringComparison.Ordinal) ? ThisProcess
            : source;

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
