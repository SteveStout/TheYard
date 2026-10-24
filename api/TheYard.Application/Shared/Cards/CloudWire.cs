// The one shape every card under Cloud infrastructure answers with (ADR: The App Service plan, read every minute): two dials, two sparklines, a chart, four question rows and a readout. Each resource
// fills the same frame with its own numbers, so the page draws one layout seven times instead of
// holding seven copies of it.
using System.ComponentModel;

namespace TheYard.Application.Shared.Cards;

// #region cloud-wire
/// <summary>One resource's card for one window, every figure read from the kept minutes.</summary>
/// <param name="Window">The window asked for: 24h, 7d or 30d.</param>
/// <param name="Windows">Every window the card offers.</param>
/// <param name="Available">False when nothing can be drawn; <paramref name="Note"/> says why.</param>
/// <param name="Note">One sentence about the keep or what is missing; null when there is nothing to say.</param>
/// <param name="AsOfMs">When the report was built, in milliseconds since the epoch, UTC.</param>
/// <param name="ReadAgeSeconds">How old the newest kept minute is, in seconds; null before the first.</param>
/// <param name="Title">The card's title.</param>
/// <param name="Spec">The resource's size in a few words, as the header's right side prints it.</param>
/// <param name="Resource">The resource the card is about, with its portal link.</param>
/// <param name="Dials">The two dials, left then right.</param>
/// <param name="Sparks">The two sixty-minute sparklines between the dials.</param>
/// <param name="Chart">The window's chart.</param>
/// <param name="Rows">The four question rows.</param>
/// <param name="Readout">The readout beside the card.</param>
public sealed record CloudReport(
    string Window,
    IReadOnlyList<string> Windows,
    bool Available,
    string? Note,
    long AsOfMs,
    long? ReadAgeSeconds,
    string Title,
    string? Spec,
    CloudLink? Resource,
    IReadOnlyList<CloudDial> Dials,
    IReadOnlyList<CloudSpark> Sparks,
    CloudChart Chart,
    IReadOnlyList<CloudRow> Rows,
    CloudReadout Readout)
{
    /// <summary>The card's one tone (Domain's CardTones): fine, amber, red or unread. The rail's dot and the header's chip.</summary>
    [Description("fine, amber, red or unread")]
    public string Tone { get; init; } = "unread";

    /// <summary>Where the spec line was read: "ARM sku · this process".</summary>
    public string? SpecSource { get; init; }

    /// <summary>The first dial's figure, which the rail prints beside the card's name.</summary>
    public string? Headline { get; init; }

    /// <summary>The thresholds as chips, one a level, in place of the sentence under the dials.</summary>
    public IReadOnlyList<CloudChip> Thresholds { get; init; } = [];

    /// <summary>How often the tab reads the card, in seconds; the retry line counts down from it.</summary>
    public int RefreshSeconds { get; init; } = CardFrame.RefreshSeconds;

    /// <summary>What the resource is, in a paragraph read from its own code, with where it was read; null on every card but the nightly Function's.</summary>
    public CloudDescription? Description { get; init; }

    /// <summary>The resource's processes, one section each; empty on every card but the nightly Function's.</summary>
    public IReadOnlyList<CloudProcess> Processes { get; init; } = [];
}

/// <summary>One threshold as a chip: "CPU amber 60 %".</summary>
/// <param name="Label">What the chip says.</param>
/// <param name="Tone">amber, red or held.</param>
public sealed record CloudChip(string Label, [property: Description("amber, red or held")] string Tone);

/// <summary>A piece of text that may link to a resource's portal blade.</summary>
/// <param name="Text">What is printed.</param>
/// <param name="Href">Where it opens, built from the id and tenant Azure returned; null for plain text.</param>
public sealed record CloudLink(string Text, string? Href);

/// <summary>One dial: the reading now, in percent of its limit, with the thirty-day 95th and peak marked.</summary>
/// <param name="Name">What the dial is, under it: "CPU, right now".</param>
/// <param name="Percent">The reading as a share of its limit, 0 to 100; null when not read.</param>
/// <param name="Big">The big figure in the middle: "23 %".</param>
/// <param name="Absolute">The line under it: "0.23 of 1 vCPU".</param>
/// <param name="Amber">Where the amber mark sits, in percent.</param>
/// <param name="Red">Where the red mark sits, in percent.</param>
/// <param name="P95">The thirty-day 95th, in percent; null when not enough is kept.</param>
/// <param name="Peak">The thirty-day peak, in percent; null when not enough is kept.</param>
/// <param name="Tone">good, info, warning or alarm.</param>
public sealed record CloudDial(string Name, double? Percent, string Big, string Absolute, double Amber, double Red, double? P95, double? Peak, [property: Description("good, info, warning or alarm")] string Tone);

/// <summary>One sixty-minute sparkline.</summary>
/// <param name="Name">Its label: "CPU, 60 min".</param>
/// <param name="Values">The minutes' readings in percent, oldest first; null where nothing was read.</param>
public sealed record CloudSpark(string Name, IReadOnlyList<double?> Values);

