using System.Reflection;
using System.Text.RegularExpressions;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// What this project says about itself to a reader who has not opened it, and
/// to a reader that is not a person (ADR: The public face).
///
/// Two kinds of claim are checked here. Counts, because a number in prose goes
/// stale silently and a stale number is the cheapest possible way to look
/// careless: the README said twenty-nine decision records for sixteen records
/// longer than it was true. And the head of the page, because a crawler, an
/// unfurler and an applicant tracking system read that and nothing else.
/// </summary>
public class PublicFaceTests
{
    // #region counts
    /// <summary>How many records the site serves, which is the only number any
    /// of these claims is allowed to be.</summary>
    private static int Records() =>
        DocsCatalog.Files.Keys.Count(slug => slug.StartsWith("adr-", StringComparison.Ordinal));

    /// <summary>
    /// The documents that describe this project as it is now. Records and the
    /// changelog are left out on purpose: both are dated, both quote counts
    /// that were true on the day they were written, and a document that says
    /// what it says forever is the whole point of a decision record.
    /// </summary>
    private static IEnumerable<string> LivingDocuments() =>
        DocsCatalog.Files
            .Where(entry => !entry.Key.StartsWith("adr-", StringComparison.Ordinal))
            .Where(entry => entry.Key != "changelog")
            .Select(entry => entry.Value);

