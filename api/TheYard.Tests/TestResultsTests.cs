using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// Every test result on the Admin tab (ADR: The five-minute gate, the addendum
/// on every check running once). The gate runs every suite once and writes
/// what each test did into data/test-results.json, which ships with the
/// version; the Admin tab reads it from /api/admin/tests.
///
/// <para>Two things are held. The endpoint serves the file when the build has
/// one and says so when it has not, rather than inventing a result. And a
/// file that is in the repository is the kind only a green gate writes: every
/// suite the gate runs is in it, its counts are its rows, and nothing in it
/// failed, because a red gate commits nothing.</para>
/// </summary>
public class TestResultsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] Suites =
        ["vitest", "xunit-sqlite", "xunit-live", "xunit-cosmos", "browser-sqlite", "browser-cosmos"];

    [Fact]
    public async Task The_endpoint_serves_the_gate_s_results_or_says_there_are_none()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/tests");
        string body = await response.Content.ReadAsStringAsync();

        if (File.Exists(Repo.DataFile("test-results.json")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var served = JsonDocument.Parse(body);
            Assert.True(served.RootElement.GetProperty("suites").GetArrayLength() > 0, "the served file should list its suites");
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("No test results", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_shipped_results_file_is_one_only_a_green_gate_writes()
    {
        string path = Repo.DataFile("test-results.json");
        if (!File.Exists(path))
        {
            return;
        }

        var wrong = new List<string>();
        using var results = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = results.RootElement;
        if (!Regex.IsMatch(root.GetProperty("version").GetString() ?? "", @"^1\.0\.0\.\d+$"))
        {
            wrong.Add("version should be 1.0.0.N, the changelog's number for the build the gate tested");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement suite in root.GetProperty("suites").EnumerateArray())
        {
            string id = suite.GetProperty("id").GetString()!;
            seen.Add(id);
            int passed = suite.GetProperty("passed").GetInt32();
            int failed = suite.GetProperty("failed").GetInt32();
            int skipped = suite.GetProperty("skipped").GetInt32();
            var rows = suite.GetProperty("tests").EnumerateArray().ToList();
            if (passed + failed + skipped != rows.Count)
            {
                wrong.Add($"{id}: {passed} passed, {failed} failed and {skipped} skipped, and {rows.Count} rows");
            }
            if (failed != 0 || skipped != 0)
            {
                wrong.Add($"{id}: {failed} failed and {skipped} skipped; a red gate commits nothing and the house forbids a skipped test");
            }
            foreach (JsonElement row in rows)
            {
                if (row.GetArrayLength() != 4 || row[2].GetString() is not ("p" or "f" or "s") || row[3].GetInt32() < 0)
                {
                    wrong.Add($"{id}: a row should be [group, name, p f or s, milliseconds]: {row}");
                    break;
                }
            }
        }
        foreach (string missing in Suites.Where(id => !seen.Contains(id)))
        {
            wrong.Add($"{missing} is a suite the gate runs and the file does not have it");
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
}
