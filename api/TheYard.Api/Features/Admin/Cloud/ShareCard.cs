// The job-hunt share card (ADR: One frame for every resource, the addendum on the storage cards): the
// share's transactions this hour against the alert's own line, and its bytes against its quota, on the
// dials; listings, reads and writes for sixty minutes between them; transactions a day for thirty days;
// who is reading it by API, what it costs by day, whether anything is walking it, and what is on it. The
// share bills every listing, read and write, so the card is red only at the line that already emails.
using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The job-hunt share's card.</summary>
public static class ShareCard
{
    /// <summary>Where each row and readout line is read, by row key or line label (CardFrame.Sourced).</summary>
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["reading"] = "Azure Monitor, transactions by ApiName",
        ["cost"] = CardFrame.TheBill,
        ["walking"] = "Azure Monitor, listings against the last 7 days, and transactions by authentication",
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

    /// <summary>
    /// The walking row's lines, in listings an hour: amber where listings alone add about a quarter of a dollar a
    /// day at the Transaction optimized tier's list price (7,000 an hour), red at the alert's own line, which is
    /// read from the alert when it can be.
    /// </summary>
    public static readonly (double Amber, double Red) WalkingListings = (7_000, 20_000);

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
        var auth = Pairs(ResourceView.Fact(inputs, "auth_hour", ""));
        var rows = new List<CloudRow> { Reading(inputs, thisHour), CostRow(inputs), Walking(inputs, line, auth), OnIt(inputs) };
        string since = ResourceView.Fact(inputs, "auth_since", "") is { Length: > 0 } s ? $"since {s} UTC" : "in the clock hour under way";
        var readout = new CloudReadout(
            "The share, now",
            ResourceView.Uptime(inputs),
            $"read, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("Tier", ResourceView.Fact(inputs, "tier"), "info"),
                new("Redundancy", ShareKeeping.RedundancyWords(ResourceView.Fact(inputs, "sku", "")), "info"),
                new("Quota", quotaGb is { } q ? $"{q:0} GB, {Words(usedGb, "0.#")} used" : "not read", "info"),
                new("Last write seen", LastWrite(inputs), "info"),
                new("Who mounts it", auth.TryGetValue("SAS", out double sas) ? $"the laptop's mount and the chats, by SAS: {sas:N0} {since}" : "not read", "info"),
                new("Who reads it on a timer", auth.TryGetValue("AccountKey", out double key) ? $"the job hunt's Function, by the account key: {key:N0} {since}" : "not read", "info"),
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


    /// <summary>What it costs: today so far, yesterday and the month, and each day on the bill.</summary>
    private static CloudRow CostRow(ResourceCardInputs inputs)
    {
        string today = ResourceView.Fact(inputs, "cost_today", "");
        if (ResourceView.Fact(inputs, ShareKeeping.CostUnread, "") == "yes")
        {
            return new CloudRow("cost", "What does it cost?", "not read yet", [new(ShareKeeping.CostUnreadWords, null)], "info");
        }
        if (ResourceView.Fact(inputs, ShareKeeping.CostMissing, "") == "yes")
        {
            return new CloudRow("cost", "What does it cost?", "not on the bill yet", [new(ShareKeeping.CostMissingWords, null)], "info");
        }
        return new CloudRow("cost", "What does it cost?", today == "" ? "not on the bill yet" : $"${today}",
            [new($"today so far · yesterday ${ResourceView.Fact(inputs, "cost_yesterday", "0.00")} · this month ${ResourceView.Fact(inputs, "cost_month", "0.00")} · the bill is transactions, not gigabytes; Azure reports a day eight to twenty four hours late", null)], "info")
        { Table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("cost_rows"), "Day", "Cost", "What happened") };
    }

    /// <summary>
    /// Whether anything is walking it: listings this hour against the week's median busy hour (the median of the hours
    /// that listed anything; the quietest hour is 0 every night and says nothing), judged in money: amber over
    /// <see cref="WalkingListings"/>' amber line, red at the alert's line. The hour's split by caller sits inside the
    /// row, so a reader sees whether it was the mounts and chats or the Function.
    /// </summary>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="alertLine">The alert's line in transactions an hour, when it was read.</param>
    /// <param name="auth">Transactions in the clock hour under way, by authentication.</param>
    public static CloudRow Walking(ResourceCardInputs inputs, double? alertLine, IReadOnlyDictionary<string, double> auth)
    {
        var listings = Series.Decode(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("listings_hourly"));
        double? now = ResourceView.HourSum(inputs, "listings") ?? (listings.Count > 0 ? listings[^1].Value : null);
        if (now is null)
        {
            return new CloudRow("walking", "Is anything walking it?", "not read yet", [new("listings this hour against the week's median busy hour", null)], "info");
        }
        double? busy = Series.Percentile(listings.Select(l => l.Value).Where(v => v > 0), 50);
        double red = alertLine ?? WalkingListings.Red;
        string tone = now.Value >= red ? "alarm" : now.Value > WalkingListings.Amber ? "warning" : "good";
        string split = auth.Count == 0 ? "who made them is not read"
            : string.Create(CultureInfo.InvariantCulture, $"this clock hour by SAS (the laptop's mount and the chats) {auth.GetValueOrDefault("SAS"):N0} and by the account key (the job hunt's Function) {auth.GetValueOrDefault("AccountKey"):N0} transactions");
        return new CloudRow("walking", "Is anything walking it?", tone == "good" ? "No" : "Yes",
            [new(string.Create(CultureInfo.InvariantCulture, $"listings this hour {now.Value:N0} against a median busy hour of {(busy is { } b ? b.ToString("N0", CultureInfo.InvariantCulture) : "not read")} this week · amber over {WalkingListings.Amber:N0} an hour, about a quarter of a dollar a day · red at the alert's {red:N0} · {split}"), null)],
            tone);
    }

    /// <summary>
    /// Transactions by authentication in the clock hour under way: Azure Monitor's hour buckets start on the hour, and
    /// the hour before is left out so the split is one window, never two added together.
    /// </summary>
    /// <param name="byAuth">Hourly transactions by authentication.</param>
    /// <param name="hour">The start of the clock hour under way.</param>
    public static string AuthHour(IReadOnlyDictionary<string, IReadOnlyDictionary<DateTimeOffset, double>> byAuth, DateTimeOffset hour) =>
        string.Join(';', byAuth.Select(pair => $"{pair.Key}={pair.Value.Where(p => p.Key >= hour).Sum(p => p.Value).ToString("0", CultureInfo.InvariantCulture)}"));

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
        var marks = inputs.Hours.SelectMany(h => h.Events).Select(e => new CloudMark(e.At.ToUnixTimeMilliseconds(), CloudMarks.KindOf(e), $"{e.Title} on {e.Resource}")).ToList();
        long start = days.Count > 0 ? days[0].Day.ToUnixTimeMilliseconds() : (inputs.Now - TimeSpan.FromDays(30)).ToUnixTimeMilliseconds();
        string lineWords = dayLine is { } d ? $"the red line is {d.ToString("N0", CultureInfo.InvariantCulture)} a day ({line!.Value.ToString("N0", CultureInfo.InvariantCulture)} an hour), the alert" : "the alert's line is not read";
        return new CloudChart("Transactions each day, last 30 days", [new("Transactions a day", points) { Amber = Transactions.Amber, Red = Transactions.Red }, new("Used of the quota", used) { Amber = Quota.Amber, Red = Quota.Red }], marks, 100,
            $"teal transactions a day · green used of the quota · red the alert's line · {lineWords}", start);
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


    private static double? Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    private static string Words(double? value, string format) => value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : "not read";
}
