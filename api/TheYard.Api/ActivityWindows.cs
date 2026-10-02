// The windows the activity graph offers and the bucket each is drawn in. Its
// own file because the activity endpoints and the kept logs both read it; part
// of the report region, whose other types are listed in Activity.cs.
namespace TheYard.Api;

// #region report
/// <summary>The windows the graph offers, and the bucket each one is drawn in.</summary>
public static class ActivityWindows
{
    /// <summary>
    /// The length, bucket and name of a window: "24h" by the hour (the default
    /// when none is named), "7d" by six hours, "30d" by the day. Null for any
    /// other name, so the caller can answer that it does not exist.
    /// </summary>
    public static (TimeSpan Length, TimeSpan Bucket, string Name)? Parse(string? window) => (window ?? "24h") switch
    {
        "24h" => (TimeSpan.FromHours(24), TimeSpan.FromHours(1), "24h"),
        "7d" => (TimeSpan.FromDays(7), TimeSpan.FromHours(6), "7d"),
        "30d" => (TimeSpan.FromDays(30), TimeSpan.FromDays(1), "30d"),
        _ => null,
    };
}
// #endregion report
