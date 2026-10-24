// The two slots only the nightly Function's card fills (ADR-102, the addendum on the description and the
// processes): a paragraph saying what the resource is, read from its own code, and one section a process, each
// with its switches, its schedule, its last run, thirty days as a line and as a strip of days, and the way into
// its run history. Every other card leaves them empty, so the frame and the other cards are untouched.
using System.ComponentModel;

namespace TheYard.Application.Shared.Cards;

// #region cloud-processes
/// <summary>What the resource is, in one paragraph, and where the paragraph was read.</summary>
/// <param name="Text">The paragraph.</param>
/// <param name="Source">Where it was read: the files, the version, and where the switches come from.</param>
public sealed record CloudDescription(string Text, string Source);

/// <summary>One switch as a chip: its name and the value the app's settings hold.</summary>
/// <param name="Name">The setting's name: ATS_SWEEP.</param>
/// <param name="Value">on, shadow, off, or "not read".</param>
/// <param name="Tone">fine for on, info for shadow, off for anything else.</param>
public sealed record CloudSwitch(string Name, string Value, [property: Description("fine, info or off")] string Tone);

/// <summary>One labelled figure of a process section, with its source under it.</summary>
/// <param name="Label">What it is: "Last run".</param>
/// <param name="Text">The figure: "11:20 Central · 1 min 31 s · success".</param>
/// <param name="Source">Where it was read.</param>
public sealed record CloudFigure(string Label, string Text, string Source);

/// <summary>One of the resource's processes.</summary>
/// <param name="Key">Its name in the Function and the runs route: AtsSweep.</param>
/// <param name="Name">What the card calls it: "The inbox pass".</param>
/// <param name="Tone">good, info or off: its first switch's value, and the border's colour.</param>
/// <param name="Switches">Its switches as chips; empty for a process with none.</param>
/// <param name="Description">What it does, from its own summary comment.</param>
/// <param name="Schedule">Its schedule in words.</param>
/// <param name="Figures">Last run, what it read and wrote, thirty days, next run: four, in that order.</param>
/// <param name="Strip">One cell a Central day, oldest first: ok, late, fail, shadow, or skip where no run was.</param>
/// <param name="StripLabel">The strip's label: "runs, 30 days".</param>
/// <param name="StripTotal">The strip's total: "188".</param>
/// <param name="AllHref">Every run of it in Application Insights; null when there is no link.</param>
/// <param name="HistoryCount">The closed history's count, over thirty days.</param>
public sealed record CloudProcess(
    string Key,
    string Name,
    [property: Description("good, info or off")] string Tone,
    IReadOnlyList<CloudSwitch> Switches,
    string Description,
    string Schedule,
    IReadOnlyList<CloudFigure> Figures,
    IReadOnlyList<string> Strip,
    string StripLabel,
    string StripTotal,
    string? AllHref,
    string HistoryCount);
// #endregion cloud-processes
