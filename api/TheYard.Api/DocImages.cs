using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TheYard.Api;

// #region docs-images
/// <summary>
/// The pictures a document carries. The markdown names them on GitHub's raw host so the files read
/// on GitHub as they are; read here, that meant every document's pictures came from a third host, and
/// the README on a phone was 1.4 MB, 985 KB of it a PNG rendered from an SVG this repository already
/// serves at 17 KB. So the served markdown names them here instead: the raw address becomes
/// /api/docs/images/{name}, and a PNG whose SVG source stands beside it is served as that SVG. The
/// markdown files themselves are untouched, which is what keeps them right on GitHub.
/// </summary>
public static partial class DocImages
{
    public const string RawHost = "https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/";
    public const string Route = "/api/docs/images/";

    /// <summary>A picture's name: letters, digits and hyphens, one of four extensions. Anything else is not a file read.</summary>
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*\.(png|jpg|jpeg|svg|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex NameShape();

    [GeneratedRegex(@"https://raw\.githubusercontent\.com/SteveStout/TheYard/main/docs/images/([a-z0-9][a-z0-9-]*)\.(png|jpg|jpeg|svg|webp)", RegexOptions.IgnoreCase)]
    private static partial Regex RawAddress();

    public static bool IsName(string name) => NameShape().IsMatch(name);

    public static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    /// <summary>The file a name points at, under docs/images only, or null for a name the shape refuses.</summary>
    public static string? PathOf(string repoRoot, string name) =>
        IsName(name) ? Path.Combine(repoRoot, "docs", "images", name) : null;

    /// <summary>
    /// Every raw-host picture address in a served document, pointed here; a PNG with an SVG source beside it
    /// points at the SVG. The address carries the picture's size after a hash, #1280x800, which the request
    /// never sends: the page's renderer turns it into the image's width and height, so the space is held
    /// before the picture arrives and the words under it do not move when it lands (the operator's look,
    /// where a document's lead picture pushed a paragraph off the screen after the first paint).
    /// </summary>
    public static string Rewrite(string markdown, string repoRoot) =>
        RawAddress().Replace(markdown, match =>
        {
            string stem = match.Groups[1].Value;
            string extension = match.Groups[2].Value.ToLowerInvariant();
            bool drawn = extension == "png" && File.Exists(Path.Combine(repoRoot, "docs", "images", stem + ".svg"));
            string name = stem + (drawn ? ".svg" : "." + extension);
            (int Width, int Height)? size = SizeOf(Path.Combine(repoRoot, "docs", "images", name));
            return Route + name + (size is { } known ? $"#{known.Width}x{known.Height}" : "");
        });

    // #region picture-size
    /// <summary>
    /// A picture's width and height in pixels, read from its own header (a PNG's IHDR, a JPEG's frame marker,
    /// an SVG's width and height or its view box), or null for a file that is not there or not read here.
    /// </summary>
    public static (int Width, int Height)? SizeOf(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        string extension = Path.GetExtension(path).ToLowerInvariant();
        // #region header-only
        // Only the header is read. Reading the whole file to look at its first bytes put every
        // photograph of a document, up to 941 KB, on the large object heap on every page that
        // asked its size (ADR: The store does the work, rule d): a PNG's size is in its first 24
        // bytes, a JPEG's in a frame marker the walk below seeks to, and an SVG's in its opening
        // tag, which is in the first 32,768 characters of every drawing here.
        using var stream = File.OpenRead(path);
        if (extension == ".svg")
        {
            char[] buffer = ArrayPool<char>.Shared.Rent(SvgHeadChars);
            try
            {
                using var reader = new StreamReader(stream);
                int read = reader.ReadBlock(buffer, 0, SvgHeadChars);
                return SvgSize(new string(buffer, 0, read));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }
        Span<byte> head = stackalloc byte[24];
        if (extension == ".png")
        {
            return stream.ReadAtLeast(head, 24, throwOnEndOfStream: false) < 24
                ? null
                : ((head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19],
                    (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23]);
        }
        if (extension is ".jpg" or ".jpeg")
        {
            Span<byte> marker = head[..9];
            long at = 2;
            while (at + 9 < stream.Length)
            {
                stream.Position = at;
                stream.ReadExactly(marker);
                if (marker[0] != 0xFF)
                {
                    at++;
                    continue;
                }
                if (marker[1] is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    return ((marker[7] << 8) | marker[8], (marker[5] << 8) | marker[6]);
                }
                at += 2 + ((marker[2] << 8) | marker[3]);
            }
        }
        return null;
        // #endregion header-only
    }

    /// <summary>How many characters of a drawing are read to find its opening tag: 64 KB of string, under the large object heap's 85,000 bytes.</summary>
    private const int SvgHeadChars = 32_768;

    /// <summary>An SVG's width and height, or its view box, from the opening tag in its first characters.</summary>
    private static (int Width, int Height)? SvgSize(string head)
    {
        int open = head.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        int close = open < 0 ? -1 : head.IndexOf('>', open);
        if (close < 0)
        {
            return null;
        }
        string tag = head[open..close];
        Match width = SvgWidth().Match(tag);
        Match height = SvgHeight().Match(tag);
        if (width.Success && height.Success)
        {
            return (Pixels(width.Groups[1].Value), Pixels(height.Groups[1].Value));
        }
        Match box = SvgViewBox().Match(tag);
        return box.Success ? (Pixels(box.Groups[1].Value), Pixels(box.Groups[2].Value)) : null;
    }

    private static int Pixels(string value) =>
        (int)Math.Round(double.Parse(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"\swidth=""(\d+(?:\.\d+)?)(?:px)?""")]
    private static partial Regex SvgWidth();

    [GeneratedRegex(@"\sheight=""(\d+(?:\.\d+)?)(?:px)?""")]
    private static partial Regex SvgHeight();

    [GeneratedRegex(@"viewBox=""[\d.\-]+[ ,]+[\d.\-]+[ ,]+(\d+(?:\.\d+)?)[ ,]+(\d+(?:\.\d+)?)""")]
    private static partial Regex SvgViewBox();
    // #endregion picture-size
}
// #endregion docs-images
