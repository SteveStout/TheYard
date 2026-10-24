using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>Where a role sits: fully remote first (targeting-criteria.md), St. Louis hybrid eligible, onsite a kill.</summary>
public enum RemoteStance
{
    /// <summary>Not stated by the posting or the company.</summary>
    Unknown,

    /// <summary>Fully remote, US.</summary>
    Remote,

    /// <summary>Hybrid inside St. Louis at one day a week or less.</summary>
    Hybrid,

    /// <summary>Onsite, or hybrid outside the rule.</summary>
    Onsite,
}

/// <summary>The stored facts a lead's Strength is computed from; the same facts always give the same score.</summary>
public sealed record StrengthInputs(
    RemoteStance Remote,
    int DotNetEvidence,
    bool PostedRole,
    int? BandTop,
    int? GhostScore,
    DateOnly FirstSeen,
    DateOnly? LastDotNetPostingAt);

/// <summary>
/// The send order's number (the review, F9): a formula over stored facts, the same inputs the same score, so
/// which company gets today's email never depends on the last model call. The model adds one bounded step of
/// plus or minus one with its StrengthWhy, written once per lead and again only when <see cref="Hash"/> changes.
/// The points:
/// remote 3, St. Louis hybrid 1, unknown 0, onsite minus 3;
/// .NET evidence rows 1 each up to 3;
/// a posted role (Track A) 2;
/// the band's top at $170,000 or more 2, at the $155,000 floor or more 1, unpublished 0, below the floor minus 2;
/// ghost score 0 nothing, 1 or 2 minus 1, 3 or more minus 25 (the 10/24 fragment: 3 or more drops below every
/// lead scoring 0 to 2, whatever its pay; every other lead scores minus 8 to 13, so minus 25 puts it under all of them);
/// first seen in the last 7 days 1, more than 60 days ago minus 1;
/// a .NET posting in the last 30 days 1 (8.2, Track B ranks by recency).
/// </summary>
public static class Strength
{
    /// <summary>The permanent floor (targeting-criteria.md, 9/15).</summary>
    public const int Floor = 155_000;

    /// <summary>The total-comp line (targeting-criteria.md, 9/16).</summary>
    public const int Line = 170_000;

    /// <summary>The ghost score of 3 or more: larger than the whole range the other points span.</summary>
    public const int GhostPenalty = -25;

    /// <summary>The formula's version, in the hash, so a change to the points re-ranks every lead once.</summary>
    public const string Version = "1";

    /// <summary>The score on a Central day, with the model's step clamped to plus or minus one.</summary>
    public static int Score(StrengthInputs inputs, DateOnly today, int modelStep = 0)
    {
        int remote = inputs.Remote switch
        {
            RemoteStance.Remote => 3,
            RemoteStance.Hybrid => 1,
            RemoteStance.Onsite => -3,
            _ => 0,
        };
        int evidence = Math.Clamp(inputs.DotNetEvidence, 0, 3);
        int posted = inputs.PostedRole ? 2 : 0;
        int band = inputs.BandTop switch
        {
            null => 0,
            >= Line => 2,
            >= Floor => 1,
            _ => -2,
        };
        int ghost = inputs.GhostScore switch
        {
            null or <= 0 => 0,
            <= 2 => -1,
            _ => GhostPenalty,
        };
        int age = today.DayNumber - inputs.FirstSeen.DayNumber;
        int fresh = age <= 7 ? 1 : age > 60 ? -1 : 0;
        int recent = inputs.LastDotNetPostingAt is { } last && today.DayNumber - last.DayNumber is >= 0 and <= 30 ? 1 : 0;
        return remote + evidence + posted + band + ghost + fresh + recent + Math.Clamp(modelStep, -1, 1);
    }

    /// <summary>
    /// RankInputsHash: 16 hex digits of the SHA-256 of the inputs written out in order. The dates are stored,
    /// not the ages, so the hash moves only when a fact moves, never because a day passed.
    /// </summary>
    public static string Hash(StrengthInputs inputs)
    {
        string line = string.Join('|',
            Version,
            inputs.Remote.ToString(),
            inputs.DotNetEvidence.ToString(CultureInfo.InvariantCulture),
            inputs.PostedRole ? "1" : "0",
            inputs.BandTop?.ToString(CultureInfo.InvariantCulture) ?? "",
            inputs.GhostScore?.ToString(CultureInfo.InvariantCulture) ?? "",
            inputs.FirstSeen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            inputs.LastDotNetPostingAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line)))[..16];
    }
}
