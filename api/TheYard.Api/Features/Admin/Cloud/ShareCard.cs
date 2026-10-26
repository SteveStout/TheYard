// The job-hunt share card (ADR: One frame for every resource, the addendum on the storage cards): the
// share's transactions this hour against the alert's own line, and its bytes against its quota, on the
// dials; listings, reads and writes for sixty minutes between them; transactions a day for thirty days;
// who is reading it by API, what it costs by day, whether anything is walking it, and what is on it. The
// share bills every listing, read and write, so the card is red only at the line that already emails.
using System.Globalization;
using System.Text.Json;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The job-hunt share's card.</summary>
public static class ShareCard
{
    /// <summary>Where each row and readout line is read, by row key or line label (CardFrame.Sourced).</summary>
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["reading"] = "Azure Monitor, transactions by ApiName",
        ["cost"] = CardFrame.TheBill,
        ["walking"] = "Azure Monitor, listings against the last 7 days",
        ["on"] = "Azure Monitor, file count and capacity",
        ["Tier"] = "Azure Resource Manager",
        ["Redundancy"] = "Azure Resource Manager",
        ["Quota"] = "Azure Files share properties",
        ["Last write seen"] = CardFrame.KeptMinutes,
        ["Who mounts it"] = "Azure Monitor, transactions by authentication",
        ["Who reads it on a timer"] = "Azure Monitor, transactions by authentication",
        ["The alert"] = "Azure Monitor alert rule",
        ["Cost"] = CardFrame.TheBill,
        ["Live read"] = CardFrame.ThisProcess,
    };

    /// <summary>The share's API names that list a folder, read a file or its properties, and write.</summary>
    public const string ListingFilter = "ApiName eq 'ListFiles' or ApiName eq 'QueryDirectory' or ApiName eq 'ListShares'";
    public const string ReadFilter = "ApiName eq 'GetFile' or ApiName eq 'GetFileProperties' or ApiName eq 'GetDirectoryProperties' or ApiName eq 'Read' or ApiName eq 'QueryInfo' or ApiName eq 'GetShareProperties'";
    public const string WriteFilter = "ApiName eq 'PutRange' or ApiName eq 'CreateFile' or ApiName eq 'CreateDirectory' or ApiName eq 'SetFileProperties' or ApiName eq 'SetDirectoryProperties' or ApiName eq 'SetDirectoryMetadata' or ApiName eq 'Write' or ApiName eq 'SetInfo' or ApiName eq 'Create' or ApiName eq 'RenameFile' or ApiName eq 'DeleteFile' or ApiName eq 'Flush'";

    /// <summary>The metrics the card keeps a minute.</summary>
    public static readonly IReadOnlyList<AzureMetric> Metrics =
    [
        new("Transactions", "total", "transactions"),
        new("Transactions", "total", "listings", Filter: ListingFilter),
        new("Transactions", "total", "reads", Filter: ReadFilter),
        new("Transactions", "total", "writes", Filter: WriteFilter),
        new("FileCapacity", "average", "used_gb", 1.0 / 1_000_000_000, Interval: "PT1H"),
        new("FileCount", "average", "files", Interval: "PT1H"),
    ];

    /// <summary>The transactions dial: amber at half the alert's line, red at the line.</summary>
    public static readonly (double Amber, double Red) Transactions = (50, 100);

    /// <summary>The quota dial: amber at 80 % used, red at 90 %.</summary>
    public static readonly (double Amber, double Red) Quota = (80, 90);

    public static CloudReport View(ResourceCardInputs inputs)
    {
        double? line = Number(ResourceView.Fact(inputs, "alert_threshold", ""));
        double? quotaGb = Number(ResourceView.Fact(inputs, "quota_gb", ""));
        var hourly = Series.Decode(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("tx_hourly"));
        double? thisHour = ResourceView.HourSum(inputs, "transactions") ?? (hourly.Count > 0 ? hourly[^1].Value : null);
        double? usedGb = ResourceView.Now(inputs, "used_gb") ?? Number(ResourceView.Fact(inputs, "used_gb", ""));
        double? p95 = Series.Percentile(hourly.Select(h => h.Value), 95);
        double? txShare = ResourceView.Share(thisHour, line);
        // The hour's own sum is the reading, so its tone is the sum's, not a held run of minutes.
        string txTone = txShare is not { } t ? "info" : t >= Transactions.Red ? "alarm" : t >= Transactions.Amber ? "warning" : "good";
        var dials = new[]
        {
            new CloudDial("Transactions this hour", txShare, ResourceView.Number(thisHour), line is { } l ? $"of {l.ToString("N0", CultureInfo.InvariantCulture)}, the alert's line" : "the alert's line is not read",
                Transactions.Amber, Transactions.Red, ResourceView.Share(p95, line), ResourceView.Share(hourly.Count == 0 ? null : hourly.Max(h => h.Value), line), txTone),
            ResourceView.Dial(inputs, "used_gb", "Used of the quota", quotaGb, Quota, v => v is { } g ? $"of {Words(quotaGb, "0")} GB · {PlanView.Percent(ResourceView.Share(g, quotaGb))}" : "not read", hourly: true)
                with { Big = usedGb is { } u ? $"{u.ToString("0.#", CultureInfo.InvariantCulture)} GB" : "not read" },
        };
        double perMinute = line is { } limit ? limit / 60 : 0;
        var rows = new List<CloudRow> { Reading(inputs, thisHour), CostRow(inputs), Walking(inputs), OnIt(inputs) };
        var auth = Pairs(ResourceView.Fact(inputs, "auth_hour", ""));
        var readout = new CloudReadout(
            "The share, now",
            ResourceView.Uptime(inputs),
            $"read, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("Tier", ResourceView.Fact(inputs, "tier"), "info"),
                new("Redundancy", RedundancyWords(ResourceView.Fact(inputs, "sku", "")), "info"),
                new("Quota", quotaGb is { } q ? $"{q:0} GB, {Words(usedGb, "0.#")} used" : "not read", "info"),
                new("Last write seen", LastWrite(inputs), "info"),
                new("Who mounts it", auth.TryGetValue("SAS", out double sas) ? $"the laptop's mount and the chats, by SAS: {sas:N0} in the last hour" : "not read", "info"),
                new("Who reads it on a timer", auth.TryGetValue("AccountKey", out double key) ? $"the job hunt's Function, by the account key: {key:N0} in the last hour" : "not read", "info"),
                new("The alert", line is { } a ? $"{a.ToString("N0", CultureInfo.InvariantCulture)} transactions an hour, to his phone ({(ResourceView.Fact(inputs, "alert_enabled", "") == "true" ? "on" : "off")})" : "not read", "info"),
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            "The share bills for looking, not for storing: a listing of a folder is a transaction. Everything on this card is read from Azure; nothing is typed.");
        var report = new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && (inputs.LastHour.Count > 0 || inputs.Hours.Count > 0),
            ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "minute")),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "The job-hunt share",
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("spec"),
            ResourceView.Resource(inputs),
            dials,
            [ResourceView.Spark(inputs, "listings", "Listings, 60 min", perMinute), ResourceView.Spark(inputs, "reads", "Reads, 60 min", perMinute), ResourceView.Spark(inputs, "writes", "Writes, 60 min", perMinute)],
            DailyChart(inputs, hourly, line),
            rows,
            readout);
        return CardFrame.Sourced(report, Sources);
    }

    /// <summary>Who is reading it: the transactions of the last hour, and the table by API for the last hour and yesterday.</summary>
    private static CloudRow Reading(ResourceCardInputs inputs, double? thisHour)
    {
        string log = ResourceView.Fact(inputs, "diagnostic_log", "off") == "on" ? "the share's diagnostic log is on; read it in the portal for each caller" : "turn on the share's diagnostic log to see who, by name";
        var table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("api_rows"), "API", "Last hour", "Yesterday");
        return new CloudRow("reading", "Who is reading it?", thisHour is null ? "not read yet" : ResourceView.Number(thisHour),
            [new($"transactions in the last hour, by what was asked for · {log}", null)], "info")
        { Table = table };
    }

    /// <summary>The fact an hour carries when the bill had not been read when it was kept.</summary>
    public const string CostUnread = "cost_unread";

    /// <summary>The answer when the hour was kept before the bill was read: why, in words.</summary>
    public const string CostUnreadWords = "the bill had not been read when this hour was kept: the site reads it a minute after it starts, and Cost Management can ask it to wait an hour";

    /// <summary>What it costs: today so far, yesterday and the month, and each day on the bill.</summary>
    private static CloudRow CostRow(ResourceCardInputs inputs)
    {
        string today = ResourceView.Fact(inputs, "cost_today", "");
        if (ResourceView.Fact(inputs, CostUnread, "") == "yes")
        {
            return new CloudRow("cost", "What does it cost?", "not read yet", [new(CostUnreadWords, null)], "info");
        }
        return new CloudRow("cost", "What does it cost?", today == "" ? "not on the bill yet" : $"${today}",
            [new($"today so far · yesterday ${ResourceView.Fact(inputs, "cost_yesterday", "0.00")} · this month ${ResourceView.Fact(inputs, "cost_month", "0.00")} · the bill is transactions, not gigabytes; Azure reports a day eight to twenty four hours late", null)], "info")
        { Table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("cost_rows"), "Day", "Cost", "What happened") };
    }

    /// <summary>Whether anything is walking it: listings this hour against the quiet hour of the last seven days (their 5th percentile).</summary>
    public static CloudRow Walking(ResourceCardInputs inputs)
    {
        var listings = Series.Decode(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("listings_hourly"));
        double? now = ResourceView.HourSum(inputs, "listings") ?? (listings.Count > 0 ? listings[^1].Value : null);
        double? quiet = Series.Percentile(listings.Select(l => l.Value), 5);
        if (now is null || quiet is null)
        {
            return new CloudRow("walking", "Is anything walking it?", "not read yet", [new("listings this hour against the quiet hour of the last seven days", null)], "info");
        }
        bool walking = now.Value > Math.Max(1000, 4 * quiet.Value);
        return new CloudRow("walking", "Is anything walking it?", walking ? "Yes" : "No",
            [new($"listings this hour {now.Value:N0} against a quiet hour of {quiet.Value:N0} · a walker is four times the quiet hour and over a thousand · the laptop's mount and exclusions are written in J:\\staging\\cost1024\\rung2.txt, which this site does not read", null)],
            walking ? "warning" : "good");
    }

    /// <summary>What is on it: the files and bytes Azure counts.</summary>
    private static CloudRow OnIt(ResourceCardInputs inputs)
    {
        double? files = ResourceView.Now(inputs, "files");
        return new CloudRow("on", "What is on it?", files is { } f ? $"{f:N0} files" : "not read yet",
            [new($"{Words(ResourceView.Now(inputs, "used_gb"), "0.#")} GB · the largest folders need a listing of the whole share, which bills for every folder; no census is kept by this site yet", null)], "info");
    }

    /// <summary>Transactions a day for thirty days against the alert's line times 24, and the quota used a day.</summary>
    private static CloudChart DailyChart(ResourceCardInputs inputs, IReadOnlyList<(DateTimeOffset At, double Value)> hourly, double? line)
    {
        var days = Series.Daily(hourly);
        double? dayLine = line * 24;
        var points = days.Select(d => new CloudPoint(d.Day.ToUnixTimeMilliseconds(), ResourceView.Share(d.Value, dayLine))).ToList();
        var used = inputs.Buckets.Select(b => new CloudPoint(b.At.ToUnixTimeMilliseconds(), ResourceView.Share(b.Means.GetValueOrDefault("used_gb"), Number(ResourceView.Fact(inputs, "quota_gb", ""))))).ToList();
        var marks = inputs.Hours.SelectMany(h => h.Events).Select(e => new CloudMark(e.At.ToUnixTimeMilliseconds(), "event", $"{e.Title} on {e.Resource}")).ToList();
        long start = days.Count > 0 ? days[0].Day.ToUnixTimeMilliseconds() : (inputs.Now - TimeSpan.FromDays(30)).ToUnixTimeMilliseconds();
        string lineWords = dayLine is { } d ? $"the red line is {d.ToString("N0", CultureInfo.InvariantCulture)} a day ({line!.Value.ToString("N0", CultureInfo.InvariantCulture)} an hour), the alert" : "the alert's line is not read";
        return new CloudChart("Transactions each day, last 30 days", [new("Transactions a day", points), new("Used of the quota", used)], marks, 100,
            $"teal transactions a day · green used of the quota · red the alert's line · {lineWords}", start);
    }

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
        string today = hour.UtcDay, yesterday = hour.AddDays(-1).UtcDay, month = today[..7];
        facts["cost_today"] = byDay.GetValueOrDefault(today).ToString("0.00", CultureInfo.InvariantCulture);
        facts["cost_yesterday"] = byDay.GetValueOrDefault(yesterday).ToString("0.00", CultureInfo.InvariantCulture);
        facts["cost_month"] = byDay.Where(d => d.Key.StartsWith(month, StringComparison.Ordinal)).Sum(d => d.Value).ToString("0.00", CultureInfo.InvariantCulture);
        facts["cost_rows"] = CloudTable.ToFact(byDay.OrderByDescending(d => d.Key, StringComparer.Ordinal).Take(7).Reverse()
            .Select(d => new[] { d.Key, $"${d.Value.ToString("0.00", CultureInfo.InvariantCulture)}", "" }));
    }

    /// <summary>The newest minute the kept minutes saw a write, in words.</summary>
    private static string LastWrite(ResourceCardInputs inputs) =>
        inputs.LastHour.LastOrDefault(m => m.Values.GetValueOrDefault("writes") is > 0) is { } minute
            ? $"{minute.At.UtcDateTime:HH:mm} UTC, {minute.Values["writes"]:N0} writes that minute"
            : inputs.LastHour.Count == 0 ? "not read yet" : "none in the last sixty minutes";

    /// <summary>"AccountKey=6048;SAS=459" as pairs.</summary>
    private static Dictionary<string, double> Pairs(string fact) =>
        fact.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=')).Where(p => p.Length == 2 && Number(p[1]) is not null)
            .ToDictionary(p => p[0], p => Number(p[1])!.Value, StringComparer.Ordinal);

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

    private static double? Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    private static string Words(double? value, string format) => value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : "not read";
}
