using System.Text.Json;

namespace TheYard.Api;

// #region test-summary
/// <summary>
/// The gate's counts for this build, without the tests themselves (1.0.2.0).
///
/// <para>The landing page's evidence strip is the first thing a reader who has
/// never seen this project is asked to believe: every version runs three
/// suites in one gate and rolls only if they are green. The claim is only
/// worth making if the numbers are the build's own, so they come from the file
/// the gate wrote, `data/test-results.json`, the same file the Admin tab's
/// tests card reads. That file is 160 KB because it carries every test by
/// name; the strip needs six numbers, so this reads the suites and drops the
/// rows.</para>
///
/// <para>A suite the gate skipped and carried forward keeps the version whose
/// gate ran it, so a reader is never shown a number this build did not
/// produce without being told which one did.</para>
/// </summary>
public sealed record TestSuiteCount(
    string Id,
    string Name,
    int Passed,
    int Failed,
    int Skipped,
    int Seconds,
    string? Carried);

/// <summary>Every suite's counts, and the totals across them.</summary>
public sealed record TestSummaryReport(
    string Version,
    string RanAt,
    int GateSeconds,
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<TestSuiteCount> Suites);

/// <summary>Reads the counts out of the gate's results file, once per file.</summary>
public static class TestSummary
{
    private static readonly object Gate = new();
    private static (string Path, long Length, DateTime WrittenUtc)? _read;
    private static TestSummaryReport? _summary;

    /// <summary>The counts for the results file at <paramref name="path"/>, cached until the file changes.</summary>
    public static TestSummaryReport Of(string path)
    {
        var info = new FileInfo(path);
        var stamp = (path, info.Length, info.LastWriteTimeUtc);
        lock (Gate)
        {
            if (_summary is not null && _read == stamp)
            {
                return _summary;
            }

            var read = Parse(File.ReadAllText(path));
            _summary = read;
            _read = stamp;
            return read;
        }
    }

    /// <summary>The same, from the text, so a test does not need a file.</summary>
    public static TestSummaryReport Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var suites = new List<TestSuiteCount>();
        if (root.TryGetProperty("suites", out var listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (var suite in listed.EnumerateArray())
            {
                suites.Add(new TestSuiteCount(
                    Text(suite, "id"),
                    Text(suite, "name"),
                    Number(suite, "passed"),
                    Number(suite, "failed"),
                    Number(suite, "skipped"),
                    Number(suite, "seconds"),
                    suite.TryGetProperty("carried", out var carried) && carried.ValueKind == JsonValueKind.String
                        ? carried.GetString()
                        : null));
            }
        }

        return new TestSummaryReport(
            Text(root, "version"),
            Text(root, "ranAt"),
            Number(root, "gateSeconds"),
            suites.Sum(suite => suite.Passed),
            suites.Sum(suite => suite.Failed),
            suites.Sum(suite => suite.Skipped),
            suites);
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
}
// #endregion test-summary