/// <summary>The window's chart: two series in percent, the marks on it, and the line it is read against.</summary>
/// <param name="Title">"CPU and memory, 30 days".</param>
/// <param name="Series">The two series, each a name and its points.</param>
/// <param name="Marks">Deploys and events, each a moment and a kind.</param>
/// <param name="RedLine">Where the dashed red line sits, in percent.</param>
/// <param name="Legend">The legend in words, under the chart.</param>
/// <param name="StartMs">Where the chart starts, in milliseconds since the epoch, UTC.</param>
public sealed record CloudChart(string Title, IReadOnlyList<CloudSeries> Series, IReadOnlyList<CloudMark> Marks, double RedLine, string Legend, long StartMs)
{
    /// <summary>
    /// Spans shaded behind the series: each deploy, from its first configuration write to the
    /// new container answering, so a spike inside one reads as the roll it was. Empty, never null.
    /// </summary>
    public IReadOnlyList<CloudBand> Bands { get; init; } = [];
}

/// <summary>A span on the chart, shaded behind the series.</summary>
/// <param name="StartMs">Where it starts, in milliseconds since the epoch, UTC.</param>
/// <param name="EndMs">Where it ends.</param>
/// <param name="Title">What it was: the word Deploy, the version, and "a new container beside the old, then the swap".</param>
public sealed record CloudBand(long StartMs, long EndMs, string Title);

/// <summary>One series of the chart.</summary>
/// <param name="Name">What it is.</param>
/// <param name="Points">Its points, oldest first; a null value breaks the line.</param>
public sealed record CloudSeries(string Name, IReadOnlyList<CloudPoint> Points);

/// <summary>One point of a series.</summary>
/// <param name="AtMs">The start of the bucket, in milliseconds since the epoch, UTC.</param>
/// <param name="Value">The value in percent; null when not read.</param>
public sealed record CloudPoint(long AtMs, double? Value);

/// <summary>A moment marked on the chart.</summary>
/// <param name="AtMs">When, in milliseconds since the epoch, UTC.</param>
/// <param name="Kind">"deploy" for a gold line, "event" for a red dashed one.</param>
/// <param name="Title">What it was, for the hover.</param>
public sealed record CloudMark(long AtMs, string Kind, string Title);

/// <summary>One question row, in the Overview card's style.</summary>
/// <param name="Key">A stable name the page and the tests find it by.</param>
/// <param name="Question">"What does it power?".</param>
/// <param name="Answer">The big answer: "3 apps, running".</param>
/// <param name="Detail">The line under it, in pieces, any of which may link to a portal blade.</param>
/// <param name="Tone">good, info, warning or alarm.</param>
public sealed record CloudRow(string Key, string Question, string Answer, IReadOnlyList<CloudLink> Detail, [property: Description("good, info, warning or alarm")] string Tone)
{
    /// <summary>
    /// A list the row answers with, drawn as a table under it: the apps on the plan, a database's
    /// containers, a registry's images. Empty, never null, when the row has no list.
    /// </summary>
    public CloudTable Table { get; init; } = CloudTable.None;

    /// <summary>Where the row's answer comes from.</summary>
    public string? Source { get; init; }
}

/// <summary>A row's list as a table: its column names, and its rows as cells any of which may link to a portal blade.</summary>
/// <param name="Columns">The column names, in order.</param>
/// <param name="Rows">One list of cells a row, as many cells as columns.</param>
public sealed record CloudTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<CloudLink>> Rows)
{
    /// <summary>No table: no columns and no rows.</summary>
    public static readonly CloudTable None = new([], []);

    /// <summary>
    /// A table from a fact a card kept as text: one row a line, cells split by tabs. Facts are kept
    /// as text so every hour document has one shape; this turns the list back into rows.
    /// </summary>
    public static CloudTable FromFact(string? fact, params string[] columns) =>
        string.IsNullOrWhiteSpace(fact)
            ? None
            : new CloudTable(columns, [.. fact.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => (IReadOnlyList<CloudLink>)[.. line.Split('\t').Take(columns.Length).Select(cell => new CloudLink(cell, null))])]);

    /// <summary>Rows as a fact: one line a row, cells joined by tabs, with tabs and newlines in a cell made spaces.</summary>
    public static string ToFact(IEnumerable<IEnumerable<string?>> rows) =>
        string.Join('\n', rows.Select(row => string.Join('\t', row.Select(cell => (cell ?? "").Replace('\t', ' ').Replace('\n', ' ')))));
}

/// <summary>The readout beside the card: an uptime ring, then one line a reading.</summary>
/// <param name="Title">"The plan".</param>
/// <param name="UptimePercent">The share of the window the resource was up, 0 to 100; null when not read.</param>
/// <param name="UptimeLabel">Under the ring: "up, 30 days".</param>
/// <param name="Lines">The readings, in order.</param>
/// <param name="Footnote">Where the readings come from, in one sentence.</param>
public sealed record CloudReadout(string Title, double? UptimePercent, string UptimeLabel, IReadOnlyList<CloudLine> Lines, string Footnote);

/// <summary>One reading of the readout.</summary>
/// <param name="Label">"CPU now".</param>
/// <param name="Value">"23 %".</param>
/// <param name="Tone">good, info, warning or alarm.</param>
public sealed record CloudLine(string Label, string Value, [property: Description("good, info, warning or alarm")] string Tone)
{
    /// <summary>Where the reading comes from, printed under its label: "Azure Monitor", "the bill, Cost Management", "this process".</summary>
    public string? Source { get; init; }

    /// <summary>A name the page finds the line by, so a reworded label never loses it ("instances"); null for a line the page only prints.</summary>
    public string? Key { get; init; }
}
// #endregion cloud-wire
