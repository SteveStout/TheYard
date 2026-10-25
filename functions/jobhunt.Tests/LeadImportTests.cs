using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The lead run's rows into the database (ADR-118, Decision 5): a made-up run file held to LeadSpool's rules over
/// memory stores (a company with no registrable key refused, a Track A lead with no application refused, a personal
/// address refused, a company neither in the file nor the book refused, a tell in WhatTheyDo kept and blocked), the
/// file moved to merged/ or rejected/ with its line, MX asked once an address, Strength computed by code; the board
/// scan's title rule and its write-back; and the three boards' and the resolver's JSON read from their shapes.
/// </summary>
public sealed class LeadImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 15, 20, 0, TimeSpan.Zero);

    private static string Fixture(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "functions", "jobhunt.Tests", "Fixtures", "campaign", name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        throw new InvalidOperationException(name + " is not found");
    }

    private static (LeadImport Import, Book Db, FakeMx Mx, string Root) Import(params (string Name, string Text)[] files)
    {
        string root = Path.Combine(Path.GetTempPath(), "lead-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "campaign", "leads-inbox"));
        foreach (var (name, text) in files)
        {
            File.WriteAllText(Path.Combine(root, "campaign", "leads-inbox", name), text);
        }
        var db = new Book();
        var mx = new FakeMx();
        return (new LeadImport(new FolderMergeStore(root), db, db, mx, new FixedTime(Now)), db, mx, root);
    }

    [Fact]
    public async Task The_run_s_rows_are_held_to_the_rules_and_the_file_moves_to_merged_with_its_line()
    {
        var (import, db, mx, root) = Import(("run.json", Fixture("leads-run.json")));

        string line = await import.RunAsync(CancellationToken.None);

        Assert.Equal("1 lead files, 1 companies, 2 leads written, 1 blocked, 3 refused", line);
        Assert.Equal(["acmedental.com"], db.Companies.Select(c => c.Key));
        Assert.Equal(["Pat Lee", "Sam Roe"], db.Leads.Select(l => l.Lead.ContactName));
        var pat = db.Leads[0];
        Assert.Equal((true, (string?)null), (pat.Mx, pat.Blocked));
        Assert.Equal(["acmedental.com"], mx.Asked);
        // Remote 3, two evidence rows 2, no posted role, no band, ghost 1 minus 1, first seen today 1, a .NET posting in 30 days 1, the model's step 1.
        Assert.Equal(3 + 2 - 1 + 1 + 1 + 1, pat.Strength);
        Assert.Matches("^[0-9a-f]{16}$", pat.Hash);
        Assert.Equal(("WhatTheyDo: a tell (robust)", (bool?)null), (db.Leads[1].Blocked, db.Leads[1].Mx));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "campaign", "leads-inbox"), "*.json"));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "campaign", "leads-inbox", "merged")));
        string log = File.ReadAllText(Path.Combine(root, "campaign", "leads-inbox", "import.log"));
        Assert.Contains("company mail.globex.com: key mail.globex.com is not a registrable domain", log, StringComparison.Ordinal);
        Assert.Contains("lead at acmedental.com: Track A without the application's time and the role", log, StringComparison.Ordinal);
        Assert.Contains("lead at acmedental.com: a personal address", log, StringComparison.Ordinal);
        Assert.Contains("lead at initech.com: company initech.com is not in the file or the book", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_that_is_not_a_lead_file_goes_to_rejected_and_nothing_is_written()
    {
        var (import, db, _, root) = Import(("bad.json", "{ \"companies\": 3 }"), ("notes.txt", "not json"));

        string line = await import.RunAsync(CancellationToken.None);

        Assert.Equal("1 lead files, 0 companies, 0 leads written, 0 blocked, 1 refused", line);
        Assert.Empty(db.Companies);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "campaign", "leads-inbox", "rejected")));
        Assert.True(File.Exists(Path.Combine(root, "campaign", "leads-inbox", "notes.txt")));
    }

    [Fact]
    public async Task Nothing_waiting_reads_nothing()
    {
        var (import, _, _, _) = Import();
        Assert.Equal("0 lead files", await import.RunAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("Senior .NET Engineer", true)]
    [InlineData("Staff Engineer, C#", true)]
    [InlineData("Backend Engineer (dotnet)", true)]
    [InlineData("Senior Java Engineer", false)]
    [InlineData("Network Engineer", false)]
    public void The_scan_counts_a_posting_as_dot_net_by_its_title(string title, bool dotNet) => Assert.Equal(dotNet, BoardScan.IsDotNet(title));

    [Fact]
    public async Task The_scan_reads_each_board_once_and_writes_the_newest_dot_net_posting_back()
    {
        var db = new Book { Boards = [new BoardCompany(1, "greenhouse", "acme"), new BoardCompany(2, "lever", "globex"), new BoardCompany(3, "ashby", "gone")] };
        var boards = new FakeBoards
        {
            ["acme"] = [new("Senior .NET Engineer", "", new DateOnly(2026, 10, 1)), new("Staff .NET Engineer", "", new DateOnly(2026, 10, 20)), new("Designer", "", new DateOnly(2026, 10, 23))],
            ["globex"] = [new("Java Engineer", "", new DateOnly(2026, 10, 2))],
        };

        string line = await new BoardScan(db, boards, new FixedTime(Now)).RunAsync(CancellationToken.None);

        Assert.Equal("3 boards, 2 answered, 4 postings, 2 .NET", line);
        Assert.Equal([(1, (DateOnly?)new DateOnly(2026, 10, 20), "Staff .NET Engineer"), (2, null, null)], db.Scanned);
        Assert.Equal(3, boards.Asked);
    }

    [Fact]
    public void The_three_boards_json_reads_to_titles_and_days()
    {
        var greenhouse = PublicBoardReader.Read("greenhouse", "{\"jobs\":[{\"title\":\"Senior .NET Engineer\",\"updated_at\":\"2026-10-20T10:00:00-04:00\",\"first_published\":\"2026-10-09T09:00:00-04:00\"}]}");
        var lever = PublicBoardReader.Read("lever", "[{\"text\":\"Staff Engineer, C#\",\"createdAt\":1792800000000}]");
        var ashby = PublicBoardReader.Read("ashby", "{\"jobs\":[{\"title\":\"Platform Engineer\",\"publishedAt\":\"2026-10-22T00:00:00.000+00:00\"}]}");

        Assert.Equal(new BoardPosting("Senior .NET Engineer", "", new DateOnly(2026, 10, 9)), greenhouse.Single());
        Assert.Equal(("Staff Engineer, C#", (DateOnly?)new DateOnly(2026, 10, 24)), (lever.Single().Title, lever.Single().Posted));
        Assert.Equal(new DateOnly(2026, 10, 22), ashby.Single().Posted);
        Assert.Equal("https://boards-api.greenhouse.io/v1/boards/acme/jobs", PublicBoardReader.Address("greenhouse", "acme"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PublicBoardReader.Address("workday", "acme"));
    }

    [Theory]
    [InlineData("{\"Status\":0,\"Answer\":[{\"name\":\"acme.com.\",\"type\":15,\"data\":\"10 mx.acme.com.\"}]}", true)]
    [InlineData("{\"Status\":0,\"Answer\":[{\"name\":\"acme.com.\",\"type\":5,\"data\":\"other.com.\"}]}", false)]
    [InlineData("{\"Status\":0}", false)]
    [InlineData("{\"Status\":3}", false)]
    [InlineData("{\"Status\":2}", null)]
    public void The_resolver_s_answer_is_mail_no_mail_or_not_known(string json, bool? mail) => Assert.Equal(mail, DohMxCheck.Read(json));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeMx : IMxCheck
    {
        public List<string> Asked { get; } = [];

        public Task<bool?> HasMxAsync(string domain, CancellationToken ct)
        {
            Asked.Add(domain);
            return Task.FromResult<bool?>(true);
        }
    }

    private sealed class FakeBoards : Dictionary<string, IReadOnlyList<BoardPosting>>, IBoardReader
    {
        public int Asked { get; private set; }

        public Task<IReadOnlyList<BoardPosting>?> PostingsAsync(string kind, string token, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(TryGetValue(token, out var postings) ? postings : null);
        }
    }

    private sealed class Book : ICompanyBook, ILeadStore
    {
        public List<SpoolCompany> Companies { get; } = [];

        public List<(SpoolLead Lead, bool? Mx, int Strength, string Hash, string? Blocked)> Leads { get; } = [];

        public IReadOnlyList<BoardCompany> Boards { get; init; } = [];

        public List<(int, DateOnly?, string?)> Scanned { get; } = [];

        public Task<CompanyRecord?> FindAsync(string key, CancellationToken ct) => Task.FromResult<CompanyRecord?>(null);

        public Task MarkDoNotEmailAsync(int companyId, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;

        public Task<int> SaveAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct)
        {
            Companies.Add(company);
            return Task.FromResult(Companies.Count);
        }

        public Task<IReadOnlyList<BoardCompany>> BoardsAsync(CancellationToken ct) => Task.FromResult(Boards);

        public Task MarkScannedAsync(int companyId, DateOnly? lastDotNetPostingAt, string? lastDotNetPostingTitle, DateTimeOffset at, CancellationToken ct)
        {
            Scanned.Add((companyId, lastDotNetPostingAt, lastDotNetPostingTitle));
            return Task.CompletedTask;
        }

        public Task SaveAsync(int companyId, SpoolLead lead, bool? mx, int strength, string inputsHash, string? blocked, string run, DateTimeOffset at, CancellationToken ct)
        {
            Leads.Add((lead, mx, strength, inputsHash, blocked));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LeadRecord>> OpenAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<LeadRecord>>([]);

        public Task BlockAsync(long leadId, string reason, CancellationToken ct) => Task.CompletedTask;

        public Task MarkUnverifiedAsync(long leadId, string reason, CancellationToken ct) => Task.CompletedTask;
    }
}
