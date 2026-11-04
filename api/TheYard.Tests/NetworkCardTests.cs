using System.Text.Json;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The network log's card: the cap and the states are the Function's own, the week dial reads this week against the
/// four before, the questions answer with nothing read and with reads, every row and line is sourced, and nothing on
/// the wire could be a name, an address or a code; the source's minute and hour from a fake reader.
/// </summary>
public sealed class NetworkCardTests
{
    // 09:00 CST on Wednesday 4 November 2026; the ISO week began Monday 2 November.
    private static readonly DateTimeOffset Nine = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);

    private static ResourceCardInputs Inputs(IReadOnlyDictionary<string, string?>? facts, double drafted = 0)
    {
        var values = new Dictionary<string, double?> { ["drafted"] = drafted, ["open"] = 12, ["started_week"] = 3 };
        var lastHour = Enumerable.Range(0, 60).Select(i => new ResourceMinute(Nine.AddMinutes(i - 60), values, "test")).ToList();
        var bucket = new ResourceBucket(Nine.AddMinutes(-5), 5, values, values, values, "test");
        IReadOnlyList<ResourceHour> hours = facts is null ? [] : [new ResourceHour(Nine.AddHours(-1), "/subscriptions/s/resourceGroups/RG-THEYARD-SS/providers/Microsoft.Sql/servers/sql-theyard-ss-westus3/databases/sqldb-theyard-ss-basic", null, facts, [])];
        return new ResourceCardInputs("24h", TimeSpan.FromDays(1), Nine, new PlanHistoryAvailability(true, "kept in the test"), facts is null ? [] : lastHour, [bucket], [bucket], hours, 1.02);
    }

    private static Dictionary<string, string?> Facts(string mode) => new(StringComparer.Ordinal)
    {
        ["switch"] = mode,
        ["weeks"] = "2026-10-05,2;2026-10-12,4;2026-10-19,4;2026-10-26,6;2026-11-02,3",
        ["open"] = "12",
        ["drafted"] = "5",
        ["quiet"] = "2",
        ["stopped"] = "1",
        ["closed"] = "40",
        ["database"] = "sqldb-theyard-ss-basic",
    };

    [Fact]
    public void The_cap_and_the_states_are_the_Function_s_own()
    {
        string due = File.ReadAllText(Path.Combine(Repo.Root(), "functions", "JobHunt.Domain", "Features", "NetworkLog", "DueRule.cs"));
        Assert.Contains($"public const int MondayCap = {NetworkCard.MondayCap};", due, StringComparison.Ordinal);
        string states = File.ReadAllText(Path.Combine(Repo.Root(), "functions", "JobHunt.Domain", "Features", "NetworkLog", "Conversation.cs"));
        Assert.All(NetworkCard.States, state => Assert.Contains($"    {state},", states, StringComparison.Ordinal));
        Assert.Equal(new DateOnly(2026, 11, 2), NetworkCard.Monday(new DateOnly(2026, 11, 8)));
    }

    [Fact]
    public void Read_the_card_answers_its_three_questions_with_this_week_against_the_four_before()
    {
        var card = NetworkCard.View(Inputs(Facts("shadow"), drafted: 5));
        Assert.Equal("The network log", card.Title);
        Assert.Equal("3", card.Dials[0].Big);
        Assert.Equal("against 4 a week over the four before", card.Dials[0].Absolute);
        Assert.Equal("5", card.Dials[1].Big);
        var logging = card.Rows.Single(r => r.Key == "logging");
        Assert.Equal("3 started this week", logging.Answer);
        Assert.Contains("newest first: 6, 4, 4, 2", logging.Detail[0].Text, StringComparison.Ordinal);
        Assert.Equal("5 drafted, waiting for Steve", card.Rows.Single(r => r.Key == "follow-ups").Answer);
        Assert.Equal("2 quiet after a follow-up", card.Rows.Single(r => r.Key == "silences").Answer);
        Assert.Equal(4, card.Rows.Count);
        Assert.Equal("nothing of its own this month", card.Rows.Single(r => r.Key == "cost").Answer);
        Assert.Equal("Thu 5 Nov, 6:00 AM Central", NetworkCard.Next(Nine));
    }

    [Fact]
    public void Unread_the_card_says_so_and_off_is_off()
    {
        var unread = NetworkCard.View(Inputs(null));
        Assert.All(unread.Rows.Where(r => r.Key != "cost"), row => Assert.Equal("not read yet", row.Answer));
        Assert.Equal("Off (off)", NetworkCard.View(Inputs(Facts("off"))).Rows.Single(r => r.Key == "logging").Answer);
    }

    [Fact]
    public void Nothing_on_the_wire_names_a_person_and_every_row_is_sourced()
    {
        var card = NetworkCard.View(Inputs(Facts("on"), drafted: 5));
        string wire = JsonSerializer.Serialize(card);
        Assert.DoesNotContain("@", wire.Replace("@@", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("linkedin:", wire, StringComparison.Ordinal);
        Assert.All(card.Rows, row => Assert.False(string.IsNullOrEmpty(row.Source), row.Key));
    }

    [Fact]
    public async Task The_source_reads_the_minute_and_the_hour_from_the_view()
    {
        var read = new NetworkCountsRead(true, null, [(new DateOnly(2026, 10, 26), 6), (new DateOnly(2026, 11, 2), 3)],
            new Dictionary<string, int>(StringComparer.Ordinal) { ["Open"] = 12, ["Drafted"] = 5, ["Quiet"] = 2 });
        var source = new NetworkSource(new Fixed(read), _ => Task.FromResult<string?>(null), _ => Task.FromResult<(string, string?)?>(null), "sqldb-theyard-ss-basic");
        var minute = Assert.Single(await source.MinutesAsync(Nine, Nine, "v", CancellationToken.None));
        Assert.Equal((5.0, 12.0, 3.0), (minute.Values["drafted"], minute.Values["open"], minute.Values["started_week"]));
        var hour = await source.HourAsync(Nine, Nine, CancellationToken.None);
        Assert.Equal("2026-10-26,6;2026-11-02,3", hour.Facts["weeks"]);
        Assert.Equal(("12", "5", "2", "0", "0"), (hour.Facts["open"], hour.Facts["drafted"], hour.Facts["quiet"], hour.Facts["stopped"], hour.Facts["closed"]));
        Assert.Null(hour.Facts["switch"]);
        var unread = await new NetworkSource(new Fixed(NetworkCountsRead.Unread("the network schema is not published yet")), _ => Task.FromResult<string?>(null), _ => Task.FromResult<(string, string?)?>(null), "db").HourAsync(Nine, Nine, CancellationToken.None);
        Assert.Equal("the network schema is not published yet", unread.Facts[ResourceView.MissingFact]);
        Assert.Empty(await new NetworkSource(new Fixed(NetworkCountsRead.Unread("no")), _ => Task.FromResult<string?>(null), _ => Task.FromResult<(string, string?)?>(null), "db").MinutesAsync(Nine, Nine, "v", CancellationToken.None));
    }

    private sealed class Fixed(NetworkCountsRead read) : INetworkCounts
    {
        public Task<NetworkCountsRead> ReadAsync(CancellationToken cancellation) => Task.FromResult(read);
    }
}
