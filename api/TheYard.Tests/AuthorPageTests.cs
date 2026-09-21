using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The Author page's two promises, held by the gate (ADR: The sidebar, the
/// addendum on the author's section). The page is about a private person's
/// life on a public site, so what it must never say and what its photographs
/// must never carry are rules, and a rule that lives in the memory of the
/// session that wrote the page is gone by the next edit.
///
/// <para>The first fact holds the words: no address, no phone number, no email
/// address, nothing dated and nothing that names a private person. The second
/// holds the pictures: no metadata in any file, every width the page offers a
/// real file of that width, an alt text on each, the one frame, and the blocks
/// alternating by their order and by nothing else.</para>
/// </summary>
public class AuthorPageTests
{
    private static string Root => Repo.Root();

    private static string Words() => File.ReadAllText(Path.Combine(Root, "docs", "AUTHOR.md"));

    private static string PhotoList() =>
        File.ReadAllText(Path.Combine(Root, "src", "lib", "authorPhotos.json"));

    private static string Cuts() =>
        Path.Combine(Root, "api", "TheYard.Api", "wwwroot", "images", "author");

    // #region privacy
    /// <summary>What the page must never say, as a pattern and the reason for it.</summary>
    private static readonly (string Pattern, string Reason)[] Forbidden =
    [
        (@"@", "no email address of any kind: the resume already shows the one that is public, and a page is a harvesting target"),
        (@"\(?\b\d{3}\)?[ .-]\d{3}[ .-]\d{4}\b", "no phone number"),
        (@"\b\d{1,5}\s+(\w+\s+){1,3}(street|st|avenue|ave|road|rd|drive|dr|lane|ln|court|ct|boulevard|blvd|way|place|pl|circle|cir|trail|terrace)\b\.?", "no street address: nothing on this page places his home"),
        (@"\b(my|our) (house|home|street|neighbou?rhood|subdivision) (is|was|sits)\b", "nothing that places his home, which the email this page grew from did"),
        (@"\b\d{5}(-\d{4})?\b", "no ZIP code"),
        (@"\bwedding\b|\bfianc|\bengaged\b", "no wedding and no engagement: both were true on one date and are stale on every other"),
        (@"\b(i'm|i am|aged?)\s+\d{2}\b|\b\d{2}[- ]years?[- ]old\b", "no age: it goes stale every year and adds nothing"),
        (@"\.net 8\b|\bstart date\b", "nothing from the one employer's email the page's material came from"),
    ];

    /// <summary>
    /// The exact words that must not appear, held as salted SHA-256 digests and
    /// not as text: they are a private person's first name, a former manager's
    /// first name and a date, and this file is as public as the page. Writing
    /// them here to keep them off the page would publish them here. Every word
    /// and every pair of neighbouring words on the page is digested the same
    /// way and looked for in this list.
    /// </summary>
    private static readonly (string Digest, string Reason)[] ForbiddenWords =
    [
        ("1db3eaa0e3c1f7181c319937ef17bb2ceb2e61f5690ea5af5b7902a146acc0dc", "a family member's first name: he is 'my father-in-law' and nothing more"),
        ("bb219b4bc775567863b6aa3cbe18dba58af56e18c64686d7afde2d29acd87f7e", "a former manager's first name: nobody from a former employer is on this page"),
        ("4d3ad6d6b72063611c7ce3c8427c4c48c100fc82881d1679aa0267042f1d7b66", "the wedding date, as the email wrote it"),
        ("6fbbbc97e1d004120cd2ea8ead8ca6ea118a7074b823aa856c9206f49eec0275", "the wedding date, without the ordinal"),
        ("edf6b12b4cccfdcebbb0c4b776d7c33b7c3e03c8205c9adac9e370df068c28d8", "the wedding date, abbreviated"),
        ("3d4ced163ae6fab691003bf464acbfa98eaf4cccf690ed90d72589986610f403", "the wedding date, abbreviated and without the ordinal"),
        ("305473cf2a7bfd25a6c0657b6e649ad5523207a184da1bdeb35eab5b9d1d261b", "the wedding date, spelled out"),
    ];

    private const string Salt = "theyard-author:";

