using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>
/// Checks how a search query matches a file or folder name. A query with * or ? is a wildcard
/// pattern that must match the whole name; any other query matches anywhere in the name. Case is
/// ignored, regular expression symbols in a query are plain text, and the query is trimmed. An
/// empty query matches nothing, so a blank search returns no results instead of the whole tree.
/// </summary>
public sealed class NamePatternTests
{
    [Theory]
    [InlineData("*.md", "README.md", true)]
    [InlineData("*.md", "readme.MD", true)]
    [InlineData("*.md", "readme.md.bak", false)]
    [InlineData("report?", "report1", true)]
    [InlineData("report?", "report12", false)]
    [InlineData("q?-*.csv", "Q1-sales.csv", true)]
    public void A_wildcard_query_is_a_glob_over_the_whole_name(string query, string name, bool expected)
    {
        Assert.Equal(expected, new NamePattern(query).Matches(name));
    }

    [Theory]
    [InlineData("invoice", "2026-Invoice-March.pdf", true)]
    [InlineData("invoice", "receipt.pdf", false)]
    [InlineData(".md", "notes.md", true)]
    public void A_plain_query_is_a_substring(string query, string name, bool expected)
    {
        Assert.Equal(expected, new NamePattern(query).Matches(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_query_matches_nothing(string? query)
    {
        var pattern = new NamePattern(query);
        Assert.True(pattern.IsEmpty);
        Assert.False(pattern.Matches("anything"));
    }

    [Fact]
    public void The_query_is_echoed_trimmed()
    {
        Assert.Equal("*.md", new NamePattern("  *.md ").Query);
    }

    [Fact]
    public void Regex_characters_in_a_query_are_literal()
    {
        Assert.True(new NamePattern("a+b").Matches("a+b.txt"));
        Assert.False(new NamePattern("a+b").Matches("aab.txt"));
        Assert.True(new NamePattern("(x)*").Matches("(x)1"));
    }
}
