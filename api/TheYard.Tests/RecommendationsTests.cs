using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// What colleagues say comes from one file, data/recommendations.json, in one shape, and the
/// site shows the file as it is (ADR: The sidebar, the addendum on the column on the right).
/// These hold the file to the shape before a version ships, and the endpoint to the file.
/// </summary>
public class RecommendationsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string FilePath = Repo.DataFile("recommendations.json");

    private static RecommendationsReply Reply() =>
        JsonSerializer.Deserialize<RecommendationsReply>(File.ReadAllText(FilePath), RecommendationsFile.Format)!;

    [Fact]
    public void The_file_is_in_shape_and_holds_the_ten_on_his_profile()
    {
        var reply = Reply();
        Assert.Empty(RecommendationsFile.Problems(reply));
        Assert.Equal(10, reply.Recommendations.Count);
    }

    /// <summary>
    /// The shape is six fields, every entry the same, nothing extra: a seventh field on one entry
    /// is a field the pages never show, and a missing one is a hole in the column.
    /// </summary>
    [Fact]
    public void Every_entry_has_the_same_six_fields_and_nothing_else()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        string[] expected = ["name", "title", "relationship", "when", "paragraphs", "pull"];
        var wrong = new List<string>();
        foreach (var entry in document.RootElement.GetProperty("recommendations").EnumerateArray())
        {
            var keys = entry.EnumerateObject().Select(property => property.Name).ToArray();
            if (!keys.SequenceEqual(expected))
            {
                wrong.Add($"{entry.GetProperty("name").GetString()}: [{string.Join(", ", keys)}]");
            }
        }

        Assert.True(wrong.Count == 0, "entries whose fields are not the six, in order: " + string.Join("; ", wrong));
        var top = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "readme", "source", "recommendations" }, top);
    }

    /// <summary>A page quotes the file, so the file carries nothing a page should not: no address, no link in the words, no em dash.</summary>
    [Fact]
    public void The_words_carry_nothing_a_page_should_not()
    {
        foreach (var entry in Reply().Recommendations)
        {
            string all = string.Join(" ", new[] { entry.Name, entry.Title, entry.Relationship }.Concat(entry.Paragraphs));
            Assert.DoesNotContain("@", all);
            Assert.DoesNotContain("http", all);
            Assert.DoesNotContain("\u2014", all);
        }
    }

    [Fact]
    public async Task The_endpoint_answers_the_file_in_its_order_on_the_wire()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/recommendations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var served = document.RootElement.GetProperty("recommendations").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(Reply().Recommendations.Select(entry => entry.Name).ToArray(), served);
        Assert.Equal(Reply().Source, document.RootElement.GetProperty("source").GetString());
        // The wire is snake_case, as every other answer here.
        Assert.True(document.RootElement.GetProperty("recommendations")[0].TryGetProperty("paragraphs", out _));
        Assert.False(document.RootElement.TryGetProperty("Recommendations", out _));
    }

    /// <summary>The check catches the slips an edit can make, so the gate fails for the right reason and says which entry.</summary>
    [Fact]
    public void The_shape_check_names_the_entry_that_is_out_of_shape()
    {
        var good = Reply();
        var first = good.Recommendations[0];
        var broken = good with
        {
            Recommendations =
            [
                first with { When = "Smarch 2024" },
                first with { Name = "Somebody Else", Pull = "A sentence nobody wrote." },
                first with { Name = "Nobody", Paragraphs = [] },
                first with { Name = "Half Way", Pull = first.Pull.TrimEnd('.') },
                null!,
                first,
            ],
        };
        var problems = RecommendationsFile.Problems(broken);
        Assert.Contains(problems, problem => problem.StartsWith(first.Name, StringComparison.Ordinal) && problem.Contains("when", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("Somebody Else", StringComparison.Ordinal) && problem.Contains("pull", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("Nobody", StringComparison.Ordinal) && problem.Contains("paragraphs", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("Half Way", StringComparison.Ordinal) && problem.Contains("whole sentence", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("entry 5", StringComparison.Ordinal) && problem.Contains("null", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("second entry from the same person", StringComparison.Ordinal));
        Assert.Empty(RecommendationsFile.Problems(good));
        Assert.Single(RecommendationsFile.Problems(null));
    }
}
