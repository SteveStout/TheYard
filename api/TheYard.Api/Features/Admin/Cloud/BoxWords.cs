// The box's card in words (ADR: The box reports itself): bytes, rates, temperatures and spans the way System
// Monitor says them, and the one scrub every word from the box passes before it reaches the wire, so an
// address or a port the agent read never reaches a public card.
using System.Globalization;
using System.Text.RegularExpressions;

namespace TheYard.Api.Features.Admin.Cloud;

// #region box-words
/// <summary>The box's figures in words, and the scrub that keeps addresses and ports off the card.</summary>
public static partial class BoxWords
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB"];

    /// <summary>A byte count in binary units: "8.5 GiB", "0 B".</summary>
    public static string Bytes(double bytes)
    {
        int unit = 0;
        while (Math.Abs(bytes) >= 1024 && unit < Units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes.ToString("0", CultureInfo.InvariantCulture)} B" : $"{bytes.ToString("0.0", CultureInfo.InvariantCulture)} {Units[unit]}";
    }

    /// <summary>A rate: "16.9 KiB/s".</summary>
    public static string Rate(double bytesPerSecond) => $"{Bytes(bytesPerSecond)}/s";

    /// <summary>A share to one place: "25.4 %".</summary>
    public static string Percent(double? share) => share is { } value ? $"{value.ToString("0.#", CultureInfo.InvariantCulture)} %" : "not read";

    /// <summary>A temperature: "41 °C".</summary>
    public static string Degrees(double? celsius) => celsius is { } value ? $"{value.ToString("0", CultureInfo.InvariantCulture)} °C" : "not read";

    /// <summary>A span in the largest two units: "3 d 4 h", "12 min", "40 s".</summary>
    public static string Span(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays} d {span.Hours} h"
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
        : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} min"
        : $"{Math.Max(0, (int)span.TotalSeconds)} s";

    /// <summary>
    /// Words from the box with every IPv4 or IPv6 address and every port taken out, so nothing the agent read about
    /// where the box sits on a network reaches the wire.
    /// </summary>
    public static string Scrub(string? words) =>
        string.IsNullOrEmpty(words) ? "" : Port().Replace(Ipv6().Replace(Ipv4().Replace(words, "an address"), "an address"), "a port").Trim();

    [GeneratedRegex(@"\b\d{1,3}(\.\d{1,3}){3}(:\d+)?\b")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"\b(?:[0-9a-fA-F]{1,4}:){1,7}(?::[0-9a-fA-F]{1,4}){1,7}\b|\b(?:[0-9a-fA-F]{1,4}:){4,7}[0-9a-fA-F]{1,4}\b|::[0-9a-fA-F]{1,4}\b")]
    private static partial Regex Ipv6();

    [GeneratedRegex(@"\bport \d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex Port();
}
// #endregion box-words
