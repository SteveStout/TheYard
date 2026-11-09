using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// Azure SQL's live source (ADR: Live dials where the source allows, the addendum on Azure SQL's line): DTU is the
/// greatest of the three shares whichever leads, the newest interval is one sample and a read that finds nothing newer
/// adds none, an absent view adds none, and a site with no relational store says so instead of reading.
/// </summary>
public sealed class SqlLiveSourceTests
{
    private static readonly DateTime Nine = new(2026, 11, 7, 15, 0, 0, DateTimeKind.Utc);

    private static ResourceStatRow Row(DateTime at, double cpu, double io, double log) => new(at, cpu, io, log, 30, 4);

    [Theory]
    [InlineData(40, 10, 5, 40)]
    [InlineData(10, 55, 5, 55)]
    [InlineData(10, 5, 72.5, 72.5)]
    public void Dtu_is_the_greatest_of_the_three_shares_whichever_leads(double cpu, double io, double log, double dtu) =>
        Assert.Equal(dtu, SqlLiveSource.Dtu(Row(Nine, cpu, io, log)));

    [Fact]
    public async Task The_newest_interval_is_one_sample_and_a_read_with_nothing_newer_adds_none()
    {
        var rows = new List<ResourceStatRow> { Row(Nine.AddSeconds(-15), 20, 2, 1), Row(Nine, 31.5, 4, 2) };
        int reads = 0;
        var source = new SqlLiveSource(_ => { reads++; return Task.FromResult(new StoreLoad(true, null, rows)); }, () => true);

        var first = await source.SampleAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        var again = await source.SampleAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        rows.Add(Row(Nine.AddSeconds(15), 8, 3, 1));
        var next = await source.SampleAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(("sqldb", 15), (source.Key, source.GrainSeconds));
        Assert.Null(source.Absent);
        Assert.Equal(new DateTimeOffset(Nine), first!.At);
        Assert.Equal(31.5, first.Values["dtu"]);
        Assert.Equal(4, first.Values["data_io"]);
        Assert.Null(again);
        Assert.Equal(8, next!.Values["dtu"]);
        Assert.Equal(3, reads);
        Assert.Equal(40, SqlLiveSource.Rows);
    }

    [Fact]
    public async Task An_absent_view_adds_nothing_and_a_site_with_no_relational_store_says_so()
    {
        var absent = new SqlLiveSource(_ => Task.FromResult(StoreLoad.Absent("the view exists on Azure SQL Database only")), () => true);
        var empty = new SqlLiveSource(_ => Task.FromResult(new StoreLoad(true, null, [])), () => true);
        var none = new SqlLiveSource(_ => throw new InvalidOperationException("never read"), () => false);

        Assert.Null(await absent.SampleAsync(DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Null(await empty.SampleAsync(DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal("this site keeps no relational store", none.Absent);
    }
}
