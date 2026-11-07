// What the recommendations add up to, worked out from the file and never stated: how many there are, the years
// they span, and who wrote them by LinkedIn's own relationship line, seen from Steven's side. Every page reads these
// from here, so adding a person to the file moves the count, the span and the groups with no edit anywhere else.
// Pure: the entries' relationship lines and months in, the facts out.
using System.Globalization;

namespace TheYard.Domain.Features.Docs;

// #region recommendation-facts
/// <summary>One group of writers: who they were to Steven, in the order the pages show the groups.</summary>
/// <param name="Key">above, led, team, other or outside.</param>
/// <param name="Title">The group's heading.</param>
/// <param name="Lead">One line under the heading.</param>
/// <param name="Names">The writers in the group, in the file's order.</param>
public sealed record RecommendationGroup(string Key, string Title, string Lead, IReadOnlyList<string> Names);

/// <summary>The derived facts: the count, the first and last year in words, and the groups that hold anyone.</summary>
/// <param name="Count">How many recommendations the file holds.</param>
/// <param name="From">The earliest year a recommendation is dated, as words.</param>
/// <param name="To">The latest year, as words.</param>
/// <param name="Groups">The groups in their fixed order, an empty one left out.</param>
public sealed record RecommendationFacts(int Count, string From, string To, IReadOnlyList<RecommendationGroup> Groups)
{
    /// <summary>The five groups in the order the pages show them, with their headings and lines.</summary>
    public static readonly IReadOnlyList<(string Key, string Title, string Lead)> Order =
    [
        ("above", "People he reported to", "The view from above."),
        ("led", "People he was senior to", "The view from the people he was lead to, years apart, the same account."),
        ("team", "People on his team", "The people who sat beside him on the same team."),
        ("other", "People on other teams at the same company", "The reputation as it travelled across a building."),
        ("outside", "People from outside the company", "Partners, contractors and vendors who worked with him from the other side of the table."),
    ];

    /// <summary>The group a relationship line belongs to, read the way LinkedIn words it; null for a line no group knows.</summary>
    /// <param name="relationship">LinkedIn's line, such as "Steven was senior to Drew but did not manage Drew directly".</param>
    public static string? GroupOf(string? relationship)
    {
        string line = (relationship ?? "").Trim().ToLowerInvariant();
        if (line.Contains("managed steven", StringComparison.Ordinal) || line.Contains("steven reported directly", StringComparison.Ordinal)
            || line.Contains("steven's mentor", StringComparison.Ordinal) || line.Contains("taught steven", StringComparison.Ordinal))
        {
            return "above";
        }
        if (line.StartsWith("steven was senior to", StringComparison.Ordinal) || line.Contains("reported directly to steven", StringComparison.Ordinal)
            || line.StartsWith("steven managed", StringComparison.Ordinal) || line.Contains("steven taught", StringComparison.Ordinal)
            || (line.StartsWith("steven was", StringComparison.Ordinal) && line.Contains("mentor", StringComparison.Ordinal)))
        {
            return "led";
        }
        if (line.Contains("on the same team", StringComparison.Ordinal))
        {
            return "team";
        }
        if (line.Contains("on different teams", StringComparison.Ordinal))
        {
            return "other";
        }
        return line.Contains("different companies", StringComparison.Ordinal) || line.Contains("client", StringComparison.Ordinal) ? "outside" : null;
    }

    /// <summary>The facts of a list of entries, each its writer's name, relationship line and month in words.</summary>
    /// <param name="entries">The entries in the file's order.</param>
    public static RecommendationFacts Of(IReadOnlyList<(string Name, string Relationship, string When)> entries)
    {
        var years = entries.Select(entry => Year(entry.When)).OfType<int>().ToList();
        var groups = Order
            .Select(group => new RecommendationGroup(group.Key, group.Title, group.Lead,
                [.. entries.Where(entry => GroupOf(entry.Relationship) == group.Key).Select(entry => entry.Name)]))
            .Where(group => group.Names.Count > 0)
            .ToList();
        string Words(int? year) => year?.ToString(CultureInfo.InvariantCulture) ?? "";
        return new RecommendationFacts(entries.Count, Words(years.Count > 0 ? years.Min() : null), Words(years.Count > 0 ? years.Max() : null), groups);
    }

    /// <summary>The year at the end of "April 2024"; null when there is none.</summary>
    private static int? Year(string? when) =>
        (when ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries) is [.., var last] && int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            ? year
            : null;
}
// #endregion recommendation-facts
