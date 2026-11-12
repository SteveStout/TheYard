using JobHunt.Server.Infrastructure.Shared.Site;

namespace JobHunt.Server.Tests;

/// <summary>
/// invites-joblink1112: Steve's 11/12 08:08 rule, "always if possible include the job link". The queued notes are the
/// 06:00 invites lane's own texts of 11/12 (nightly/2026-11-12/0600-invites-sonnet-5-5/texts); Charley Frazier's and
/// Jeffrey Sweeney's notes as chat1112 sent them at 296 and 261 on LinkedIn's counter come out of the builder exactly.
/// </summary>
public sealed class JobLinkNoteTests
{
    private const string Charley = "Charley,\n\nYour team puts LLM agents into construction workflows; construction tech is my background too. TheYard, my platform, is live on Azure, .NET 10 and React.\n\ntheyard.stevenstout.biz/?r=gupojr\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve\n";

    private const string Jeffrey = "Jeffrey,\n\nPricing engines like Smart CPQ have to keep shipping without going down. I shipped 200+ zero-downtime releases as the sole engineer on a live financial platform.\n\ntheyard.stevenstout.biz/?r=lh0pov\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve\n";

    private const string Jason = "Jason,\n\nWe share Paul Sears and Kevin McGraw as mutual connections. I shipped 200+ zero-downtime releases as the sole engineer on a live financial platform.\n\ntheyard.stevenstout.biz/?r=p6ffha\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve\n";

    private const string Emily = "Emily,\n\nKeeping insurance complexity out of therapists' way is the kind of platform work I love. I shipped 200+ zero-downtime releases as the sole engineer on a live financial platform.\n\ntheyard.stevenstout.biz/?r=x73in0\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve\n";

    [Fact]
    public void Charley_s_note_is_the_one_sent_at_296()
    {
        string? note = JobLinkNote.Build(Charley, "Technical Lead Manager", "jobs.ashbyhq.com/Homebound/fb4c15fb-11d3-4ab0-9497-720d08051c3f");

        Assert.Equal(
            "Charley,\n\nYour team puts LLM agents into construction workflows; construction tech is my background too. I applied for your Technical Lead Manager role:\n\n"
            + "jobs.ashbyhq.com/Homebound/fb4c15fb-11d3-4ab0-9497-720d08051c3f\ntheyard.stevenstout.biz/?r=gupojr\n\nIs it a bad idea to connect?\n\nThanks,\nSteve\n", note);
        Assert.Equal(296, JobLinkNote.Expected(note!));
    }

    [Fact]
    public void Jeffrey_s_note_drops_the_proof_keeps_thanks_and_is_261()
    {
        string? note = JobLinkNote.Build(Jeffrey, "Senior Technical Architect", "job-boards.greenhouse.io/conga/jobs/5285407007");

        Assert.Equal(261, JobLinkNote.Expected(note!));
        Assert.DoesNotContain("200+", note, StringComparison.Ordinal);
        Assert.EndsWith("Thanks,\nSteve\n", note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Jason, "Senior Software Engineer - Lead Platform", "jobs.ashbyhq.com/a-place-for-mom/ee4c0ce4-ceb6-4d38-a1d5-0dda8a737d2a")]
    public void Every_built_note_is_under_300_and_passes_the_outreach_checks(string queued, string role, string url)
    {
        string? note = JobLinkNote.Build(queued, role, url);

        Assert.NotNull(note);
        Assert.InRange(JobLinkNote.Expected(note), 1, 299);
        Assert.Contains($"I applied for your {role} role:\n\n{url}\n", note, StringComparison.Ordinal);
        Assert.Empty(TextCheck.Problems(note, invite: true));
    }

    // Emily Old's note went out at 270 with a hook chat1112 shortened by hand; the program never rewrites a hook, and
    // her queued hook with the long role title does not fit even without the proof and "Thanks,", so it keeps today's note.
    [Fact]
    public void A_note_that_cannot_fit_keeps_today_s_text()
    {
        Assert.Null(JobLinkNote.Build(Emily, "Senior Engineering Manager (Patient New Verticals)", "jobs.ashbyhq.com/headway/9ef73ae3-ff90-425b-96ef-0f1cb0bfc0c2"));
        Assert.Null(JobLinkNote.Build(Jeffrey, new string('R', 200), "job-boards.greenhouse.io/conga/jobs/5285407007"));
    }

