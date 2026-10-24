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

    /// <summary>Whether a name from a route is one this rule makes; anything else is never looked up.</summary>
    /// <param name="name">The name asked for.</param>
    public static bool IsName(string? name) => name is not null && Shape().IsMatch(name);

    /// <summary>The content type a kept name is served with; null for a name that is not one.</summary>
    /// <param name="name">A kept name.</param>
    public static string? ContentType(string name) => IsName(name) ? Kinds[name[(name.IndexOf('.', StringComparison.Ordinal) + 1)..]] : null;

    [GeneratedRegex("^[0-9a-f]{16}\\.(webp|png|jpg)$")]
    private static partial Regex Shape();
}
// #endregion blog-image-names
