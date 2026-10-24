namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// The campaign's numbers, as constants a test reads (the review, F4): ten emails a Central day at most, and on
/// the first three days the switch is `on`, three, then five, then ten, so a defect on the first morning costs
/// three emails from Steve's own Gmail and not ten. Shadow writes the full ten as would-sends.
/// </summary>
public static class CampaignRules
{
    /// <summary>The most emails one Central day may carry, follow-ups included.</summary>
    public const int DailyCap = 10;

    /// <summary>The cap on the first, second and third day of `on`; the fourth day on is <see cref="DailyCap"/>.</summary>
    public static readonly IReadOnlyList<int> Ramp = [3, 5, 10];

    /// <summary>The send's spacing inside one firing (F7: seconds, not minutes, at ten messages).</summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Today's cap: the ramp's figure for the day of `on` (1 is the first day), the daily cap after it and in
    /// shadow (dayOfOn null), never more than the daily cap.
    /// </summary>
    public static int CapFor(int? dayOfOn) =>
        dayOfOn is int day and >= 1 && day <= Ramp.Count ? Math.Min(Ramp[day - 1], DailyCap) : DailyCap;

    /// <summary>How many more may go today, given the day's count so far: never below zero.</summary>
    public static int Left(int sentToday, int? dayOfOn) => Math.Max(0, CapFor(dayOfOn) - Math.Max(0, sentToday));
}
