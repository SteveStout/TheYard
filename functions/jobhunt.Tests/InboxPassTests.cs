using System.Reflection;
using JobHunt.Functions.Ats;
using JobHunt.Functions.Leads;
using Microsoft.Azure.Functions.Worker;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The 30-minute inbox pass (Steve's 2026-10-23 alpha goal): the leads harvest on harvests before it archives
/// and leaves anything it cannot read whole, the triage step hands the model only mail no triage run has
/// judged, and the pass runs every half hour. The alerts below are in the shape LinkedIn sends, with made-up jobs.
/// </summary>
public sealed class InboxPassTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 23, 14, 20, 0, TimeSpan.Zero);

    private const string Whole = "Your job alert for lead engineer\nManage your job alerts: https://www.linkedin.com/comm/jobs/alerts?x=1\n\nLead Engineer\nAcme\nUnited States (Remote)\nView job: https://www.linkedin.com/comm/jobs/view/111/?trk=x\n\n---------------------------------------------------------\n\nStaff Engineer\nGlobex\nBerkeley\nView job: https://www.linkedin.com/comm/jobs/view/222/?trk=y\n";

    private const string Broken = "Your job alert for lead engineer\nManage your job alerts: https://www.linkedin.com/comm/jobs/alerts?x=1\n\nLead Engineer\nView job: https://www.linkedin.com/comm/jobs/view/333/?trk=z\n";

    private const string Empty = "Your job alert\n\nThere are no new jobs for this alert.\n";

    [Fact]
    public async Task On_writes_the_leads_then_archives_only_the_alerts_that_read_whole()
    {
        using var scratch = new Scratch();
        scratch.Master("");
        var inbox = new MemoryInbox(
            Alert("a1", "t1", Whole),
            Alert("a2", "t2", Broken),
            Alert("a3", "t3", Empty),
            Alert("a4", "t4", Whole.Replace("111", "444", StringComparison.Ordinal).Replace("222", "555", StringComparison.Ordinal)),
            new InboxMessage("p1", "t4", "Jane Doe <jane.doe@acme.example>", "Re: your alert", Morning, "a person in the alert's thread"));
        string line = await new LeadsHarvest(scratch.Store, inbox, new FixedTime(Morning)).RunAsync(true, CancellationToken.None);
        Assert.Equal(["a1"], inbox.Archived);
        string leads = File.ReadAllText(Path.Combine(scratch.Root, "nightly", "leads-2026-10-23.md"));
        Assert.Contains("## a1 | jobalerts-noreply@linkedin.com", leads, StringComparison.Ordinal);
        Assert.Contains("| Acme | Lead Engineer | United States (Remote) | https://www.linkedin.com/jobs/view/111/ | a1 | pass |", leads, StringComparison.Ordinal);
        Assert.Contains("| Globex | Staff Engineer | Berkeley | https://www.linkedin.com/jobs/view/222/ | a1 | no: not remote: Berkeley |", leads, StringComparison.Ordinal);
        Assert.DoesNotContain("## a2", leads, StringComparison.Ordinal);
        Assert.DoesNotContain("## a4", leads, StringComparison.Ordinal);
        Assert.StartsWith("ON alerts 4,", line, StringComparison.Ordinal);
        Assert.Contains("harvested 1 to nightly/leads-2026-10-23.md, 1 archived and read back, 0 failed, 3 left", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_appends_to_the_day_s_leads_file_and_never_rewrites_it()
    {
        using var scratch = new Scratch();
        scratch.Master("");
        scratch.Write("nightly/leads-2026-10-23.md", "# Leads harvested by the records run\n");
        var inbox = new MemoryInbox(Alert("a1", "t1", Whole));
        await new LeadsHarvest(scratch.Store, inbox, new FixedTime(Morning)).RunAsync(true, CancellationToken.None);
        string leads = File.ReadAllText(Path.Combine(scratch.Root, "nightly", "leads-2026-10-23.md"));
        Assert.StartsWith("# Leads harvested by the records run\n", leads, StringComparison.Ordinal);
        Assert.Contains("the 30-minute inbox pass", leads, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shadow_archives_nothing_and_writes_no_leads_file()
    {
        using var scratch = new Scratch();
        scratch.Master("");
        var inbox = new MemoryInbox(Alert("a1", "t1", Whole));
        string line = await new LeadsHarvest(scratch.Store, inbox, new FixedTime(Morning)).RunAsync(false, CancellationToken.None);
        Assert.Empty(inbox.Archived);
        Assert.False(File.Exists(Path.Combine(scratch.Root, "nightly", "leads-2026-10-23.md")));
        Assert.Equal("SHADOW alerts 1, requisitions 2, pass 1, blocks unread 0", line);
    }

    [Fact]
    public async Task Triage_hands_the_model_only_mail_no_triage_run_has_judged()
    {
        using var scratch = new Scratch();
        scratch.Write(InboxTriage.Seen, "# Gmail id\tdecision\twhy\nm1\tleave\ta person\nm2\tarchive\tan alert\n");
        var inbox = new MemoryInbox(
            new InboxMessage("m1", "t1", "x@y.example", "s", Morning, ""),
            new InboxMessage("m2", "t2", "x@y.example", "s", Morning, ""),
            new InboxMessage("m3", "t3", "x@y.example", "s", Morning, ""));
        Assert.Equal(["m3"], await new InboxTriage(scratch.Store, inbox).UnjudgedAsync(CancellationToken.None));
        Assert.Equal("in:inbox newer_than:2d", inbox.Queries.Single());
    }

    [Fact]
    public async Task Triage_with_no_seen_file_yet_hands_over_everything_in_the_window()
    {
        using var scratch = new Scratch();
        var inbox = new MemoryInbox(new InboxMessage("m1", "t1", "x@y.example", "s", Morning, ""));
        Assert.Equal(["m1"], await new InboxTriage(scratch.Store, inbox).UnjudgedAsync(CancellationToken.None));
    }

    [Fact]
    public void The_triage_run_is_fired_only_when_TRIAGE_is_on()
    {
        Assert.Equal("TRIAGE", Dispatch.Gate(InboxTriage.Event));
        Assert.Equal("BOOKKEEPING_DISPATCH", Dispatch.Gate(CentralClock.Bookkeeping));
        Assert.Equal("NIGHTLY_DISPATCH", Dispatch.Gate(CentralClock.Reply));
    }

    [Fact]
    public void The_pass_runs_every_thirty_minutes()
    {
        var run = typeof(AtsSweepTimer).GetMethod(nameof(AtsSweepTimer.RunAsync))!;
        Assert.Equal("AtsSweep", run.GetCustomAttribute<FunctionAttribute>()!.Name);
        var timer = run.GetParameters()[0].GetCustomAttribute<TimerTriggerAttribute>()!;
        Assert.Equal("0 20,50 * * * *", timer.Schedule);
    }

    [Fact]
    public void No_code_under_leads_or_the_triage_step_can_send_draft_trash_or_delete()
    {
        string root = Root();
        string code = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "functions", "jobhunt", "Leads"), "*.cs").Select(File.ReadAllText))
            + File.ReadAllText(Path.Combine(root, "functions", "jobhunt", "InboxTriage.cs"))
            + File.ReadAllText(Path.Combine(root, "functions", "jobhunt", "AtsSweepTimer.cs"));
        foreach (string forbidden in new[] { "/send", "/drafts", "/trash", "batchDelete", "batchModify", "DeleteAsync", "HttpMethod.Delete", "addLabelIds" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    private static InboxMessage Alert(string id, string thread, string text) =>
        new(id, thread, "LinkedIn Job Alerts <jobalerts-noreply@linkedin.com>", "Lead Engineer at Acme", Morning, text.Replace('\n', ' ')) { Lines = text };

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "infra", "jobhunt.bicep")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("infra/jobhunt.bicep not found above " + AppContext.BaseDirectory);
    }

    /// <summary>An inbox in memory: the searches it was asked, its archives recorded.</summary>
    private sealed class MemoryInbox(params InboxMessage[] messages) : IInbox
    {
        public List<string> Archived { get; } = [];

        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<string>> FindAsync(string query, int max, CancellationToken ct)
        {
            Queries.Add(query);
            var found = query.Contains(LinkedInAlert.Sender, StringComparison.Ordinal)
                ? messages.Where(m => m.Address == LinkedInAlert.Sender)
                : messages;
            return Task.FromResult<IReadOnlyList<string>>([.. found.Select(m => m.Id).Take(max)]);
        }

        public Task<InboxMessage> ReadAsync(string id, CancellationToken ct) => Task.FromResult(messages.Single(m => m.Id == id));

        public Task<IReadOnlyList<string>> ThreadSendersAsync(string threadId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. messages.Where(m => m.ThreadId == threadId).Select(m => m.From)]);

        public Task<bool> ArchiveAsync(string id, CancellationToken ct)
        {
            Archived.Add(id);
            return Task.FromResult(true);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
