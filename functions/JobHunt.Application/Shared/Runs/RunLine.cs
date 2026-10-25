using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Application.Shared.Runs;

/// <summary>
/// One process's run, as one line in Application Insights (ADR-109, the addendum on the six processes): the
/// process's name, when it started, how long it took, what it came to in one word, and its counts. Written at
/// the end of every run, in a finally, so a run that threw still writes its line. It carries counts and names
/// only, never an address, a message body or a file's contents: a detail is one word of letters, digits and
/// underscores (a slot's name, a reason, an exception's type), and anything else is dropped.
/// </summary>
public sealed partial class RunLine(string process, DateTimeOffset started, IReadOnlyList<int>? slotMinutes = null)
{
    /// <summary>The words a run can come to.</summary>
    public static readonly IReadOnlyList<string> Outcomes = ["success", "late", "blocked", "failed", "planned", "unchanged"];

    private readonly SortedDictionary<string, long> _counts = new(StringComparer.Ordinal);

    /// <summary>The process's name: AtsSweep, DailyCard, Merger, MergerHealth, Nightly or SheetMirror.</summary>
    public string Process { get; } = process;

    /// <summary>When the run started, UTC.</summary>
    public DateTimeOffset Started { get; } = started;

    /// <summary>What the run came to so far; a run that is never told otherwise came to success.</summary>
    public string Outcome { get; private set; } = "success";

    /// <summary>The one word beside the outcome: the reason, the slot, the exception's type; null for none.</summary>
    public string? Detail { get; private set; }

    /// <summary>The counts, by name.</summary>
    public IReadOnlyDictionary<string, long> Counts => _counts;

    /// <summary>Sets what the run came to, with its one word.</summary>
    public RunLine Came(string outcome, string? detail = null)
    {
        Outcome = Outcomes.Contains(outcome) ? outcome : throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "not one of the six outcome words");
        Detail = Word(detail);
        return this;
    }

    /// <summary>A run that threw: failed, with the exception's type name.</summary>
    public RunLine Failed(Exception ex) => Came("failed", ex.GetType().Name);

    /// <summary>Keeps a count by its name (lower case, letters, digits and underscores).</summary>
    public RunLine Count(string name, long value)
    {
        if (Name().IsMatch(name))
        {
            _counts[name] = value;
        }
        return this;
    }

    /// <summary>
    /// The line, read at the run's end: "process:Merger outcome=success took_ms=812 started=2026-10-24T15:00:00Z
    /// merged=3". A run that came to success more than a minute after its last scheduled minute came to late,
    /// with the seconds after the slot as its word.
    /// </summary>
    public string Text(DateTimeOffset ended)
    {
        string outcome = Outcome, detail = Detail ?? "";
        if (outcome == "success" && SecondsLate() is > 60 and var late)
        {
            outcome = "late";
            detail = late.ToString(CultureInfo.InvariantCulture);
        }
        var parts = new List<string>
        {
            $"process:{Process}",
            $"outcome={outcome}",
            $"took_ms={(long)Math.Max(0, (ended - Started).TotalMilliseconds)}",
            $"started={Started.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}",
        };
        if (detail.Length > 0)
        {
            parts.Add($"detail={detail}");
        }
        parts.AddRange(_counts.Select(c => $"{c.Key}={c.Value.ToString(CultureInfo.InvariantCulture)}"));
        return string.Join(' ', parts);
    }

    /// <summary>Seconds after the last scheduled minute at or before the start; null for a process with no slots.</summary>
    public long? SecondsLate()
    {
        if (slotMinutes is not { Count: > 0 } minutes)
        {
            return null;
        }
        var hour = new DateTimeOffset(Started.Year, Started.Month, Started.Day, Started.Hour, 0, 0, TimeSpan.Zero);
        var slot = minutes.Select(m => hour.AddMinutes(m)).Where(at => at <= Started).DefaultIfEmpty(hour.AddHours(-1).AddMinutes(minutes.Max())).Max();
        return (long)(Started - slot).TotalSeconds;
    }

    /// <summary>A detail kept only when it is one word of letters, digits and underscores, up to sixty.</summary>
    public static string? Word(string? detail) => detail is not null && Detailish().IsMatch(detail) ? detail : null;

    [GeneratedRegex(@"^[A-Za-z0-9_]{1,60}$")]
    private static partial Regex Detailish();

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,30}$")]
    private static partial Regex Name();
}
