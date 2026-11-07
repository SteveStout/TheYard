using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The box's readings as its agent posts them, built by hand for the box's tests (ADR: The box reports itself): four
/// cores, 32 GB, the array of six, two GPUs, the package sensor and the services the job hunt runs on. Each test
/// changes the one part it is about.
/// </summary>
internal static class BoxSamples
{
    /// <summary>09:00 CST on Saturday 7 November 2026.</summary>
    public static readonly DateTimeOffset Nine = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

    /// <summary>A whole, quiet snapshot read at <paramref name="at"/>.</summary>
    public static BoxSnapshot Snapshot(DateTimeOffset? at = null) => new(
        "WickedAwsome",
        "6.14.0-35-generic",
        "Linux Mint 22.3",
        Nine.AddDays(-3),
        273_600,
        [1.12, 1.3, 1.05],
        [new(0, 26.8, 4000, 41), new(1, 30.9, 4000, 42), new(2, 28.1, 3980, 40), new(3, 27.1, 4000, 41)],
        new BoxMemory(33_600_000_000, 8_500_000_000, 18_800_000_000, 6_300_000_000),
        new BoxSwap(2_100_000_000, 0),
        [new("enp0s31f6", 17_300, 722_800, 2_040_000_000, 42_520_000_000), new("tailscale0", 410, 2150, 222_000_000, 1_180_000_000), new("lo", 900, 900, 5_000_000, 5_000_000)],
        [new("/", "md126p2", "ext4", 2_800_000_000_000, 18_400_000_000, "active raid0", 6, 6), new("/boot/efi", "md126p1", "vfat", 1_000_000_000, 6_000_000, null, null, null)],
        [new(0, "NVIDIA GeForce RTX 2080", 3, 412_000_000, 8_192_000_000, 36, 97, 100, 18, 215, "P8"), new(1, "NVIDIA GeForce RTX 2080", 2, 5_000_000, 8_192_000_000, 34, 97, 100, 9, 215, "P8")],
        [new("coretemp Package id 0", 41, 82, 100), new("acpitz temp1", 30, null, null)],
        [
            new BoxService("claude-remote-control.service", "user", "active", "running", Nine.AddDays(-3), null),
            new("jdrive.service", "user", "active", "running", Nine.AddDays(-3), null),
            new("theyard-box-agent.service", "user", "active", "running", Nine.AddMinutes(-12), null),
            new("app-dev.lizardbyte.app.Sunshine.service", "user", "inactive", "dead", null, null),
            new("jobhunt-6am.timer", "user", "active", "waiting", Nine.AddDays(-3), "next Sun 06:00"),
            new("jobhunt-6am.service", "user", "inactive", "dead", null, "last result success"),
            new("tailscaled.service", "system", "active", "running", Nine.AddDays(-3), null),
            new("ssh.service", "system", "active", "running", Nine.AddDays(-3), null),
        ],
        "1",
        0.3,
        at ?? Nine.AddSeconds(-20));

    /// <summary>A live sample read at <paramref name="at"/>.</summary>
    public static BoxLiveSample Sample(DateTimeOffset at) =>
        new(at, [26.8, 30.9, 28.1, 27.1], 8_500_000_000, 33_600_000_000, 0, 2_100_000_000, [new("enp0s31f6", 17_300, 722_800)], [new(0, 3), new(1, 2)]);

    /// <summary>A clock that stands still.</summary>
    public sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        /// <summary>Where it stands.</summary>
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