    [Theory]
    [InlineData("Evidence: confirmation page job-boards.greenhouse.io/conga/jobs/5285407007/confirmation, title", "job-boards.greenhouse.io/conga/jobs/5285407007")]
    [InlineData("banner at jobs.ashbyhq.com/headway/9ef73ae3-ff90-425b-96ef-0f1cb0bfc0c2/application. Resume", "jobs.ashbyhq.com/headway/9ef73ae3-ff90-425b-96ef-0f1cb0bfc0c2")]
    [InlineData("at https://jobs.lever.co/northwind/1f0e2d3c-aaaa-bbbb-cccc-1234567890ab/apply today", "jobs.lever.co/northwind/1f0e2d3c-aaaa-bbbb-cccc-1234567890ab")]
    public void The_posting_link_is_read_bare_from_the_application_row(string notes, string url) => Assert.Equal(url, JobLinkNote.PostingUrl(notes));

    [Fact]
    public void A_second_link_beside_the_posting_still_fails_the_check()
    {
        string note = JobLinkNote.Build(Jeffrey, "Senior Technical Architect", "job-boards.greenhouse.io/conga/jobs/5285407007")!.Replace("Thanks,", "See example.com. Thanks,", StringComparison.Ordinal);

        Assert.Contains(TextCheck.Problems(note, invite: true), problem => problem.Contains("other links", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_board_s_answer_gives_the_posting_s_title()
    {
        Assert.Equal("Senior Technical Architect", BoardPostings.Title("job-boards.greenhouse.io", "5285407007", """{"id":5285407007,"title":"Senior Technical Architect"}"""));
        Assert.Equal("Technical Lead Manager", BoardPostings.Title("jobs.ashbyhq.com", "fb4c15fb", """{"jobs":[{"id":"aa","title":"Other"},{"id":"fb4c15fb","title":"Technical Lead Manager"}]}"""));
        Assert.Null(BoardPostings.Title("jobs.ashbyhq.com", "gone", """{"jobs":[{"id":"aa","title":"Other"}]}"""));
        Assert.Equal("Principal Engineer", BoardPostings.Title("jobs.lever.co", "x", """{"id":"x","text":"Principal Engineer"}"""));
    }

    [Fact]
    public async Task The_worklist_writes_the_link_when_the_board_lists_the_role_and_today_s_note_when_it_does_not()
    {
        var morning = new Morning(_ => new ClaudeRun(0, false, ResultLine.Empty));
        morning.Share.Files["linkedin-proof/QUEUE-hotmgr-2026-11-03.md"] =
            "### 1. Kim Hot, Delta (VP). Code [code].\n\n```\nKim,\n\nYour team ships payments on .NET. I moved four apps onto one .NET platform on Azure.\n\ntheyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve\n```\n";
        morning.Share.Files["inbox/tracker-inbox/20261102-120000Z-apply-delta.csv"] =
            ",Delta,Application,11/1/2026,,\"APPLICATION SUBMITTED on Greenhouse: Staff Engineer. Evidence: job-boards.greenhouse.io/delta/jobs/123/confirmation\"\n";
        DayReads reads = await new DayReader(morning.Share).ReadAsync(DateOnly.FromDateTime(Morning.At.UtcDateTime), default);

        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);
        Worklist live = await new JobLinks(new Postings("Staff Engineer"), new FakeProse()).ApplyAsync(reads, builder.InvitesAsync(reads, 10, dry: true, default), default);
        Worklist gone = await new JobLinks(new Postings(null), new FakeProse()).ApplyAsync(reads, builder.InvitesAsync(reads, 10, dry: true, default), default);

        WorkRow row = Assert.Single(live.Rows, r => r.Person == "Kim Hot");
        Assert.Contains("I applied for your Staff Engineer role:\n\njob-boards.greenhouse.io/delta/jobs/123\n", live.Texts[row.Row], StringComparison.Ordinal);
        Assert.Equal(JobLinkNote.Expected(live.Texts[row.Row]), row.Expected);
        WorkRow plain = Assert.Single(gone.Rows, r => r.Person == "Kim Hot");
        Assert.DoesNotContain("applied", gone.Texts[plain.Row], StringComparison.Ordinal);
        Assert.Equal(JobLinkNote.Expected(gone.Texts[plain.Row]), plain.Expected);
    }

    private sealed class Postings(string? title) : IPostingCheck
    {
        public Task<string?> LiveTitleAsync(string url, CancellationToken ct) => Task.FromResult(title);
    }
}
