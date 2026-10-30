// What a kept hour of the job-hunt share carries, and the words it is read back in: the share's own
// record, the table of who asked for what, and the account's days on the bill. Its own file because
// writing a fact into the hour the recorder keeps is a different job from drawing a row out of one,
// which is ShareCard.cs; this side changes when Azure's record or the bill's shape does, and that
// side changes when the card does.
using System.Globalization;
using System.Text.Json;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>
/// The facts the storage readings write into the share's kept hour, and the two words-for-a-code
/// helpers both sides read them with (ADR: One frame for every resource, the addendum on the
/// storage cards).
/// </summary>
public static class ShareKeeping
{
    /// <summary>The fact an hour carries when the bill had not been read when it was kept.</summary>
    public const string CostUnread = "cost_unread";

    /// <summary>The answer when the hour was kept before the bill was read: why, in words.</summary>
    public const string CostUnreadWords = "the bill had not been read when this hour was kept: the site reads it a minute after it starts, and Cost Management can ask it to wait an hour";

    /// <summary>The fact an hour carries when the bill was read and holds no line under the storage account's name.</summary>
    public const string CostMissing = "cost_missing";

    /// <summary>The answer when the bill holds no line for the account: never a zero, which would say it was free.</summary>
    public const string CostMissingWords = "the bill read holds no line under the storage account's name in the last 34 days, so the month is not read rather than shown as free";

    /// <summary>The share's record: tier, quota and bytes used.</summary>
    public static void ShareFacts(Dictionary<string, string?> facts, JsonElement share)
    {
        facts["tier"] = Tier(AzureMetrics.Text(share, "properties.accessTier"));
        facts["quota_gb"] = AzureMetrics.Text(share, "properties.shareQuota");
        if (long.TryParse(AzureMetrics.Text(share, "properties.shareUsageBytes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long used))
        {
            facts["used_gb"] = (used / 1_000_000_000.0).ToString("0.##", CultureInfo.InvariantCulture);
        }
        facts["spec"] = string.Join(" · ", new[] { "Azure Files", Redundancy(facts.GetValueOrDefault("sku") ?? ""), facts["tier"], facts["quota_gb"] is { } q ? $"{q} GB quota" : null }.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>The table by API: the last hour and yesterday, the busiest first, the first eight.</summary>
    public static string ApiRows(IReadOnlyDictionary<string, IReadOnlyDictionary<DateTimeOffset, double>> byApi, DateTimeOffset today, DateTimeOffset hour) =>
        CloudTable.ToFact(byApi
            .Select(api => (Api: api.Key, Hour: api.Value.Where(p => p.Key >= hour.AddHours(-1)).Sum(p => p.Value), Yesterday: api.Value.Where(p => p.Key >= today.AddDays(-1) && p.Key < today).Sum(p => p.Value)))
            .Where(a => a.Hour + a.Yesterday > 0)
            .OrderByDescending(a => a.Hour).ThenByDescending(a => a.Yesterday).ThenBy(a => a.Api, StringComparer.Ordinal)
            .Take(8)
            .Select(a => new[] { a.Api, a.Hour.ToString("N0", CultureInfo.InvariantCulture), a.Yesterday.ToString("N0", CultureInfo.InvariantCulture) }));

    /// <summary>The account's days on the bill: today, yesterday, the month, and each day of the last week with the Activity Log's events that day.</summary>
    public static void CostFacts(Dictionary<string, string?> facts, CostKept kept, string account, DateTimeOffset hour)
    {
        // A bill with no day for any resource has not been read in this process yet: the site reads it a minute after
        // it starts, and Cost Management can ask it to wait an hour. That is "not read", never a day that cost $0.00.
        if (kept.Days.Count == 0)
        {
            facts[CostUnread] = "yes";
            return;
        }
        var byDay = kept.Days.Where(d => string.Equals(d.Resource, account, StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => d.Day).ToDictionary(g => g.Key, g => g.Sum(d => d.Cost), StringComparer.Ordinal);
        if (byDay.Count == 0)
        {
            facts[CostMissing] = "yes";
            return;
        }
        string today = hour.UtcDay, yesterday = hour.AddDays(-1).UtcDay, month = today[..7];
        facts["cost_today"] = byDay.GetValueOrDefault(today).ToString("0.00", CultureInfo.InvariantCulture);
        facts["cost_yesterday"] = byDay.GetValueOrDefault(yesterday).ToString("0.00", CultureInfo.InvariantCulture);
        facts["cost_month"] = byDay.Where(d => d.Key.StartsWith(month, StringComparison.Ordinal)).Sum(d => d.Value).ToString("0.00", CultureInfo.InvariantCulture);
        facts["cost_rows"] = CloudTable.ToFact(byDay.OrderByDescending(d => d.Key, StringComparer.Ordinal).Take(7).Reverse()
            .Select(d => new[] { d.Key, $"${d.Value.ToString("0.00", CultureInfo.InvariantCulture)}", "" }));
    }

    /// <summary>The tier in the portal's words.</summary>
    public static string? Tier(string? tier) => tier switch
    {
        "TransactionOptimized" => "Transaction optimized",
        null => null,
        _ => tier,
    };

    /// <summary>The SKU's redundancy in the portal's words: Standard_LRS is "Standard LRS".</summary>
    public static string Redundancy(string sku) => sku switch
    {
        "" => "not read",
        _ => sku.Replace('_', ' '),
    };

    /// <summary>The redundancy in full: Standard_LRS is "Locally redundant (LRS)".</summary>
    public static string RedundancyWords(string sku) =>
        sku.EndsWith("_LRS", StringComparison.Ordinal) ? "Locally redundant (LRS)"
        : sku.EndsWith("_ZRS", StringComparison.Ordinal) ? "Zone redundant (ZRS)"
        : sku.EndsWith("_GRS", StringComparison.Ordinal) ? "Geo redundant (GRS)"
        : Redundancy(sku);
}
