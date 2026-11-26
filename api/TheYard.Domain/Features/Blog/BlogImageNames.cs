// What a blog picture is called where it is kept (ADR-113, the addendum on where the pictures live): the first
// sixteen hex digits of the SHA-256 of its bytes and its kind, so an edited picture is a new name and a year's
// cache of the old one is never wrong. Pure: bytes in, a name out, and the check a route makes before it asks.
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TheYard.Domain.Features.Blog;

// #region blog-image-names
/// <summary>The kept name of a blog picture, and the kinds a picture may be.</summary>
public static partial class BlogImageNames
{
    /// <summary>Each kind's extension against its content type.</summary>
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["webp"] = "image/webp",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
    };

    /// <summary>The name for these bytes of this kind: "3f9a0c2b1d4e5f60.webp".</summary>
    /// <param name="bytes">The picture.</param>
    /// <param name="kind">webp, png or jpg.</param>
    public static string For(ReadOnlySpan<byte> bytes, string kind) =>
        Kinds.ContainsKey(kind)
            ? $"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..16]}.{kind}"
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "not webp, png or jpg");

    /// <summary>The width a slide's thumbnail is drawn at for the strip and the mini cards, in pixels.</summary>
    public const int ThumbWidth = 320;

    /// <summary>
    /// The widths a thumbnail is cut at: the 320 the page names, and 480 and 640 for a phone's sharper screen
    /// (scripts/blog_thumbs.mjs). A cut not kept yet is answered with its full picture under the stand-in rule.
    /// </summary>
    public static readonly IReadOnlyList<int> ThumbWidths = [ThumbWidth, 480, 640];

    /// <summary>The most bytes a thumbnail may be: a 320 px WebP of a slide is a tenth of this.</summary>
    public const int ThumbMaxBytes = 64 * 1024;

    /// <summary>Whether a name from a route is one this rule makes, a picture or its thumbnail; anything else is never looked up.</summary>
    /// <param name="name">The name asked for.</param>
    public static bool IsName(string? name) => name is not null && (Shape().IsMatch(name) || Thumb().IsMatch(name));

    /// <summary>The content type a kept name is served with; null for a name that is not one. A thumbnail is always WebP.</summary>
    /// <param name="name">A kept name.</param>
    public static string? ContentType(string name) =>
        Thumb().IsMatch(name) ? Kinds["webp"] : IsName(name) ? Kinds[name[(name.IndexOf('.', StringComparison.Ordinal) + 1)..]] : null;

    /// <summary>
    /// A picture's thumbnail beside it: its own sixteen digits, the width and webp ("3f9a0c2b1d4e5f60-320.webp"), kept
    /// in the same container under the same year's cache rule. Named for its picture rather than its own bytes, so the
    /// page can name it from the picture's address; a picture with new bytes is a new name, and so is its thumbnail.
    /// An address that is not one of the kept pictures' comes back as it was.
    /// </summary>
    /// <param name="url">A picture's served address, /blog/images/name.</param>
    public static string ThumbOf(string url)
    {
        int slash = url.LastIndexOf('/');
        string name = url[(slash + 1)..];
        return url.StartsWith("/blog/images/", StringComparison.Ordinal) && Shape().IsMatch(name)
            ? $"{url[..(slash + 1)]}{name[..16]}-{ThumbWidth}.webp"
            : url;
    }

    /// <summary>The sixteen digits of the picture a thumbnail's name stands for; null for a name that is no thumbnail.</summary>
    /// <param name="name">A name from the route.</param>
    public static string? PictureOf(string name) => Thumb().IsMatch(name) ? name[..16] : null;

    [GeneratedRegex("^[0-9a-f]{16}\\.(webp|png|jpg)$")]
    private static partial Regex Shape();

    [GeneratedRegex("^[0-9a-f]{16}-(320|480|640)\\.webp$")]
    private static partial Regex Thumb();
}
// #endregion blog-image-names