    [Fact]
    public void Every_living_document_counts_the_records_correctly()
    {
        string root = Repo.Root();
        int records = Records();
        var wrong = new List<string>();
        int counted = 0;

        foreach (string relative in LivingDocuments())
        {
            // Whitespace collapsed first. This test read the file as written
            // until 1.0.0.73, and an editor had wrapped one of the README's two
            // claims so that a newline fell between the number and the noun.
            // The check never saw that claim, and passed the whole time on the
            // other one.
            string document = Regex.Replace(
                File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))),
                @"\s+",
                " ");

            foreach (Match match in Regex.Matches(document, @"([A-Za-z][a-z]+(?:-[a-z]+)?) decision record"))
            {
                int? claimed = Words.Value(match.Groups[1].Value);
                if (claimed is null)
                {
                    // "the decision records" and "and decision records" are
                    // prose, not claims.
                    continue;
                }

                counted++;
                if (claimed != records)
                {
                    wrong.Add($"{relative} says '{match.Groups[1].Value} decision records' and there are {records}");
                }
            }
        }

        // The README states it twice and How this was built once. A scan that
        // reads nothing passes, which is the failure this test exists to catch
        // in the documents, so it is worth catching here too.
        Assert.True(counted >= 3, $"only {counted} counted claims were found across the living documents");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion counts

    /// <summary>
    /// The README states how many xUnit tests there are, and until now that was
    /// a number somebody typed. It said 236 while there were 312, which is the
    /// same failure the record count had and was found the same way: by reading
    /// the document rather than by any check.
    ///
    /// <para>It is countable, so it is counted. Every test method here is a
    /// Fact or a Theory whose rows are InlineData, and nothing uses MemberData
    /// or ClassData, so the runner's total is the number of Facts plus the
    /// number of InlineData rows. If that stops being true this test will drift
    /// from the runner and say so by failing.</para>
    /// </summary>
    [Fact]
    public void The_test_count_in_the_readme_is_the_test_count()
    {
        int counted = typeof(PublicFaceTests).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Sum(method =>
            {
                int rows = method.GetCustomAttributes<InlineDataAttribute>().Count();
                if (rows > 0)
                {
                    return rows;
                }

                return method.GetCustomAttribute<FactAttribute>() is null ? 0 : 1;
            });

        string readme = File.ReadAllText(Path.Combine(Repo.Root(), "README.md"));
        var claim = Regex.Match(readme, @"\((\d+) xUnit tests");

        Assert.True(claim.Success, "the README should say how many xUnit tests there are");
        Assert.True(
            int.Parse(claim.Groups[1].Value) == counted,
            $"the README says {claim.Groups[1].Value} xUnit tests and there are {counted}");
    }

    [Fact]
    public void The_preview_card_counts_the_same_records_the_readme_does()
    {
        // The card is generated, and its numbers are read from the repository at
        // generation time, which is the point of generating it. That only helps
        // if somebody regenerates it: adding this record is exactly the change
        // that makes an unregenerated card wrong, so the count is asserted here
        // rather than trusted.
        string card = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "images", "og.svg"));
        int records = Records();

        var claim = Regex.Match(card, @">(\d+)</text>\s*<text[^>]*>decision records<");
        Assert.True(claim.Success, "the preview card should state a record count next to the words 'decision records'");
        Assert.Equal(records, int.Parse(claim.Groups[1].Value));

        // And the picture an unfurler actually fetches exists beside it. One
        // command writes both, for the reason in docs/images/og.mjs: when they
        // were two commands they drifted on the first change that moved a count.
        var png = new FileInfo(Path.Combine(Repo.Root(), "public", "og.png"));
        Assert.True(png.Exists, "public/og.png should be generated alongside the drawing");
        Assert.True(png.Length > 10_000, "a card that small has not rendered its text");
    }

    // #region head
    [Theory]
    // The description is what a search result and a recruiter's parser show.
    [InlineData("name=\"description\"")]
    // Absolute, because a relative og:image is silently dropped by most unfurlers.
    [InlineData("content=\"https://theyard.stevenstout.biz/og.png\"")]
    [InlineData("property=\"og:title\"")]
    [InlineData("property=\"og:description\"")]
    [InlineData("property=\"og:image\"")]
    [InlineData("name=\"twitter:card\"")]
    [InlineData("rel=\"canonical\"")]
    [InlineData("application/ld+json")]
    public void The_page_head_says_what_this_is(string expected) =>
        Assert.Contains(expected, File.ReadAllText(Path.Combine(Repo.Root(), "index.html")), StringComparison.Ordinal);

    [Fact]
    public void The_description_fits_where_it_will_be_shown()
    {
        string head = File.ReadAllText(Path.Combine(Repo.Root(), "index.html"));
        var description = Regex.Match(head, @"name=""description""\s*\n?\s*content=""([^""]+)""");

        Assert.True(description.Success, "index.html should carry a meta description");
        // Search results truncate near 160 characters. A description that is cut
        // off mid-sentence reads worse than a shorter one that ends.
        Assert.InRange(description.Groups[1].Value.Length, 50, 160);
    }

    [Fact]
    public void The_crawler_files_are_where_a_crawler_looks_for_them()
    {
        string root = Repo.Root();
        // public/ is copied verbatim into the built site, so these land at the
        // root of the domain, which is the only place either one is read from.
        string robots = File.ReadAllText(Path.Combine(root, "public", "robots.txt"));
        string sitemap = File.ReadAllText(Path.Combine(root, "public", "sitemap.xml"));

        Assert.Contains("Sitemap: https://theyard.stevenstout.biz/sitemap.xml", robots, StringComparison.Ordinal);
        // The endpoint that throws on purpose is not for crawlers: hitting it
        // makes real 500s and real Application Insights exceptions for nothing.
        Assert.Contains("Disallow: /api/admin/selftest/", robots, StringComparison.Ordinal);
        Assert.Contains("<loc>https://theyard.stevenstout.biz/</loc>", sitemap, StringComparison.Ordinal);
        Assert.DoesNotContain("selftest", sitemap, StringComparison.Ordinal);
    }
    // #endregion head

    /// <summary>Number words, as far as this README needs to count.</summary>
    private static class Words
    {
        private static readonly string[] Units =
        [
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
            "seventeen", "eighteen", "nineteen",
        ];

        private static readonly string[] Tens =
        [
            "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
        ];

        public static int? Value(string word)
        {
            string lower = word.ToLowerInvariant();
            int index = Array.IndexOf(Units, lower);
            if (index >= 0)
            {
                return index;
            }

            string[] parts = lower.Split('-');
            int tens = Array.IndexOf(Tens, parts[0]);
            if (tens < 2)
            {
                return null;
            }

            if (parts.Length == 1)
            {
                return tens * 10;
            }

            int units = Array.IndexOf(Units, parts[1]);
            return units is > 0 and < 10 ? (tens * 10) + units : null;
        }
    }
}
