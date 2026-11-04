using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// When a follow-up is owed and when it never is: the due rule's 21 and 45 days and its Monday cap, silence after a
/// sent follow-up (Quiet), a request to stop, a bounce, and FOLLOWUP-v1 with every check its render must pass.
/// </summary>
public sealed class NetworkLogFollowUpTests
{
    private static readonly DateTimeOffset Last = new(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);

    private static Conversation Row(ConversationState state = ConversationState.Open, string? topic = "the platform consolidation on .NET 10", int followUps = 0, DateTimeOffset? drafted = null, DateTimeOffset? last = null) => new()
    {
        PersonKey = "pat@acme.com",
        FirstName = "Pat",
        FullName = "Pat Lee",
        Source = "gmail",
        FirstTwoWayAt = Last.AddDays(-10),
        LastInboundAt = (last ?? Last).AddDays(-1),
        LastOutboundAt = last ?? Last,
        State = state,
        TopicLine = topic,
        FollowUpCount = followUps,
        DraftedAt = drafted,
    };

    [Fact]
    public void Due_at_21_days_and_not_at_20()
    {
        Assert.True(DueRule.IsDue(Row(), Last.AddDays(21)));
        Assert.False(DueRule.IsDue(Row(), Last.AddDays(20)));
    }

    [Fact]
    public void No_TopicLine_waits_and_a_newer_inbound_resets_the_clock()
    {
        Assert.False(DueRule.IsDue(Row(topic: null), Last.AddDays(25)));
        Assert.False(DueRule.IsDue(Row(topic: " "), Last.AddDays(25)));
        var answered = Row() with { LastInboundAt = Last.AddDays(10) };
        Assert.False(DueRule.IsDue(answered, Last.AddDays(25)));
        Assert.True(DueRule.IsDue(answered, Last.AddDays(31)));
    }

    [Fact]
    public void A_second_follow_up_only_after_a_new_exchange()
    {
        var drafted = Last.AddDays(21);
        Assert.False(DueRule.IsDue(Row(followUps: 1, drafted: drafted, last: Last), Last.AddDays(43)));
        Assert.True(DueRule.IsDue(Row(followUps: 1, drafted: drafted, last: drafted.AddDays(2)), drafted.AddDays(24)));
        Assert.False(DueRule.IsDue(Row(state: ConversationState.Drafted), Last.AddDays(25)));
    }

    [Fact]
    public void Past_45_days_is_stale_and_never_drafted()
    {
        Assert.True(DueRule.IsDue(Row(), Last.AddDays(45)));
        Assert.False(DueRule.IsDue(Row(), Last.AddDays(46)));
        Assert.True(DueRule.IsStale(Row(), Last.AddDays(46)));
        Assert.False(DueRule.IsStale(Row(), Last.AddDays(45)));
        Assert.False(DueRule.IsStale(Row(state: ConversationState.Quiet), Last.AddDays(100)));
    }

    [Fact]
    public void A_Monday_builds_at_most_fifteen_newest_exchange_first()
    {
        var now = Last.AddDays(40);
        var rows = Enumerable.Range(0, 20).Select(i => Row(last: Last.AddDays(i)) with { PersonKey = $"p{i:00}@acme.com" }).ToList();
        var batch = DueRule.Batch(rows, now);
        Assert.Equal(15, batch.Count);
        Assert.Equal("p19@acme.com", batch[0].PersonKey);
        Assert.Equal("p05@acme.com", batch[^1].PersonKey);
    }

    private static ThreadMessage Steve(DateTimeOffset at) => new("Steven Stout <stevenstout11@gmail.com>", at, true, false);

    private static ThreadMessage Pat(DateTimeOffset at, string snippet = "") => new("Pat Lee <pat@acme.com>", at, false, false, snippet);

    [Fact]
    public void A_draft_still_sitting_stays_Drafted()
    {
        var row = Row(state: ConversationState.Drafted, drafted: Last.AddDays(21));
        Assert.Equal(DraftedOutcome.StaysDrafted, QuietRule.Next(row, true, new ThreadSummary("t", "s", [Pat(Last)]), Last.AddDays(60)));
    }

