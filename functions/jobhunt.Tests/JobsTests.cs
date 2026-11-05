using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The night's picker (PROMPT-JOBHUNT-NIGHTLY-JOBS rung 3): every deterministic gate with a named case that passes and
/// one that fails with its reason, RULE 72's lines read from one constant class, the alert rows read from a real
/// leads file, the candidates file's shape (supply line, header, the passed rows, every failure at its foot), an
/// alert dropped for the posting the scan read tonight, the alert leads alone while the postings tables are not
/// published, the share's CSV when the tracker's SQL copy does not answer, the picker's hour and its statements' columns.
/// </summary>
public sealed partial class JobsTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static Candidate Role(string title = "Principal Software Engineer", string company = "Acme", string location = "Remote, US", string stance = "Remote", int? band = 210_000, string text = "C# and .NET on Azure", int age = 3, string confirmed = "feed") =>
        new("feed:greenhouse", company, title, "https://job-boards.greenhouse.io/acme/jobs/1", "greenhouse", Today.AddDays(-age), location, stance, band, confirmed, text);

    private static string? Fail(Candidate c, IReadOnlyList<string[]>? master = null) => JobsGates.Judge(c, master ?? [], [], Today).Failed;

    [Fact]
    public void A_remote_principal_dotnet_role_with_a_band_over_the_pass_line_passes_every_gate()
    {
        var verdict = JobsGates.Judge(Role(), [], [], Today);
        Assert.Null(verdict.Failed);
        Assert.Equal(("PASS", "remote", "dotnet", 3), (verdict.BandFlag, verdict.Remote, verdict.Stack, verdict.DaysOld));
        Assert.Equal("remote; Principal; dotnet; band PASS; 3 days", verdict.WhyPassed);
    }

    [Theory]
    [InlineData(185_000, "PASS")]
    [InlineData(180_000, "PASS")]
    [InlineData(179_999, "FLAG")]
    [InlineData(170_000, "FLAG")]
    [InlineData(169_999, "SKIP")]
    [InlineData(null, "NONE")]
    public void The_band_is_judged_on_rule_72_s_two_lines(int? top, string flag)
    {
        Assert.Equal(flag, PayFloor.Judge(top));
        Assert.Equal((180_000, 170_000), (PayFloor.Pass, PayFloor.Flag));
    }

    [Fact]
    public void A_band_under_the_flag_line_is_skipped_with_its_number_and_a_flag_band_passes_flagged()
    {
        Assert.Equal("band tops at $165,000, under $170,000", Fail(Role(band: 165_000)));
        var flagged = JobsGates.Judge(Role(band: 175_000), [], [], Today);
        Assert.Null(flagged.Failed);
        Assert.Equal("FLAG", flagged.BandFlag);
        Assert.Null(Fail(Role(band: null)));
    }

    [Theory]
    [InlineData("Staff Software Engineer", null, null)]
    [InlineData("Lead .NET Developer", null, null)]
    [InlineData("Software Architect", null, null)]
    [InlineData("Engineering Manager, Platform", null, null)]
    [InlineData("Director of Engineering", null, null)]
    [InlineData("Senior Software Engineer", 190_000, null)]
    [InlineData("Senior Software Engineer", null, "senior, no band")]
    [InlineData("Senior Software Engineer", 175_000, "senior, band tops at $175,000")]
    [InlineData("Software Engineer II", 200_000, "level Mid")]
    [InlineData("Product Manager", 200_000, "level Manager, not an engineering title")]
    public void The_level_gate_passes_the_lead_titles_and_a_senior_only_with_a_band_over_the_pass_line(string title, int? band, string? failed) =>
        Assert.Equal(failed, Fail(Role(title: title, band: band)));

    [Theory]
    [InlineData("Remote, US", "Unknown", "remote")]
    [InlineData("United States", "Unknown", "remote")]
    [InlineData("St. Louis, MO", "Hybrid", "hybrid, days unknown")]
    [InlineData("Clayton, MO (Hybrid, one day a week)", "Hybrid", "St. Louis hybrid, 1 day")]
    [InlineData("Chicago, IL", "Hybrid", "hybrid outside St. Louis: Chicago, IL")]
    [InlineData("Boston, MA", "Onsite", "onsite")]
    [InlineData("Boston, MA", "Unknown", "not remote: Boston, MA")]
    [InlineData("Anywhere in the US", "Unknown", "remote")]
    [InlineData("Chesterfield, MO (1 day per week in office)", "Hybrid", "St. Louis hybrid, 1 day")]
    public void The_remote_gate_reads_remote_and_St_Louis_hybrid_and_names_the_rest(string location, string stance, string read) =>
        Assert.Equal(read, JobsGates.RemoteRead(Role(location: location, stance: stance)));

    [Fact]
    public void A_hybrid_or_onsite_role_away_from_St_Louis_fails_and_a_St_Louis_one_with_no_day_count_passes_to_Steve()
    {
        Assert.Equal("onsite", Fail(Role(location: "Boston, MA", stance: "Onsite")));
        Assert.StartsWith("hybrid outside St. Louis", Fail(Role(location: "Chicago, IL", stance: "Hybrid")), StringComparison.Ordinal);
        Assert.Null(Fail(Role(location: "St Louis, MO", stance: "Hybrid")));
    }

    [Fact]
    public void A_role_whose_title_says_remote_passes_wherever_its_location_is()
    {
        var verdict = JobsGates.Judge(Role(title: "Principal Software Engineer (Remote)", location: "Boston, MA", stance: "Unknown"), [], [], Today);
        Assert.Null(verdict.Failed);
        Assert.Equal("remote", verdict.Remote);
    }

    [Fact]
    public void A_role_older_than_21_days_fails_with_its_age()
    {
        Assert.Null(Fail(Role(age: 21)));
        Assert.Equal("posted 22 days ago", Fail(Role(age: 22)));
    }

    [Theory]
    [InlineData("Principal Engineer", "We build in C# on .NET 8", "dotnet")]
    [InlineData("Principal Engineer", "Our services run on Azure Kubernetes Service", "azure")]
    [InlineData("Principal Engineer, AI Platform", "Ship LLM features in Python", "ai lead")]
    [InlineData("Principal Engineer", "", "unread")]
    [InlineData("Principal Engineer", "Go and Rust on AWS", "none")]
    [InlineData("Lead ASP.NET Developer", "", "dotnet")]
    [InlineData("Staff Engineer, Agentic Platform", "Python services", "ai lead")]
    public void The_stack_gate_reads_dotnet_azure_or_an_ai_native_lead_and_leaves_an_unread_alert_to_the_lane(string title, string text, string read) =>
        Assert.Equal(read, JobsGates.StackRead(Role(title: title, text: text)));

    [Fact]
    public void A_role_whose_stack_names_none_of_them_fails()
    {
        Assert.Equal("stack names no C#, .NET or Azure and is not an AI-native lead role", Fail(Role(text: "Go and Rust on AWS")));
    }

    [Fact]
    public void A_staffing_firm_a_former_employer_the_same_role_and_a_fourth_application_in_60_days_fail()
    {
        string[] App(int n, string company, string notes, string day = "10/20/2026") => [n.ToString(System.Globalization.CultureInfo.InvariantCulture), "NA", company, "Greenhouse", day, "", notes];
        var master = new List<string[]>
        {
            App(1, "Acme", "SUBMITTED by Claude: Principal Software Engineer (Greenhouse 1)"),
            App(2, "Umbrella", "SUBMITTED: Lead"), App(3, "Umbrella", "SUBMITTED: Manager"), App(4, "Umbrella", "SUBMITTED: Architect"),
        };
        Assert.Equal("staffing firm placing an unnamed client", Fail(Role(company: "Kforce Inc.")));
        Assert.Equal("staffing firm placing an unnamed client", Fail(Role(company: "Motion Recruitment Partners")));
        Assert.Equal("applied already, Count 1", Fail(Role(), master));
        Assert.StartsWith("RULE 50: Umbrella", Fail(Role(company: "Umbrella", title: "Staff Engineer"), master), StringComparison.Ordinal);
        Assert.Equal("1 in 60 days (Counts 1)", JobsGates.Judge(Role(title: "Staff Engineer"), master, [], Today).TrackerNote);
        Assert.Equal("former employer", Fail(Role(company: FormerEmployers.All[0].Name)));
    }

    [Fact]
    public void The_alert_rows_are_the_leads_file_s_passed_rows_dated_by_their_alert()
    {
        string text = File.ReadAllText(Fixture("leads-2026-11-05.md"));
        var rows = JobsFiles.AlertRows(text);
        var passedLinks = Regex.Matches(text, @"\| (https://\S+) \| \S+ \| pass \|", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(passedLinks, rows.Select(r => r.Url));
        Assert.All(rows, r => Assert.Equal("alert", r.Confirmed));
        Assert.All(rows, r => Assert.InRange(r.Posted!.Value, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 5)));
        Assert.All(rows, r => Assert.StartsWith("alert:", r.Source, StringComparison.Ordinal));
    }

    [Fact]
    public void An_alert_row_s_band_and_section_date_are_read()
    {
        const string text = "\n## 1a1a | LinkedIn digest, jobalerts-noreply@linkedin.com | 2026-11-05 07:46 Central | You may be a fit | 2 requisitions\n\n| Company | Title | Location / remote line as written | Link | Source Gmail id | Filter |\n|---|---|---|---|---|---|\n| PetDesk | Principal Engineer, New Products | United States; band $180K-$200K | https://www.linkedin.com/jobs/view/4485454908/ | 1a1a | pass |\n| Zocdoc | Staff Software Engineer | New York, NY | https://www.linkedin.com/jobs/view/4431872864/ | 1a1a | no: not remote: New York, NY |\n";
        var row = Assert.Single(JobsFiles.AlertRows(text));
        Assert.Equal(("PetDesk", "United States", 180_000, 200_000, new DateOnly(2026, 11, 5), "LinkedIn digest"), (row.Company, row.Location, row.BandMin, row.BandMax, row.Posted!.Value, row.BoardKind));
    }

    [Fact]
    public async Task The_candidates_file_has_its_supply_line_the_passed_rows_and_every_failure_at_its_foot()
    {
        using var scratch = new Scratch();
        scratch.Write("nightly/leads-2026-11-06.md", "\n## 1a1a | LinkedIn digest, x | 2026-11-06 01:10 Central | s | 2 requisitions\n\n| Company | Title | Location / remote line as written | Link | Source Gmail id | Filter |\n|---|---|---|---|---|---|\n| Globex | Staff .NET Engineer | United States | https://www.linkedin.com/jobs/view/2/ | 1a1a | pass |\n| Acme | Principal Software Engineer | United States | https://www.linkedin.com/jobs/view/1/ | 1a1a | pass |\n");
        var postings = new Feed(true)
        {
            Rows = { (1, Role()), (2, Role(title: "Software Engineer II", company: "Initech")), (3, Role(title: "Staff Engineer", company: "Hooli", text: "")) },
            Texts = { [3] = "Go and Rust on AWS" },
        };

        var result = await new JobsPick(scratch.Store, postings, new Tracker([]), new Fixed(Night)).RunAsync(CancellationToken.None);
        string[] lines = scratch.Text("nightly/jobs-candidates-2026-11-06.tsv").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("# scanned 3 postings from 3 boards, 2 alert leads; 2 passed the gates; 1 confirmed by the feeds; tracker sql", lines[0]);
        Assert.Equal(string.Join('\t', JobsFiles.Columns), lines[1]);
        Assert.StartsWith("feed:greenhouse\tAcme\tPrincipal Software Engineer\tPrincipal\t", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("alert:LinkedIn digest\tGlobex\tStaff .NET Engineer\tStaff\t", lines[3], StringComparison.Ordinal);
        Assert.Equal("# FAILED", lines[4]);
        Assert.EndsWith("\tlevel Mid", lines[6], StringComparison.Ordinal);
        Assert.EndsWith("\tstack names no C#, .NET or Azure and is not an AI-native lead role", lines[7], StringComparison.Ordinal);
        Assert.Equal(8, lines.Length);
        Assert.Equal([3L], postings.AskedTexts);
        Assert.Equal((3, 2, 2, 1), (result.Scanned, result.Alerts, result.Passed, result.Confirmed));
    }

    [Fact]
    public async Task Without_the_postings_tables_or_the_tracker_s_copy_the_picker_writes_from_the_alerts_and_the_csv()
    {
        using var scratch = new Scratch();
        scratch.Master("1,NA,Globex,Greenhouse,10/30/2026,,SUBMITTED: Staff .NET Engineer\r\n");
        scratch.Write("nightly/leads-2026-11-05.md", "\n## 1a1a | LinkedIn digest, x | 2026-11-05 07:46 Central | s | 1 requisitions\n\n| Company | Title | Location / remote line as written | Link | Source Gmail id | Filter |\n|---|---|---|---|---|---|\n| Globex | Staff .NET Engineer | United States | https://www.linkedin.com/jobs/view/2/ | 1a1a | pass |\n");

        var result = await new JobsPick(scratch.Store, new Feed(false), new Tracker(null), new Fixed(Night)).RunAsync(CancellationToken.None);

        Assert.Contains("postings blocked: no_postings, alert leads alone; tracker csv (the sql copy did not answer: InvalidOperationException)", result.Line, StringComparison.Ordinal);
        Assert.EndsWith("\tapplied already, Count 1", scratch.Text("nightly/jobs-candidates-2026-11-06.tsv").TrimEnd('\n').Split('\n')[^1], StringComparison.Ordinal);
        Assert.Equal(0, result.Passed);
    }

    [Fact]
    public void The_picker_s_hour_is_two_o_clock_Central_and_its_event_rides_the_nightly_switch()
    {
        Assert.True(CentralClock.IsJobsHour(Night));
        Assert.True(CentralClock.IsJobsHour(new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.Zero)));
        Assert.False(CentralClock.IsJobsHour(Night.AddHours(1)));
        Assert.Null(CentralClock.Slot(Night));
        Assert.Equal("NIGHTLY_DISPATCH", Dispatch.Gate(JobsFiles.Event));
    }

    [Fact]
    public void Every_column_the_picker_reads_is_declared_by_the_sql_project()
    {
        var tables = new[] { "Postings", "PostingDescriptions", "Companies", "Tracker" }.ToDictionary(t => t, t => Column().Matches(File.ReadAllText(Path.Combine(Root(), "functions", "JobHunt.Database", "Tables", t + ".sql"))).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal));
        var aliases = new Dictionary<string, string> { ["p"] = "Postings", ["d"] = "PostingDescriptions", ["n"] = "PostingDescriptions", ["c"] = "Companies", ["t"] = "Tracker" };
        var statements = typeof(SqlJobsReader).Assembly.GetType("JobHunt.Infrastructure.Features.Jobs.JobsSql", throwOnError: true)!
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        var wrong = statements.SelectMany(sql => Qualified().Matches(sql)).Where(m => !tables[aliases[m.Groups[1].Value]].Contains(m.Groups[2].Value)).Select(m => m.Value).ToList();
        Assert.True(wrong.Count == 0, string.Join(", ", wrong));
        Assert.Equal(4, statements.Count);
        Assert.All(statements, sql => Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE)\b", sql));
        Assert.True(SqlJobsReader.Yearly("per-year-salary") && SqlJobsReader.Yearly("1 YEAR") && !SqlJobsReader.Yearly("per-hour-wage"));
    }

    [Fact]
    public async Task The_clock_s_two_o_clock_work_writes_the_candidates_and_one_log_line_with_no_database()
    {
        Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CAMPAIGN_SQL")), "the test host has no database setting");
        using var scratch = new Scratch();
        scratch.Master("");
        scratch.Write("nightly/leads-2026-11-06.md", "\n## 1a1a | LinkedIn digest, x | 2026-11-06 01:10 Central | s | 1 requisitions\n\n| Company | Title | Location / remote line as written | Link | Source Gmail id | Filter |\n|---|---|---|---|---|---|\n| Globex | Staff .NET Engineer | United States | https://www.linkedin.com/jobs/view/2/ | 1a1a | pass |\n");

        string line = await new JobsHour(scratch.Store, new Fixed(Night), Microsoft.Extensions.Logging.Abstractions.NullLogger<JobsHour>.Instance).PickAsync(CancellationToken.None);

        Assert.StartsWith("nightly/jobs-candidates-2026-11-06.tsv: scanned 0 postings from 0 boards, 1 alert leads; 1 passed the gates; 0 confirmed by the feeds; postings blocked", line, StringComparison.Ordinal);
        Assert.Equal($"2026-11-06 02:00:00 {line}\n", scratch.Text(JobsHour.Log));
        Assert.True(scratch.Has("nightly/jobs-candidates-2026-11-06.tsv"));
    }

    [Fact]
    public void Every_candidates_row_has_the_header_s_cells_and_no_tab_or_line_break_inside_one()
    {
        var odd = Role(title: "Staff Engineer\tPayments\nTeam", company: "Acme\r\nCorp");
        var passed = new[] { JobsGates.Judge(Role(), [], [], Today), JobsGates.Judge(odd, [], [], Today) };
        var failed = new[] { JobsGates.Judge(Role(age: 40), [], [], Today) };
        string[] lines = JobsFiles.Write("supply\tline", passed, failed).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("# supply line", lines[0]);
        Assert.All(lines[2..4], row => Assert.Equal(JobsFiles.Columns.Count, row.Split('\t').Length));
        Assert.Equal(JobsFiles.Columns.Count + 1, lines[^1].Split('\t').Length);
        Assert.Contains("\tStaff Engineer Payments Team\t", lines[3], StringComparison.Ordinal);
        Assert.Contains("\t$170,000-$210,000\t", JobsFiles.Write("s", [JobsGates.Judge(Role() with { BandMin = 170_000 }, [], [], Today)], []), StringComparison.Ordinal);
        Assert.EndsWith("\tposted 40 days ago", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_alert_file_s_other_tables_and_rows_that_did_not_pass_are_not_read()
    {
        const string text = "\n## 1a1a | Ladders, jobs@my.theladders.com | 2026-11-05 09:00 Central | s | 3 requisitions\n\n| Company | Title | Location | Link | Source Gmail id |\n|---|---|---|---|---|\n| Initech | Staff Engineer | Remote | https://l/1/ | 1a1a |\n| Company | Title | Location / remote line as written | Link | Source Gmail id | Filter |\n|---|---|---|---|---|---|\n| Hooli | Staff Engineer | Remote | not in email text | 1a1a | pass |\n| Umbrella | Lead Engineer | Remote | https://l/2/ | 1a1a | no: RULE 50 |\n| Globex | Lead Engineer | Remote | https://l/3/ | 1a1a | pass |\n| Globex | Lead Engineer | Remote | https://l/3/ | 1a1b | pass |\n";
        var row = Assert.Single(JobsFiles.AlertRows(text));
        Assert.Equal(("Globex", "https://l/3/", "Ladders", "alert:Ladders"), (row.Company, row.Url, row.BoardKind, row.Source));
        Assert.Equal("nightly/jobs-candidates-2026-11-06.tsv", JobsFiles.Candidates(Today));
        Assert.Equal("nightly/leads-2026-11-06.md", JobsFiles.Leads(Today));
    }

    [Fact]
    public async Task Confirmed_roles_rank_first_then_the_freshest_and_one_role_is_listed_once()
    {
        using var scratch = new Scratch();
        scratch.Write("nightly/leads-2026-11-06.md", "\n## 1a1a | LinkedIn digest, x | 2026-11-06 01:10 Central | s | 2 requisitions\n\n| Company | Title | Location / remote line as written | Link | Source Gmail id | Filter |\n|---|---|---|---|---|---|\n| Fresh Co | Staff .NET Engineer | United States | https://www.linkedin.com/jobs/view/9/ | 1a1a | pass |\n| ACME | Principal Software Engineer | United States | https://www.linkedin.com/jobs/view/1/ | 1a1a | pass |\n");
        var postings = new Feed(true) { Rows = { (1, Role(age: 9)), (2, Role(title: "Lead .NET Engineer", company: "Globex", age: 1)), (3, Role(age: 9) with { Url = "https://job-boards.greenhouse.io/acme/jobs/2" }) } };

        await new JobsPick(scratch.Store, postings, new Tracker([]), new Fixed(Night)).RunAsync(CancellationToken.None);
        var rows = scratch.Text("nightly/jobs-candidates-2026-11-06.tsv").Split('\n').Skip(2).TakeWhile(l => l != "# FAILED").Select(l => l.Split('\t')).ToList();

        Assert.Equal(["Globex", "Acme", "Fresh Co"], rows.Select(r => r[1]));
        Assert.Equal(["feed", "feed", "alert"], rows.Select(r => r[11]));
        Assert.Equal("", JobsGates.Judge(Role(age: 30), [], [], Today).WhyPassed);
    }

    [Fact]
    public async Task The_picker_reads_today_s_and_yesterday_s_alerts_and_not_the_day_before()
    {
        using var scratch = new Scratch();
        string Day(string date, string company, int id) => $"\n## 1a | LinkedIn digest, x | {date} 07:00 Central | s | 1 requisitions\n\n| Company | Title | Location / remote line as written | Link | Source Gmail id | Filter |\n|---|---|---|---|---|---|\n| {company} | Staff .NET Engineer | United States | https://www.linkedin.com/jobs/view/{id}/ | 1a | pass |\n";
        scratch.Write("nightly/leads-2026-11-06.md", Day("2026-11-06", "Today Co", 1));
        scratch.Write("nightly/leads-2026-11-05.md", Day("2026-11-05", "Yesterday Co", 2));
        scratch.Write("nightly/leads-2026-11-04.md", Day("2026-11-04", "Older Co", 3));

        var result = await new JobsPick(scratch.Store, new Feed(false), new Tracker([]), new Fixed(Night)).RunAsync(CancellationToken.None);

        Assert.Equal((2, 2), (result.Alerts, result.Passed));
        Assert.DoesNotContain("Older Co", scratch.Text(result.Path), StringComparison.Ordinal);
    }

    private static readonly DateTimeOffset Night = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Database")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/JobHunt.Database not found above " + AppContext.BaseDirectory);
    }

    private static string Fixture(string name) => Path.Combine(Root(), "functions", "jobhunt.Tests", "Fixtures", "leads", name);

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Feed(bool ready) : IJobsPostings
    {
        public List<(long Key, Candidate Candidate)> Rows { get; } = [];

        public Dictionary<long, string> Texts { get; } = [];

        public List<long> AskedTexts { get; } = [];

        public Task<bool> ReadyAsync(CancellationToken ct) => Task.FromResult(ready);

        public Task<IReadOnlyList<(long Key, Candidate Candidate)>> OpenAsync(DateTimeOffset seenSince, CancellationToken ct) => Task.FromResult<IReadOnlyList<(long, Candidate)>>(Rows);

        public Task<IReadOnlyDictionary<long, string>> TextsAsync(IReadOnlyList<long> keys, CancellationToken ct)
        {
            AskedTexts.AddRange(keys.Where(Texts.ContainsKey));
            return Task.FromResult<IReadOnlyDictionary<long, string>>(Texts.Where(t => keys.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value));
        }
    }

    private sealed class Tracker(IReadOnlyList<string[]>? rows) : ITrackerRows
    {
        public Task<IReadOnlyList<string[]>> RowsAsync(CancellationToken ct) => rows is null ? throw new InvalidOperationException("no copy") : Task.FromResult(rows);
    }

    [GeneratedRegex(@"^\s*\[(\w+)\]", RegexOptions.Multiline)]
    private static partial Regex Column();

    [GeneratedRegex(@"\b([pdnct])\.\[(\w+)\]")]
    private static partial Regex Qualified();
}
