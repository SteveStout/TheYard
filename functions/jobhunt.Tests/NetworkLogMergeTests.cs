using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// How a newer read moves a stored conversation, how the LinkedIn spool is read and refused, and the pass line's
/// shape: the parts of the daily pass that are rules rather than reads.
/// </summary>
public sealed class NetworkLogMergeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);

    private static Conversation Stored(ConversationState state = ConversationState.Open, string? reason = null) => new()
    {
        Id = 7,
        PersonKey = "pat@acme.com",
        MatchKey = "pat lee|acme",
        FirstName = "Pat",
        FullName = "Pat Lee",
        Email = "pat@acme.com",
        Source = "gmail",
        ThreadIds = ["t1"],
        FirstTwoWayAt = T0,
        LastInboundAt = T0.AddDays(5),
        LastOutboundAt = T0.AddDays(6),
        LastChannel = Channel.Email,
        LastSubject = "Platform role",
        State = state,
        CloseReason = reason,
    };

    private static Conversation Read(DateTimeOffset? inbound, DateTimeOffset? outbound, string thread = "t2") => Stored() with
    {
        Id = 0,
        ThreadIds = [thread],
        FirstTwoWayAt = T0.AddDays(1),
        LastInboundAt = inbound,
        LastOutboundAt = outbound,
        LastSubject = "Coffee",
        LastChannel = Channel.LinkedIn,
    };

    [Fact]
    public void An_older_read_moves_nothing_back()
    {
        var merged = ConversationMerge.Merge(Stored(), Read(T0.AddDays(2), T0.AddDays(3)));
        Assert.Equal(T0, merged.FirstTwoWayAt);
        Assert.Equal(T0.AddDays(5), merged.LastInboundAt);
        Assert.Equal(T0.AddDays(6), merged.LastOutboundAt);
        Assert.Equal("Platform role", merged.LastSubject);
        Assert.Equal(Channel.Email, merged.LastChannel);
        Assert.Equal(["t2", "t1"], merged.ThreadIds);
        Assert.Equal(7, merged.Id);
    }

    [Fact]
    public void A_newer_read_moves_the_exchange_the_subject_and_the_channel()
    {
        var merged = ConversationMerge.Merge(Stored(), Read(T0.AddDays(9), null));
        Assert.Equal(T0.AddDays(9), merged.LastInboundAt);
        Assert.Equal(T0.AddDays(6), merged.LastOutboundAt);
        Assert.Equal("Coffee", merged.LastSubject);
        Assert.Equal(Channel.LinkedIn, merged.LastChannel);
    }

    [Theory]
    [InlineData(ConversationState.Quiet, null, ConversationState.Open)]
    [InlineData(ConversationState.Closed, "stale", ConversationState.Open)]
    [InlineData(ConversationState.Closed, "declined", ConversationState.Open)]
    [InlineData(ConversationState.Closed, "bounce", ConversationState.Closed)]
    [InlineData(ConversationState.Stopped, "not interested", ConversationState.Stopped)]
    [InlineData(ConversationState.Drafted, null, ConversationState.Drafted)]
    public void The_person_writing_again_opens_a_quiet_stale_or_declined_row_and_nothing_else(ConversationState state, string? reason, ConversationState expected)
    {
        var merged = ConversationMerge.Merge(Stored(state, reason), Read(T0.AddDays(30), null));
        Assert.Equal(expected, merged.State);
        Assert.Equal(expected == ConversationState.Open && state != ConversationState.Open ? null : reason, merged.CloseReason);
    }

    [Fact]
    public void Steve_writing_again_opens_nothing()
    {
        var merged = ConversationMerge.Merge(Stored(ConversationState.Quiet), Read(null, T0.AddDays(30)));
        Assert.Equal(ConversationState.Quiet, merged.State);
    }

    [Fact]
    public void A_stop_never_overwrites_a_stop_or_a_bounce()
    {
        var stop = new StopVerdict(ConversationState.Stopped, "remove me");
        Assert.Equal("remove me", ConversationMerge.Stop(Stored(), stop).CloseReason);
        Assert.Equal("not interested", ConversationMerge.Stop(Stored(ConversationState.Stopped, "not interested"), stop).CloseReason);
        Assert.Equal("bounce", ConversationMerge.Stop(Stored(ConversationState.Closed, "bounce"), stop).CloseReason);
    }

    [Fact]
    public void A_new_email_row_from_a_personal_address_has_no_company_and_no_match_key()
    {
        var thread = new ThreadSummary("t5", "Hello", []);
        var read = new TwoWayThread("Pat Lee <pat.lee@gmail.com>", "pat.lee@gmail.com", T0, T0.AddDays(1), T0, null);
        var row = ConversationMerge.NewEmail(read, thread);
        Assert.Null(row.Company);
        Assert.Null(row.CompanyKey);
        Assert.Null(row.MatchKey);
        Assert.Equal("pat.lee@gmail.com", row.Email);
        var work = ConversationMerge.NewEmail(read with { From = "Pat Lee <pat@mail.acme.com>", Address = "pat@mail.acme.com" }, thread);
        Assert.Equal("acme.com", work.CompanyKey);
        Assert.Equal("pat lee|acme", work.MatchKey);
    }

    [Fact]
    public void The_LinkedIn_spool_refuses_a_wrong_header_and_a_row_missing_its_times()
    {
        Assert.Single(LinkedInSpool.Parse("Name\tCompany\n", "f.tsv").Refused);
        var batch = LinkedInSpool.Parse(LinkedInSpool.Header + "\nPat Lee\tAcme\thttps://www.linkedin.com/in/pat/\tli-1\t\t\t2026-10-01T00:00:00Z\nSam Ortiz\t\tsam-ortiz\tli-2\t2026-10-03T10:00:00-05:00\t\t2026-10-02T00:00:00Z\n", "f.tsv");
        Assert.Equal(["line 2: needs a person, a profile, a thread id, a last inbound and a first two-way time"], batch.Refused);
        var sam = Assert.Single(batch.Rows);
        Assert.Equal("linkedin:sam-ortiz", sam.PersonKey);
        Assert.Null(sam.Company);
        Assert.Null(sam.MatchKey);
        Assert.Null(sam.LastOutboundAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero), sam.LastInboundAt);
        Assert.Equal("f.tsv", sam.Source);
    }

    [Fact]
    public void The_pass_line_names_every_count_and_ends_with_the_week()
    {
        string line = NetworkFiles.Line(new DateTime(2026, 11, 9, 6, 0, 4), new PassLine("shadow", 40, 3, 1, 0, 2, 5, 4, 12, 6, [("linkedin", 0), ("cut", 9)]));
        Assert.Equal("2026-11-09 06:00:04 mode=shadow threads=40 upserted=3 stopped=1 quiet=0 personal=2 due=5 drafted=4 linkedin=0 cut=9 seconds=12 conversations started this ISO week: 6", line);
        Assert.Equal("networklog/would-drafts/2026-11-09.md", NetworkFiles.WouldDraftsPath(new DateOnly(2026, 11, 9)));
    }
}
