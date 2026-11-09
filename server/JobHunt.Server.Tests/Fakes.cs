using System.Text.RegularExpressions;

namespace JobHunt.Server.Tests;

/// <summary>Reads a checked-in fixture cut from the real file of the same shape.</summary>
internal static class Fixture
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}

/// <summary>A clock held at one instant.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>The share in memory, every write held to the same write policy the real one is.</summary>
internal sealed class MemoryShare : IShare
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public string? Refused { get; set; }

    public int RefuseSpool { get; set; }

    public bool PartialSpool { get; set; }

    public string Root => "/mnt/j";

    public string Full(string relative) => $"{Root}/{relative}";

    public Task<string> ReadAsync(string relative, CancellationToken ct) =>
        Files.TryGetValue(relative, out string? text) ? Task.FromResult(text) : throw new FileNotFoundException(relative);

    public Task<string> TailAsync(string relative, int bytes, CancellationToken ct) => ReadAsync(relative, ct);

    public Task<IReadOnlyList<ShareFile>> ListAsync(string folder, string pattern, CancellationToken ct)
    {
        string prefix = folder.Length == 0 ? string.Empty : folder + "/";
        var match = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$");
        IReadOnlyList<ShareFile> files = [.. Files.Keys
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal) && !path[prefix.Length..].Contains('/') && match.IsMatch(path[prefix.Length..]))
            .Select(path => new ShareFile(path, DateTimeOffset.Parse("2026-11-02T00:00:00Z")))];
        return Task.FromResult(files);
    }

    public Task<IReadOnlyList<string>> FoldersAsync(string folder, string pattern, CancellationToken ct)
    {
        var match = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$");
        IReadOnlyList<string> folders = [.. Files.Keys
            .Where(path => path.StartsWith(folder + "/", StringComparison.Ordinal))
            .Select(path => path[(folder.Length + 1)..].Split('/'))
            .Where(parts => parts.Length > 1 && match.IsMatch(parts[0]))
            .Select(parts => $"{folder}/{parts[0]}")
            .Distinct()];
        return Task.FromResult(folders);
    }

    public Task<bool> ExistsAsync(string relative, CancellationToken ct) => Task.FromResult(Files.ContainsKey(relative));

    public Task WriteAsync(string relative, string text, CancellationToken ct)
    {
        Files[Allowed(relative)] = text;
        return Task.CompletedTask;
    }

    public Task WriteNewAsync(string relative, string text, CancellationToken ct)
    {
        if (RefuseSpool > 0 && relative.StartsWith("inbox/tracker-inbox/", StringComparison.Ordinal))
        {
            RefuseSpool--;
            throw new IOException($"{relative} refused");
        }
        if (PartialSpool && relative.StartsWith("inbox/tracker-inbox/", StringComparison.Ordinal))
        {
            Files[relative] = text[..(text.Length / 2)];
            throw new IOException($"{relative} did not read back as written");
        }
        if (!Files.TryAdd(Allowed(relative), text))
        {
            throw new IOException($"{relative} exists");
        }
        return Task.CompletedTask;
    }

    public Task AppendAsync(string relative, string text, CancellationToken ct)
    {
        if (relative == Refused)
        {
            throw new IOException($"{relative} refused");
        }
        Files[Allowed(relative)] = Files.GetValueOrDefault(relative, string.Empty) + text;
        return Task.CompletedTask;
    }

    public Task CopyInAsync(string from, string relative, CancellationToken ct)
    {
        Files[Allowed(relative)] = "copied from " + from;
        return Task.CompletedTask;
    }

    private static string Allowed(string relative) =>
        WritePolicy.Allowed(relative) ? relative : throw new UnauthorizedAccessException(relative);
}

/// <summary>A CLI that answers each lane from a script and records what it was asked.</summary>
internal sealed class ScriptedClaude(Func<ClaudeRequest, ClaudeRun> script) : IClaudeCli
{
    public List<ClaudeRequest> Requests { get; } = [];

    public Task<ClaudeRun> RunAsync(ClaudeRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(script(request));
    }
}

/// <summary>The stream's result line for a structured result.</summary>
internal static class ResultLine
{
    public static string With(string structured) =>
        $$"""{"type":"result","subtype":"success","num_turns":4,"duration_ms":1000,"total_cost_usd":0.05,"usage":{"input_tokens":1,"cache_creation_input_tokens":2,"cache_read_input_tokens":3,"output_tokens":4},"structured_output":{{structured}}}""";

    public static string Empty => With("""{"rows":[],"live_invite_count":17,"stop_condition":"none","stop_detail":null,"stop_screenshot":null}""");
}

/// <summary>A minter that counts its mints and hands out fixed codes.</summary>
internal sealed class CountingMinter : ICodeMinter
{
    public List<string> Asked { get; } = [];

    public string? Code { get; set; } = "abc123";

    public Task<string?> MintAsync(string person, string company, CancellationToken ct)
    {
        Asked.Add($"{person}|{company}");
        return Task.FromResult(Code);
    }
}

/// <summary>A resume builder that records what it was asked and answers a fixed build.</summary>
internal sealed class FakeResumes : IResumeBuilder
{
    public List<string> Asked { get; } = [];

    public string? Failure { get; set; }

