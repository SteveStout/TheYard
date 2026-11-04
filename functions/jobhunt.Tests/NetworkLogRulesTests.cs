using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The network log's rules, each with its named cases: who is a person, what makes a thread two-way, the keys a
/// row is stored and joined under, who counts on a personal mailbox, and the week's scoreboard. Every name and
/// address is made up; the shapes are the ones Gmail and the job hunt's mail really carry.
/// </summary>
public sealed class NetworkLogRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Pat Lee <pat.lee@acme.com>", true)]
    [InlineData("no-reply@us.greenhouse-mail.io", false)]
    [InlineData("Acme via Greenhouse <pat@us.greenhouse-mail.io>", false)]
    [InlineData("noreply@acme.com", false)]
    [InlineData("noreply-calendar@acme.com", false)]
    [InlineData("Google Calendar <calendar-notification@google.com>", false)]
    [InlineData("invitations@linkedin.com", false)]
    [InlineData("LinkedIn <messages-noreply@linkedin.com>", false)]
    [InlineData("Mail Delivery Subsystem <mailer-daemon@googlemail.com>", false)]
    [InlineData("postmaster@acme.com", false)]
    [InlineData("pat@bounce.acme.com", false)]
    [InlineData("Steven Stout <stevenstout11@gmail.com>", false)]
    [InlineData("pat@storee.com", false)]
    [InlineData("Pat Lee <pat.lee.home@gmail.com>", true)]
    [InlineData("", false)]
    public void A_person_is_not_a_machine_not_Steve_and_not_a_former_employer_s_work_address(string from, bool human) =>
        Assert.Equal(human, HumanSenderRule.IsHuman(from));

    [Fact]
    public void A_bounce_is_the_mail_system_answering()
    {
        Assert.True(HumanSenderRule.IsBounce("Mail Delivery Subsystem <mailer-daemon@googlemail.com>"));
        Assert.True(HumanSenderRule.IsBounce("postmaster@acme.com"));
        Assert.False(HumanSenderRule.IsBounce("pat@acme.com"));
    }

    private static ThreadMessage In(string from, int day, bool calendar = false, string snippet = "") => new(from, T0.AddDays(day), false, calendar, snippet, "stevenstout11@gmail.com", $"<m{day}@acme.com>");

    private static ThreadMessage Out(int day) => new("Steven Stout <stevenstout11@gmail.com>", T0.AddDays(day), true, false, "", "pat@acme.com", $"<s{day}@gmail.com>");

    private static ThreadSummary Thread(params ThreadMessage[] messages) => new("t1", "Platform role", messages);

    [Fact]
    public void Human_then_Steve_is_two_way_from_Steve_s_reply()
    {
        var read = TwoWayRule.Read(Thread(In("Pat Lee <pat@acme.com>", 0), Out(2), In("Pat Lee <pat@acme.com>", 3)));
        Assert.NotNull(read);
        Assert.Equal("pat@acme.com", read.Address);
        Assert.Equal(T0.AddDays(2), read.FirstTwoWayAt);
        Assert.Equal(T0.AddDays(3), read.LastInboundAt);
        Assert.Equal(T0.AddDays(2), read.LastOutboundAt);
        Assert.Equal("<m3@acme.com>", read.NewestMessageId);
    }

    [Fact]
    public void Human_only_Steve_only_and_machines_only_are_not_two_way()
    {
        Assert.Null(TwoWayRule.Read(Thread(In("Pat Lee <pat@acme.com>", 0))));
        Assert.Null(TwoWayRule.Read(Thread(Out(0), Out(5))));
        Assert.Null(TwoWayRule.Read(Thread(In("noreply@acme.com", 0), Out(1))));
    }

    [Fact]
    public void An_invite_from_a_person_is_two_way_without_a_reply()
    {
        var read = TwoWayRule.Read(Thread(In("Pat Lee <pat@acme.com>", 4, calendar: true)));
        Assert.NotNull(read);
        Assert.Equal(T0.AddDays(4), read.FirstTwoWayAt);
        Assert.Null(read.LastOutboundAt);
        Assert.Null(TwoWayRule.Read(Thread(In("Google Calendar <calendar-notification@google.com>", 4, calendar: true))));
    }

    [Theory]
    [InlineData("Pat.Lee@ACME.com", "pat.lee@acme.com")]
    [InlineData("Pat Lee <Pat.Lee@Acme.com>", "pat.lee@acme.com")]
    public void An_email_row_is_keyed_by_its_lower_case_address(string from, string key) => Assert.Equal(key, PersonKey.ForEmail(from));

    [Theory]
    [InlineData("https://www.linkedin.com/in/pat-lee-123/", "linkedin:pat-lee-123")]
    [InlineData("https://www.linkedin.com/in/Pat-Lee-123?trk=x", "linkedin:pat-lee-123")]
    [InlineData("pat-lee-123/", "linkedin:pat-lee-123")]
    [InlineData("", "")]
    public void A_linkedin_row_is_keyed_by_its_profile_slug(string profile, string key) => Assert.Equal(key, PersonKey.ForLinkedIn(profile));

    [Theory]
    [InlineData("Pat Lee", "acme", "pat lee|acme")]
    [InlineData("Pat Lee Jr.", "acme", "pat lee|acme")]
    [InlineData("Lee, Pat", "acme", "pat lee|acme")]
    [InlineData("José Núñez", "Acme, Inc.", "jose nunez|acme")]
    [InlineData("Pat Lee, PhD", "Acme Corp", "pat lee|acme")]
    [InlineData("Pat", "acme", null)]
    [InlineData("Pat Lee", "", null)]
    public void The_match_key_joins_one_person_across_channels(string name, string company, string? key) => Assert.Equal(key, MatchKey.Of(name, company));

    [Fact]
    public void Two_hosts_of_one_company_give_one_token_and_a_personal_domain_gives_none()
    {
        Assert.Equal("acme", MatchKey.TokenOfAddress("pat@mail.acme.com"));
        Assert.Equal("acme", MatchKey.TokenOfAddress("lee@acme.com"));
        Assert.Equal(MatchKey.Of("Pat Lee", MatchKey.TokenOfAddress("pat@mail.acme.com")), MatchKey.Of("Pat Lee", "Acme Inc"));
        Assert.Equal("", MatchKey.TokenOfAddress("pat@gmail.com"));
    }

    [Theory]
    [InlineData("Pat Lee", "Pat")]
    [InlineData("Lee, Pat", "Pat")]
    [InlineData("\"Pat Lee\"", "Pat")]
    [InlineData("pat@acme.com", "")]
    [InlineData("", "")]
    public void The_greeting_is_the_first_name_as_written(string name, string first) => Assert.Equal(first, MatchKey.FirstName(name));

    [Fact]
    public void A_company_domain_counts_and_a_personal_one_only_when_the_job_hunt_knows_the_person()
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { "lee.family@gmail.com", "sam ortiz" };
        Assert.True(WarmContactRule.Counts("pat@acme.com", "Pat Lee", known));
        Assert.True(WarmContactRule.Counts("lee.family@gmail.com", "Lee Family", known));
        Assert.True(WarmContactRule.Counts("sam.o@yahoo.com", "Sam Ortiz", known));
        Assert.False(WarmContactRule.Counts("neighbour@gmail.com", "Chris Neighbour", known));
        Assert.False(WarmContactRule.Counts("x@hotmail.com", "", known));
    }

    [Fact]
    public void The_scoreboard_counts_rows_whose_first_exchange_fell_in_the_Central_ISO_week()
    {
        static Conversation Row(DateTimeOffset at) => new() { PersonKey = at.ToString("O"), FirstName = "P", FullName = "P L", Source = "gmail", FirstTwoWayAt = at };
        // Monday 11/2 00:30 Central is 06:30 UTC; Sunday 11/1 23:30 Central is 05:30 UTC on Monday.
        var monday = new DateTimeOffset(2026, 11, 2, 6, 30, 0, TimeSpan.Zero);
        var sundayNight = new DateTimeOffset(2026, 11, 2, 5, 30, 0, TimeSpan.Zero);
        var sunday = new DateTimeOffset(2026, 11, 8, 20, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, IsoWeek.StartedThisWeek([Row(monday), Row(sundayNight), Row(sunday)], new DateOnly(2026, 11, 4)));
        Assert.Equal((2026, 45), IsoWeek.Of(new DateOnly(2026, 11, 2)));
        Assert.Equal((2026, 45), IsoWeek.Of(new DateOnly(2026, 11, 8)));
        Assert.Equal(new DateOnly(2026, 11, 2), IsoWeek.Monday(new DateOnly(2026, 11, 8)));
        Assert.Equal(new DateOnly(2026, 11, 9), IsoWeek.Monday(new DateOnly(2026, 11, 9)));
    }

    [Fact]
    public void The_ISO_week_crosses_the_year_boundary()
    {
        Assert.Equal((2026, 53), IsoWeek.Of(new DateOnly(2027, 1, 1)));
        Assert.Equal((2027, 1), IsoWeek.Of(new DateOnly(2027, 1, 4)));
        Assert.Equal((2026, 1), IsoWeek.Of(new DateOnly(2025, 12, 29)));
    }
}
