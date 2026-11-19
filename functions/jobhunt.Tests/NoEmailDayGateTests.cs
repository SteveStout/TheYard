using System.Text;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// selfheal1120 version 2: on a day J:\dispatch\NO-EMAIL-DAYS.txt lists, the Function's one sender lets through only
/// mail to Steve's own address. Fakes of the ports only (RULE 79): no share, no Gmail.
/// </summary>
public sealed class NoEmailDayGateTests
{
    /// <summary>Thanksgiving, 08:00 Central (14:00 UTC): the campaign's hour on a listed day.</summary>
    private static readonly DateTimeOffset Thanksgiving = new(2026, 11, 26, 14, 0, 5, TimeSpan.Zero);

    /// <summary>The file as yearend1120 wrote it on 11/19 (read 14:3x CST), with a range and a typo added.</summary>
    private const string File = "# RULES FIRST: read systemRules.md\n# NO-EMAIL-DAYS.txt: one Central date a line\n2026-11-26\n2026-11-27  # the Friday\n"
        + "2026-12-24..2027-01-01\n2026-13-40\n\n";

    [Fact]
    public void The_file_lists_dates_and_ranges_and_a_typo_lists_nothing()
    {
        var days = NoEmailDays.Parse(File);

        Assert.True(days.Read);
        Assert.True(days.Listed(new DateOnly(2026, 11, 26)));
        Assert.True(days.Listed(new DateOnly(2026, 11, 27)));
        Assert.True(days.Listed(new DateOnly(2026, 12, 31)));
        Assert.True(days.Listed(new DateOnly(2027, 1, 1)));
        Assert.False(days.Listed(new DateOnly(2026, 11, 25)));
        Assert.False(days.Listed(new DateOnly(2027, 1, 2)));
        Assert.Equal(["2026-13-40"], days.Bad);
    }

    [Fact]
    public async Task On_a_listed_day_mail_to_anyone_but_his_own_address_is_held_and_the_card_passes()
    {
        var inner = new CampaignSendTests.CountingSender();
        var gate = new NoEmailDayGate(inner, NoEmailDays.Parse(File), new At(Thanksgiving), TodayMail.To);

        var held = await Assert.ThrowsAsync<MailHeldException>(() => gate.SendAsync(new OutgoingMail("hiring@acme.com", "Hello", "t", "h"), CancellationToken.None));
        await Assert.ThrowsAsync<MailHeldException>(() => gate.SendAsync(new OutgoingMail($"{TodayMail.To}, hiring@acme.com", "Two", "t", "h"), CancellationToken.None));
        await gate.SendAsync(new OutgoingMail(TodayMail.To, "TODAY", "card", "card"), CancellationToken.None);
        await gate.SendAsync(new OutgoingMail($"Steve Stout <{TodayMail.To.ToUpperInvariant()}>", "NOT WORKING: x", "t", "h"), CancellationToken.None);

        Assert.Equal("no_email_day", held.Reason);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task On_an_open_day_and_with_no_file_every_mail_goes()
    {
        var inner = new CampaignSendTests.CountingSender();
        var open = new NoEmailDayGate(inner, NoEmailDays.Parse(File), new At(Thanksgiving.AddDays(-1)), TodayMail.To);
        var missing = new NoEmailDayGate(inner, await NoEmailDayGate.ReadAsync(new Share(null), CancellationToken.None), new At(Thanksgiving), TodayMail.To);

        await open.SendAsync(new OutgoingMail("hiring@acme.com", "Hello", "t", "h"), CancellationToken.None);
        await missing.SendAsync(new OutgoingMail("hiring@acme.com", "Hello", "t", "h"), CancellationToken.None);

        Assert.Equal(2, inner.Calls);
        Assert.False((await NoEmailDayGate.ReadAsync(new Share(null), CancellationToken.None)).Read);
        Assert.False((await NoEmailDayGate.ReadAsync(new Share(null, refuse: true), CancellationToken.None)).Read);
        Assert.True((await NoEmailDayGate.ReadAsync(new Share(File), CancellationToken.None)).Listed(new DateOnly(2026, 11, 26)));
    }

    [Fact]
    public async Task The_campaign_on_a_listed_day_sends_nothing_writes_nothing_and_counts_nothing_against_its_cap()
    {
        string root = Path.Combine(Path.GetTempPath(), "noemail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        System.IO.File.WriteAllText(Path.Combine(root, MergePaths.Master), "\"Count\",Person,Company,Communication,Date,Phone/Email,Notes\r\n");
        try
        {
            var db = new CampaignSendTests.MemoryCampaign([.. Enumerable.Range(0, 3).Select(i => CampaignSendTests.Lead(company: $"Shop {i}", key: $"shop{i}.com"))]);
            var inner = new CampaignSendTests.CountingSender();
            var gate = new NoEmailDayGate(inner, NoEmailDays.Parse(File), new At(Thanksgiving), TodayMail.To);

            var result = await new CampaignSend(db, db, gate, new FolderMergeStore(root), new At(Thanksgiving)) { Spacing = TimeSpan.Zero }.RunAsync(CampaignMode.On, CancellationToken.None);

            Assert.Equal(0, inner.Calls);
            Assert.Equal(0, result.Written);
            Assert.Empty(db.Emails);
            Assert.Equal(1, result.Held["no_email_day"]);
            Assert.Contains("held: 1 no_email_day", result.Line);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Share(string? text, bool refuse = false) : IShareReader
    {
        public Task<bool> ExistsAsync(string path, CancellationToken ct) => refuse ? throw new IOException("the share refused") : Task.FromResult(text is not null);

        public Task<byte[]> ReadAsync(string path, CancellationToken ct) => Task.FromResult(Encoding.UTF8.GetBytes(text ?? ""));

        public Task<MergeEntry?> StatAsync(string path, CancellationToken ct) => Task.FromResult<MergeEntry?>(null);

        public Task<IReadOnlyList<MergeEntry>> ListFilesAsync(string folder, CancellationToken ct) => Task.FromResult<IReadOnlyList<MergeEntry>>([]);

        public Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
