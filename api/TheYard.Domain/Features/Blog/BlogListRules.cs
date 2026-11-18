// How the list is paged and ordered (ADR: Every LinkedIn post is also a blog post, the addendum on the read model):
// ten cards a page, newest first; most read first when asked; best match first while searching; every tie to the
// newer post, then the higher id, so the order is total and a page boundary never repeats or skips a post. A page
// is found by keyset: the cursor is the last card's place in the order, and the next page is what comes after it.
// A month is the UTC calendar month a post went out in, the same on every store.
using System.Globalization;

namespace TheYard.Domain.Features.Blog;

/// <summary>A card's place in one of the three orders: the order's letter, its rank (reads or score, 0 for newest), the instant's ticks and the id.</summary>
/// <param name="Order">n newest, r most read, s best match.</param>
/// <param name="Rank">The reads or the score in tenths; 0 for newest first.</param>
/// <param name="Ticks">The post's published instant in UTC ticks.</param>
/// <param name="Id">The post's id.</param>
public sealed record BlogCursor(char Order, int Rank, long Ticks, int Id);

/// <summary>The list's page size, its orders, its cursor and its months.</summary>
public static class BlogListRules
{
    /// <summary>Cards a page.</summary>
    public const int PageSize = 10;

    /// <summary>The most related posts a post's page shows: other posts that share a topic with it, newest first.</summary>
    public const int Related = 3;

    private static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    /// <summary>Which order a filter asks for: s while searching unless most read was chosen, r for most read, n otherwise.</summary>
    /// <param name="sort">The filter's sort.</param>
    /// <param name="searching">Whether the filter has typed words.</param>
    public static char OrderOf(string? sort, bool searching) => sort == "read" ? 'r' : searching ? 's' : 'n';

    /// <summary>A card's place in an order, as its cursor.</summary>
    /// <param name="order">n, r or s.</param>
    /// <param name="rank">The reads or the score; ignored for n.</param>
    /// <param name="published">When the post went out.</param>
    /// <param name="id">The post's id.</param>
    public static BlogCursor Place(char order, int rank, DateTimeOffset published, int id) => new(order, order == 'n' ? 0 : rank, published.UtcTicks, id);

    /// <summary>The cursor as the address carries it: the order's letter and three numbers, dot apart.</summary>
    /// <param name="cursor">The cursor.</param>
    public static string Write(BlogCursor cursor) =>
        string.Create(CultureInfo.InvariantCulture, $"{cursor.Order}.{cursor.Rank}.{cursor.Ticks}.{cursor.Id}");

    /// <summary>The cursor an address carried, or null for none, for one that is not a cursor, or for one written for another order.</summary>
    /// <param name="text">What the address carried.</param>
    /// <param name="order">The order the page is in.</param>
    public static BlogCursor? Read(string? text, char order)
    {
        var parts = (text ?? string.Empty).Split('.');
        return parts.Length == 4 && parts[0].Length == 1 && parts[0][0] == order
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int rank)
            && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
            && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                ? new BlogCursor(order, rank, ticks, id)
                : null;
    }

    /// <summary>Whether a place comes after the cursor in its order: a lower rank, then an older post, then a lower id.</summary>
    /// <param name="place">The card's place.</param>
    /// <param name="cursor">The previous page's last place.</param>
    public static bool After(BlogCursor place, BlogCursor cursor) =>
        place.Rank < cursor.Rank
        || (place.Rank == cursor.Rank && (place.Ticks < cursor.Ticks || (place.Ticks == cursor.Ticks && place.Id < cursor.Id)));

    /// <summary>The places in order, first to last.</summary>
    /// <param name="places">The places of the posts that match.</param>
    public static IEnumerable<BlogCursor> Ordered(IEnumerable<BlogCursor> places) =>
        places.OrderByDescending(p => p.Rank).ThenByDescending(p => p.Ticks).ThenByDescending(p => p.Id);

    /// <summary>The month a post went out in, as yyyy-MM in UTC.</summary>
    /// <param name="published">When it went out.</param>
    public static string MonthOf(DateTimeOffset published) =>
        published.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>A month's first instant and the next month's, in UTC, for a yyyy-MM; null for anything else.</summary>
    /// <param name="month">yyyy-MM.</param>
    public static (DateTimeOffset From, DateTimeOffset To)? MonthRange(string? month) =>
        DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var first)
            ? (new DateTimeOffset(first, TimeSpan.Zero), new DateTimeOffset(first.AddMonths(1), TimeSpan.Zero))
            : null;

    /// <summary>A year's first instant and the next year's, in UTC; null for a year outside 2000 to 2999.</summary>
    /// <param name="year">The year.</param>
    public static (DateTimeOffset From, DateTimeOffset To)? YearRange(int? year) =>
        year is >= 2000 and <= 2999 ? (new DateTimeOffset(year.Value, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(year.Value + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)) : null;

    /// <summary>A month's label: "November 2026".</summary>
    /// <param name="month">yyyy-MM.</param>
    public static string MonthLabel(string month) =>
        MonthRange(month) is { } range ? $"{MonthNames[range.From.Month - 1]} {range.From.Year.ToString(CultureInfo.InvariantCulture)}" : month;

    /// <summary>The keyword chips' order: the keywords some post has, most posts first, then by label.</summary>
    /// <param name="postsHaving">How many published posts have each keyword's key.</param>
    public static IEnumerable<BlogKeyword> KeywordChips(IReadOnlyDictionary<string, int> postsHaving) =>
        KeywordRules.Keywords.Where(k => postsHaving.GetValueOrDefault(k.Key) > 0)
            .OrderByDescending(k => postsHaving[k.Key]).ThenBy(k => k.Label, StringComparer.Ordinal);
}
