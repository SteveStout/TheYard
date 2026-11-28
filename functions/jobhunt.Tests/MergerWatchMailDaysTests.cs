using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// noemailcard1127: the health route answers whether today is a no-email day and the day of the next campaign send the
/// file does not hold, read by the gate's own reader from the gate's own file, and null for both when the file does not
/// read, so the site's campaign card can hold on a listed day without a copy of the list. Fakes of the port only (RULE 79).
/// </summary>
public sealed class MergerWatchMailDaysTests
{
    /// <summary>J:\dispatch\NO-EMAIL-DAYS.txt as read 20:40 CST 11/27 (yearend1120's dates), its comment lines cut short.</summary>
    private const string File = "# RULES FIRST: read systemRules.md\n# NO-EMAIL-DAYS.txt: written 2026-11-19 by yearend1120.\n"
        + "2026-11-26\n2026-11-27\n2026-12-24\n2026-12-25\n2026-12-26\n2026-12-27\n2026-12-28\n2026-12-29\n2026-12-30\n2026-12-31\n2027-01-01\n";

    /// <summary>20:27 CST Friday 11/27, when Steve asked why the card was red.</summary>
    private static readonly DateTimeOffset Asked = new(2026, 11, 28, 2, 27, 0, TimeSpan.Zero);

    private static readonly MergerHealth Health = new(12, 0, 0, 0, null);

    [Fact]
    public async Task On_a_listed_day_the_route_says_so_and_names_the_next_open_morning()
    {
        var mail = await MergerWatch.MailDaysAsync(new Share(File), Asked, CancellationToken.None);
        var answer = MergerWatch.Answer(Health, mail);

        Assert.Equal((true, "2026-11-28"), mail);
        Assert.True(answer["no_email_today"]!.GetValue<bool>());
        Assert.Equal("2026-11-28", answer["next_email_day"]!.GetValue<string>());
        // The health route's own fields are all still there.
        Assert.Equal(12, answer["log_age_s"]!.GetValue<long>());
        Assert.NotNull(answer["switches"]);
        Assert.True(answer.ContainsKey("commit"));
    }

    [Theory]
    // 07:00 CST on an open day: the 8 o'clock send is still ahead, so it is today's.
    [InlineData("2026-11-25T13:00:00Z", false, "2026-11-25")]
    // 10:00 CST on 12/23: today's send went at 8, the next nine mornings are listed, so the next send is Sat 2 Jan.
    [InlineData("2026-12-23T16:00:00Z", false, "2027-01-02")]
    // 07:00 CST on Christmas Eve, a listed day: the next send is still Sat 2 Jan.
    [InlineData("2026-12-24T13:00:00Z", true, "2027-01-02")]
    // 08:30 CST on 11/28: the pass ran at 8, so the next send is tomorrow's.
    [InlineData("2026-11-28T14:30:00Z", false, "2026-11-29")]
    public async Task The_next_email_day_is_the_first_unlisted_morning_whose_send_is_still_ahead(string now, bool today, string next)
    {
        var mail = await MergerWatch.MailDaysAsync(new Share(File), DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);

        Assert.Equal((today, next), mail);
    }

    [Fact]
    public async Task A_file_that_is_missing_or_will_not_read_answers_null_for_both_never_false()
    {
        var missing = await MergerWatch.MailDaysAsync(new Share(null), Asked, CancellationToken.None);
        var refused = await MergerWatch.MailDaysAsync(new Share(File, refuse: true), Asked, CancellationToken.None);
        var answer = MergerWatch.Answer(Health, missing);

        Assert.Equal((null, null), missing);
        Assert.Equal((null, null), refused);
        Assert.True(answer.ContainsKey("no_email_today"));
        Assert.True(answer.ContainsKey("next_email_day"));
        Assert.Null(answer["no_email_today"]);
        Assert.Null(answer["next_email_day"]);
        Assert.Contains("\"no_email_today\":null", answer.ToJsonString(), StringComparison.Ordinal);
    }

    private sealed class Share(string? text, bool refuse = false) : IShareReader
    {
        public Task<bool> ExistsAsync(string path, CancellationToken ct) =>
            refuse ? throw new IOException("the share refused") : Task.FromResult(text is not null && path == NoEmailDays.File);

        public Task<byte[]> ReadAsync(string path, CancellationToken ct) => Task.FromResult(Encoding.UTF8.GetBytes(text ?? ""));

        public Task<MergeEntry?> StatAsync(string path, CancellationToken ct) => Task.FromResult<MergeEntry?>(null);

        public Task<IReadOnlyList<MergeEntry>> ListFilesAsync(string folder, CancellationToken ct) => Task.FromResult<IReadOnlyList<MergeEntry>>([]);

        public Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
