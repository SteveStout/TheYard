using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// Two real days of the inbox pass's leads files (nightly/leads-2026-11-04.md and leads-2026-11-05.md, copied from the
/// share at 13:20 CST on 11/5), every row the Function wrote re-filtered: the old remote rule (the word remote in the
/// location or the title) against the new one (that word, or a location that is the country alone), both with an
/// empty tracker so only the remote rule and the band move. The numbers asserted here are the before and after the
/// nightjobs1105 DONE note quotes. The rows the digest header shifted are counted too: the file shows them with the
/// header in the Title column, and the parser now skips that line.
/// </summary>
public sealed partial class LeadsFixtureTests
{
    private static string Fixture(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "functions", "jobhunt.Tests", "Fixtures", "leads", name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        throw new InvalidOperationException("Fixtures/leads/" + name + " not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Every row of the Function's own tables (six cells, a link, a filter verdict), as the file holds them.</summary>
    private static List<(Lead Lead, string Verdict)> Rows(string text)
    {
        var rows = new List<(Lead, string)>();
        foreach (string line in text.Split('\n'))
        {
            string[] cells = line.Trim().Trim('|').Split(" | ").Select(c => c.Trim()).ToArray();
            if (cells.Length != 6 || !cells[3].StartsWith("https://", StringComparison.Ordinal))
            {
                continue;
            }
            string where = cells[2];
            int band = where.IndexOf("; band ", StringComparison.Ordinal);
            var lead = new Lead(cells[0], cells[1], band < 0 ? where : where[..band], cells[3], cells[4]) { Band = band < 0 ? null : where[(band + 7)..] };
            rows.Add((lead, cells[5]));
        }
        return rows;
    }

    [Theory]
    [InlineData("leads-2026-11-04.md", 74, 12, 9, 11, 35)]
    [InlineData("leads-2026-11-05.md", 49, 15, 7, 12, 25)]
    public void Every_row_re_filtered_passes_more_on_the_new_remote_rule(string name, int rows, int filePass, int shifted, int oldPass, int newPass)
    {
        var all = Rows(Fixture(name));
        Assert.Equal(rows, all.Count);
        Assert.Equal(filePass, all.Count(r => r.Verdict == "pass"));
        Assert.Equal(shifted, all.Count(r => r.Lead.Title.StartsWith("Your job alert for", StringComparison.Ordinal)));

        var today = new DateOnly(2026, 11, 5);
        var judged = LeadsFilter.Judge(all.Select(r => r.Lead), [], [], today);
        int before = judged.Count(j => OldRemote().IsMatch(j.Lead.Location + " " + j.Lead.Title)
            && !(BoardAlerts.TopOfBand(j.Lead.Band) is { } top && top < LeadsFilter.Floor));
        Assert.Equal(oldPass, before);
        Assert.Equal(newPass, judged.Count(j => j.Failed is null));
    }

    [Fact]
    public void The_digest_header_is_skipped_and_counted_and_the_columns_stay_in_place()
    {
        // The plain text LinkedIn sent on 11/4 07:46 UTC (Gmail 1a1a585e7a5281ed), its tracking cut: the header line has no link.
        const string text = "Your job alert for lead dotnet developer in United States\nPrincipal Engineer, New Products\nPetDesk\nUnited States\nTop applicant\nView job: https://www.linkedin.com/comm/jobs/view/4485454908/?trackingId=x\n\n---------------------------------------------------------\n\nFull Stack Developer Lead\nSupplyLogic\nUnited States\nTop applicant\nApply with resume & profile\nView job: https://www.linkedin.com/comm/jobs/view/4483987515/?trackingId=y\n\n---------------------------------------------------------\n\nSee all jobs on LinkedIn: https://www.linkedin.com/comm/jobs/search-results/?keywords=x\n";
        var (leads, unread) = LinkedInAlert.Read(text, "1a1a585e7a5281ed");
        Assert.Equal(0, unread);
        Assert.Equal(1, LinkedInAlert.DigestHeaders(text));
        Assert.Equal(new Lead("PetDesk", "Principal Engineer, New Products", "United States", "https://www.linkedin.com/jobs/view/4485454908/", "1a1a585e7a5281ed"), leads[0]);
        Assert.Equal(("SupplyLogic", "Full Stack Developer Lead", "United States"), (leads[1].Company, leads[1].Title, leads[1].Location));
        Assert.All(LeadsFilter.Judge(leads, [], [], new DateOnly(2026, 11, 5)), j => Assert.Null(j.Failed));
    }

    [Theory]
    [InlineData("United States", true)]
    [InlineData("US", true)]
    [InlineData("USA", true)]
    [InlineData("Anywhere in the US", true)]
    [InlineData("United States (Remote)", true)]
    [InlineData("Remote", true)]
    [InlineData("Michigan, United States", false)]
    [InlineData("St Louis, MO", false)]
    [InlineData("New York, NY", false)]
    public void A_location_that_is_the_country_alone_reads_as_remote(string location, bool remote) =>
        Assert.Equal(remote, LeadsFilter.IsRemote(new Lead("Acme", "Staff Engineer", location, "https://l/1/", "m")));

    [GeneratedRegex(@"\bremote\b", RegexOptions.IgnoreCase)]
    private static partial Regex OldRemote();
}