    public Task<ResumeBuild> BuildAsync(string title, string company, string folder, CancellationToken ct)
    {
        Asked.Add($"{title}|{company}|{folder}");
        return Task.FromResult(new ResumeBuild($"{folder}/StevenStoutResume_2026-10-29.pdf", title.ToUpperInvariant(), "abc123", Failure));
    }
}

/// <summary>A proofread that passes everything but what it is told to fail.</summary>
internal sealed class FakeProse(params string[] failing) : IProseCheck
{
    public Task<bool> CleanAsync(string text, CancellationToken ct) => Task.FromResult(!failing.Any(text.Contains));
}

/// <summary>A usage sink that keeps what it was given.</summary>
internal sealed class KeptUsage : IUsageSink
{
    public List<string> Documents { get; } = [];

    public Task<string> ReportAsync(string document, CancellationToken ct)
    {
        Documents.Add(document);
        return Task.FromResult("usage not reported: no key");
    }
}

/// <summary>A mount that answers what it is told.</summary>
internal sealed class FakeMount(string? answer = null) : IMount
{
    public Task<string?> FreshAsync(CancellationToken ct) => Task.FromResult(answer);
}

/// <summary>A lock that can be held by somebody else.</summary>
internal sealed class FakeLock(bool free = true) : IRunLock
{
    public Task<IAsyncDisposable?> TakeAsync(string dayFolder, CancellationToken ct) =>
        Task.FromResult<IAsyncDisposable?>(free ? new Nothing() : null);

    private sealed class Nothing : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>A desktop that answers what it is told.</summary>
internal sealed class FakeDesktop(string? answer = null) : IDesktop
{
    public Task<string?> EnsureChromeAsync(CancellationToken ct) => Task.FromResult(answer);
}

/// <summary>Everything a whole run stands on, over the fixtures.</summary>
internal sealed class Morning
{
    public static readonly DateTimeOffset At = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    public Morning(Func<ClaudeRequest, ClaudeRun> script, string tracker = "")
    {
        Share = new MemoryShare();
        Claude = new ScriptedClaude(script);
        Share.Files["Job_Hunt_Tracker.csv"] = tracker.Length > 0 ? tracker : Fixture.Read("tracker.csv");
        Share.Files["outreach-limits.md"] = Fixture.Read("limits.md");
        Share.Files["CONVENTIONS.md"] = Fixture.Read("conventions.md");
        Share.Files["systemRules.md"] = Fixture.Read("rules.md");
        Share.Files["responders.md"] = Fixture.Read("responders.md");
        Share.Files["state.md"] = Fixture.Read("state-tail.md");
        Share.Files["linkedin-proof/QUEUE-linuxreach-2026-10-27-0858.md"] = Fixture.Read("queue.md");
        Share.Files["linkedin-proof/QUEUE-linuxreach-2026-11-02-1021-status.md"] = Fixture.Read("queue-status.md");
        Share.Files["nightly/2026-11-02/0400-reply-opus-5-5/linkedin-queue.md"] = Fixture.Read("linkedin-queue.md");
        Share.Files["APPLY-SWEEP-2026-10-28-ainative.md"] = Fixture.Read("sweep.md");
        Share.Files["leads/scored.tsv"] = Fixture.Read("scored.tsv");
        Share.Files["nightly/LEADS.md"] = Fixture.Read("leads.md");
        Share.Files["Steven_Stout_Resume_2026-10-29.pdf"] = "pdf";
    }

    public MemoryShare Share { get; }

    public ScriptedClaude Claude { get; }

    public CountingMinter Minter { get; } = new();

    public FakeResumes Resumes { get; } = new();

    public FakeProse Prose { get; set; } = new();

    public KeptUsage Usage { get; } = new();

    public FixedTime Time { get; } = new(At);

    public ServerConfig Config { get; } = new()
    {
        Lanes = new Dictionary<string, LaneConfig>
        {
            ["feed"] = new() { Model = "claude-opus-5-5", Effort = "high", Enabled = false },
            ["invites"] = new() { TargetModel = "claude-haiku-5-5" },
            ["messages"] = new(),
            ["network"] = new() { Enabled = false },
            ["apply"] = new() { Effort = "medium" },
        },
    };

    public RunLanes Lanes(IMount? mount = null, IRunLock? runLock = null, IDesktop? desktop = null)
    {
        return new(
            Share,
            new RunGuards(mount ?? new FakeMount(), runLock ?? new FakeLock(), desktop ?? new FakeDesktop(), Time),
            new LaneWork(new DayReader(Share), new WorklistBuilder(Share, Minter, new FakeProse(), Resumes, Config), new LaneRunner(Share, new EssayDesk(Share, Prose, Claude), Usage, Config, Time), new InviteRecount(Share, Claude, Config), new FeedLane(Share, new FeedTools(Claude, Prose, new UsageReport(Share, Usage, Config, Time)), Config, Time), new NetworkLane(Share, new NetworkTools(Claude, new UsageReport(Share, Usage, Config, Time)), Config, Time)),
            Config);
    }

    public IEnumerable<string> TrackerRows() => Share.Files.Keys.Where(path => path.StartsWith("inbox/tracker-inbox/", StringComparison.Ordinal));
}