    private static string Digest(string words) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + words)));

    /// <summary>Links and image addresses say nothing a reader reads, and a
    /// repository path is full of digits and slashes that are not a phone
    /// number; what is held is the prose, the link text, the alt text and the
    /// captions.</summary>
    private static string Read(string markdown) =>
        Regex.Replace(markdown, @"\]\((\S+?)(\s+""([^""]*)"")?\)", match => "] " + match.Groups[3].Value + " ");

    [Fact]
    public void The_Author_page_says_nothing_that_places_his_home_dates_his_life_or_names_a_private_person()
    {
        var wrong = new List<string>();
        foreach (string source in new[] { Read(Words()), PhotoList() })
        {
            string text = source.ToLowerInvariant();
            foreach ((string pattern, string reason) in Forbidden)
            {
                Match hit = Regex.Match(text, pattern);
                if (hit.Success)
                {
                    wrong.Add($"\"{hit.Value.Trim()}\" is on the Author page. The rule: {reason}");
                }
            }

            string[] words = Regex.Split(text, @"[^a-z0-9]+").Where(word => word.Length > 0).ToArray();
            for (int index = 0; index < words.Length; index++)
            {
                var candidates = new List<string> { words[index] };
                if (index + 1 < words.Length)
                {
                    candidates.Add(words[index] + " " + words[index + 1]);
                }

                foreach (string candidate in candidates)
                {
                    string digest = Digest(candidate);
                    foreach ((string forbidden, string reason) in ForbiddenWords)
                    {
                        if (digest == forbidden)
                        {
                            wrong.Add($"word {index + 1} of the Author page is one it must not hold. The rule: {reason}");
                        }
                    }
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Theory]
    [InlineData("Write to me at steve@example.com.")]
    [InlineData("Call 212-555-0142 any time.")]
    [InlineData("We live at 12 Example Creek Drive.")]
    [InlineData("Our house is on the corner by the park.")]
    [InlineData("I'm 52 and happy.")]
    [InlineData("We are planning the wedding.")]
    public void The_privacy_rule_catches_a_line_that_breaks_it(string line)
    {
        string text = line.ToLowerInvariant();
        Assert.Contains(Forbidden, rule => Regex.IsMatch(text, rule.Pattern));
    }
    // #endregion privacy

    // #region pictures
    private sealed record Photo(string Name, int[] Widths, int Width, int Height, string? NarrowBecause);

    private static List<Photo> Photos()
    {
        var photos = new List<Photo>();
        using var list = JsonDocument.Parse(PhotoList());
        foreach (JsonElement entry in list.RootElement.EnumerateArray())
        {
            int[] widths = entry.GetProperty("widths").EnumerateArray().Select(width => width.GetInt32()).ToArray();
            string? reason = entry.TryGetProperty("narrowBecause", out JsonElement because) ? because.GetString() : null;
            photos.Add(new Photo(
                entry.GetProperty("name").GetString()!,
                widths,
                entry.GetProperty("width").GetInt32(),
                entry.GetProperty("height").GetInt32(),
                reason));
            if (entry.TryGetProperty("phone", out JsonElement phone))
            {
                // A phone's tighter cut is a file like any other; it is drawn at a phone's width, so 960 is its floor.
                int[] tight = phone.GetProperty("widths").EnumerateArray().Select(width => width.GetInt32()).ToArray();
                photos.Add(new Photo(phone.GetProperty("name").GetString()!, tight, tight.Max(), 0, "a phone's cut, drawn at a phone's width"));
            }
        }

        return photos;
    }

    /// <summary>The metadata blocks a JPEG holds, by name, and its real width.
    /// A JPEG is a run of segments, each a marker and a length, until the scan
    /// starts; the ones that carry a camera, a place, a date or an author are
    /// APP1 (EXIF and XMP), APP11 (a C2PA stamp) and APP13 (IPTC).</summary>
    private static (List<string> Blocks, int Width) ReadJpeg(byte[] file)
    {
        var blocks = new List<string>();
        int width = 0;
        int at = 2;
        while (at + 4 <= file.Length && file[at] == 0xFF)
        {
            byte marker = file[at + 1];
            if (marker == 0xDA)
            {
                break;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at + 2));
            if (marker == 0xE1)
            {
                blocks.Add("APP1 (EXIF or XMP)");
            }
            else if (marker == 0xEB)
            {
                blocks.Add("APP11 (a C2PA stamp)");
            }
            else if (marker == 0xED)
            {
                blocks.Add("APP13 (IPTC)");
            }
            else if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                width = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at + 7));
            }

            at += 2 + length;
        }

        return (blocks, width);
    }

    /// <summary>The same for a WebP, which is a RIFF file of named chunks: EXIF
    /// and XMP are chunks of their own, and the width is in whichever of the
    /// three image headers the file has.</summary>
    private static (List<string> Blocks, int Width) ReadWebp(byte[] file)
    {
        var blocks = new List<string>();
        int width = 0;
        int at = 12;
        while (at + 8 <= file.Length)
        {
            string chunk = Encoding.ASCII.GetString(file, at, 4);
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 4));
            int body = at + 8;
            if (chunk is "EXIF" or "XMP ")
            {
                blocks.Add(chunk.Trim());
            }
            else if (chunk == "VP8X")
            {
                width = 1 + (file[body + 4] | (file[body + 5] << 8) | (file[body + 6] << 16));
            }
            else if (chunk == "VP8 " && width == 0)
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(body + 6)) & 0x3FFF;
            }
            else if (chunk == "VP8L" && width == 0)
            {
                width = 1 + (int)(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(body + 1)) & 0x3FFF);
            }

            at = body + length + (length & 1);
        }

        return (blocks, width);
    }

    [Fact]
    public void Every_photograph_the_Author_page_serves_is_a_clean_file_of_the_width_it_claims()
    {
        var wrong = new List<string>();
        List<Photo> photos = Photos();
        Assert.True(photos.Count > 0, "src/lib/authorPhotos.json should list the page's photographs");

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (Photo photo in photos)
        {
            if (photo.Widths.Max() < 1920 && string.IsNullOrWhiteSpace(photo.NarrowBecause))
            {
                wrong.Add($"{photo.Name}: its largest cut is {photo.Widths.Max()} wide. The pictures are HD: cut it at 1920 or wider, or say in narrowBecause why this one cannot be");
            }

            if (photo.Widths.Min() > 480)
            {
                wrong.Add($"{photo.Name}: its smallest cut is {photo.Widths.Min()} wide, and a phone should not have to take more than 480");
            }

            if (photo.Width != photo.Widths.Max())
            {
                wrong.Add($"{photo.Name}: the list says the picture is {photo.Width} wide and its largest cut is {photo.Widths.Max()}; the box the page reserves is the largest cut's");
            }

            foreach (int width in photo.Widths)
            {
                foreach (string format in new[] { "webp", "jpg" })
                {
                    string name = $"{photo.Name}-{width}.{format}";
                    expected.Add(name);
                    string path = Path.Combine(Cuts(), name);
                    if (!File.Exists(path))
                    {
                        wrong.Add($"{name} is offered in a srcset and is not a file; run npm run images:author");
                        continue;
                    }

                    byte[] file = File.ReadAllBytes(path);
                    (List<string> blocks, int real) = format == "jpg" ? ReadJpeg(file) : ReadWebp(file);
                    if (blocks.Count > 0)
                    {
                        wrong.Add($"{name} holds {string.Join(", ", blocks)}. A photograph knows where and when it was taken and this page must not: cut it with scripts/author_photos.mjs, which strips it");
                    }

                    if (real != width)
                    {
                        wrong.Add($"{name} is really {real} wide. A width in a srcset is the file's real width or a lie the browser acts on");
                    }
                }
            }
        }

        // And nothing else is in the folder: an original that wandered in is served to anybody who guesses its name.
        if (Directory.Exists(Cuts()))
        {
            foreach (string path in Directory.GetFiles(Cuts()))
            {
                if (!expected.Contains(Path.GetFileName(path)))
                {
                    wrong.Add($"{Path.GetFileName(path)} is in the Author page's folder and not in its list. The originals never enter the repository");
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_photograph_has_words_for_it_wears_the_one_frame_and_the_blocks_alternate_by_their_order()
    {
        var wrong = new List<string>();

        // Every image the document names is one of the page's photographs, with a real alt text.
        var names = Photos().Select(photo => photo.Name).ToHashSet(StringComparer.Ordinal);
        MatchCollection images = Regex.Matches(Words(), @"!\[([^\]]*)\]\(([^)\s]+)");
        Assert.True(images.Count > 0, "docs/AUTHOR.md should show at least one photograph");
        foreach (Match image in images)
        {
            string alt = image.Groups[1].Value.Trim();
            string address = image.Groups[2].Value;
            Match stem = Regex.Match(address, @"/images/author/([a-z0-9-]+?)-\d+\.jpg$");
            if (!stem.Success || !names.Contains(stem.Groups[1].Value))
            {
                wrong.Add($"{address} is not one of the photographs in src/lib/authorPhotos.json, so the page would leave it out");
            }

            if (alt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 4)
            {
                wrong.Add($"the alt text \"{alt}\" does not describe its picture: say what is in it, in a sentence's worth of words");
            }
        }

        // Every photograph in the list is on the page, so none is cut and served to nobody, and the
        // credit a photograph carries is on the page while it is.
        var shown = images.Select(image => Regex.Match(image.Groups[2].Value, @"/images/author/([a-z0-9-]+?)-\d+\.jpg$").Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        using (var list = JsonDocument.Parse(PhotoList()))
        {
            foreach (JsonElement entry in list.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("name").GetString()!;
                if (!shown.Contains(name))
                {
                    wrong.Add($"{name} is in src/lib/authorPhotos.json and docs/AUTHOR.md does not show it");
                }
                if (entry.TryGetProperty("credit", out JsonElement credit) && !Words().Contains(credit.GetString()!, StringComparison.Ordinal))
                {
                    wrong.Add($"{name} is on the page and its credit, \"{credit.GetString()}\", is not");
                }
                // A phone is never offered a file wider than 960 (PHONE_WIDEST in src/lib/authorPhotos.ts, which
                // offers a photograph with no tighter crop its own cuts up to that width).
                if (entry.TryGetProperty("phone", out JsonElement phone)
                    && phone.GetProperty("widths").EnumerateArray().Any(width => width.GetInt32() > 960))
                {
                    wrong.Add($"{name}: its phone cut offers a file wider than 960");
                }
            }
        }
        if (!Regex.IsMatch(File.ReadAllText(Path.Combine(Root, "src", "lib", "authorPhotos.ts")), @"export const PHONE_WIDEST = 960;"))
        {
            wrong.Add("PHONE_WIDEST in src/lib/authorPhotos.ts is not 960: a phone is never offered a file wider than that");
        }
        // One frame: the markup gives every photograph the class, and the class takes the frame's two tokens.
        string markup = File.ReadAllText(Path.Combine(Root, "src", "lib", "authorPhotos.ts"));
        string sheet = File.ReadAllText(Path.Combine(Root, "src", "components", "DocsMenu.module.css"));
        if (!markup.Contains("<img class=\"author-frame\"", StringComparison.Ordinal))
        {
            wrong.Add("photoFigure no longer gives its image the author-frame class: every photograph wears the one frame");
        }

        // Every rule that styles the frame, because a second one (the shape two photographs share side by side) may sit beside the first.
        string[] frames = Regex.Matches(sheet, @"\.author-frame\)\s*\{([^}]*)\}").Select(rule => rule.Groups[1].Value).ToArray();
        if (!frames.Any(rule => rule.Contains("var(--frame-photo-border)", StringComparison.Ordinal)
            && rule.Contains("var(--frame-photo-shadow)", StringComparison.Ordinal)))
        {
            wrong.Add("the author-frame rule should take its border and its shadow from --frame-photo-border and --frame-photo-shadow, so every photograph changes together");
        }

        if (frames.Any(rule => Regex.IsMatch(rule, @"opacity|(?<![a-z-])filter")))
        {
            wrong.Add("a photograph is fully solid: no opacity and no filter on the frame");
        }

        // Alternation: gold on the odd blocks, teal on the even ones, by order, and no block carries a colour of its own.
        if (!Regex.IsMatch(sheet, @"\.author-block:nth-of-type\(odd\)\)\s*\{\s*border-top-color:\s*var\(--color-gold\);")
            || !Regex.IsMatch(sheet, @"\.author-block:nth-of-type\(even\)\)\s*\{\s*border-top-color:\s*var\(--color-accent\);"))
        {
            wrong.Add("the headed blocks alternate gold then teal by their order: .author-block:nth-of-type(odd) is --color-gold and :nth-of-type(even) is --color-accent");
        }

        string layout = File.ReadAllText(Path.Combine(Root, "src", "lib", "author.ts"));
        if (Regex.IsMatch(layout, @"author-block-(gold|teal|odd|even)"))
        {
            wrong.Add("src/lib/author.ts gives a block a colour class. The alternation comes from the order, so an edit to the document cannot break it");
        }

        if (Regex.IsMatch(sheet, @"\.author-[a-z-]+[^{]*\{[^}]*var\(--color-(success|warning|danger)"))
        {
            wrong.Add("an Author page rule wears a status colour. Brand colours only: a border is never a state");
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion pictures
}
