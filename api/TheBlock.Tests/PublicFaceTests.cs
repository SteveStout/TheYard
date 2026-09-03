using System.Text.RegularExpressions;
using TheBlock.Api;

namespace TheBlock.Tests;

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
    [Fact]
    public void The_record_count_in_the_readme_is_the_record_count()
    {
        string root = Repo.Root();
        int records = DocsCatalog.Files.Keys.Count(slug => slug.StartsWith("adr-", StringComparison.Ordinal));
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));

        var claims = Regex.Matches(readme, @"([A-Za-z][a-z]+(?:-[a-z]+)?) decision record")
            .Select(match => match.Groups[1].Value)
            .Where(word => Words.Value(word) is not null)
            .ToArray();

        Assert.True(claims.Length > 0, "the README should say how many decision records there are");
        foreach (string claim in claims)
        {
            Assert.True(
                Words.Value(claim) == records,
                $"the README says '{claim} decision records' and there are {records}");
        }
    }
    // #endregion counts

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