    [Fact]
    public void Sent_and_answered_opens_again_sent_and_silent_21_days_is_Quiet_and_20_is_not()
    {
        var drafted = Last.AddDays(21);
        var row = Row(state: ConversationState.Drafted, drafted: drafted);
        var sent = drafted.AddDays(1);
        Assert.Equal(DraftedOutcome.Answered, QuietRule.Next(row, false, new ThreadSummary("t", "s", [Steve(sent), Pat(sent.AddDays(2))]), sent.AddDays(3)));
        Assert.Equal(DraftedOutcome.Quiet, QuietRule.Next(row, false, new ThreadSummary("t", "s", [Steve(sent)]), sent.AddDays(21)));
        Assert.Equal(DraftedOutcome.StaysDrafted, QuietRule.Next(row, false, new ThreadSummary("t", "s", [Steve(sent)]), sent.AddDays(20)));
    }

    [Fact]
    public void A_draft_deleted_unsent_is_declined()
    {
        var row = Row(state: ConversationState.Drafted, drafted: Last.AddDays(21));
        Assert.Equal(DraftedOutcome.Declined, QuietRule.Next(row, false, new ThreadSummary("t", "s", [Pat(Last)]), Last.AddDays(30)));
    }

    [Theory]
    [InlineData("Thanks, but I'm not interested right now.", "not interested")]
    [InlineData("Please remove me from your list", "remove me")]
    [InlineData("No thanks!", "no thanks")]
    [InlineData("Don\u2019t contact me again", "don't contact")]
    [InlineData("Sounds good. On Tue, Steve wrote: > not interested in the old stack?", "not interested")]
    public void A_stop_phrase_in_the_person_s_words_stops_the_row(string snippet, string phrase)
    {
        var verdict = StopRule.Read(new ThreadSummary("t", "s", [Steve(Last), Pat(Last.AddDays(1), snippet)]), Last);
        Assert.Equal(new StopVerdict(ConversationState.Stopped, phrase), verdict);
    }

    [Fact]
    public void A_bounce_closes_and_an_ordinary_reply_does_nothing()
    {
        var bounce = new ThreadMessage("Mail Delivery Subsystem <mailer-daemon@googlemail.com>", Last.AddDays(1), false, false, "Address not found");
        Assert.Equal(new StopVerdict(ConversationState.Closed, "bounce"), StopRule.Read(new ThreadSummary("t", "s", [Steve(Last), bounce]), Last));
        Assert.Null(StopRule.Read(new ThreadSummary("t", "s", [Steve(Last), Pat(Last.AddDays(1), "Happy to talk next week")]), Last));
        Assert.Null(StopRule.Read(new ThreadSummary("t", "s", [Pat(Last.AddDays(-5), "not interested"), Steve(Last)]), Last));
    }

    [Fact]
    public void FOLLOWUP_v1_renders_the_brief_s_text_into_the_thread_s_subject()
    {
        var draft = DraftTemplate.Render("Pat", "the platform consolidation on .NET 10.", "RE: Platform role");
        Assert.Equal("Re: Platform role", draft.Subject);
        Assert.Equal("Hi Pat,\n\nIt's been a few weeks since we talked about the platform consolidation on .NET 10.\n\nWould it be a bad idea to catch up for 15 minutes in the next couple of weeks?\n\nSteve", draft.Text);
        Assert.Equal("<div>Hi Pat,</div><div><br></div><div>It&#39;s been a few weeks since we talked about the platform consolidation on .NET 10.</div><div><br></div><div>Would it be a bad idea to catch up for 15 minutes in the next couple of weeks?</div><div><br></div><div>Steve</div>", draft.Html);
        Assert.Empty(DraftTemplate.Check(draft));
        Assert.DoesNotContain("<p>", draft.Html, StringComparison.Ordinal);
        Assert.Equal("FOLLOWUP-v1", DraftTemplate.Version);
    }

    [Theory]
    [InlineData("Pat", "the move \u2014 and the team", "Platform role", "dash")]
    [InlineData("Pat", "how we can leverage the platform", "Platform role", "tell")]
    [InlineData("", "the platform", "Platform role", "blank")]
    [InlineData("Pat", "", "Platform role", "blank")]
    [InlineData("Pat", "whether it ships? and when", "Platform role", "question")]
    [InlineData("Pat", "the platform", "Platform\nrole", "subject")]
    [InlineData("Pat", "the platform", "", "subject")]
    public void A_render_that_fails_a_check_names_it(string first, string topic, string subject, string failed)
    {
        var draft = DraftTemplate.Render(first, topic, subject);
        Assert.Contains(failed, DraftTemplate.Check(draft));
    }

    [Fact]
    public void Re_is_never_doubled()
    {
        Assert.Equal("Re: Coffee", DraftTemplate.Subject("Re: Re: Coffee"));
        Assert.Equal("Re: Coffee", DraftTemplate.Subject("Coffee"));
    }
}
