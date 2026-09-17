using System.Text.Json;

namespace TheYard.Tests;

/// <summary>
/// The naming the browser relies on (ADR: Responsive photos).
///
/// `VehicleImage` derives the card-sized copy's URL from the original's by
/// swapping `.jpg` for `-480.jpg`, which is cheap and correct exactly as long
/// as the file is there. A srcset candidate that 404s does not degrade: the
/// image fails. So the convention is held here rather than trusted, and a photo
/// added without running `npm run images:resize` fails the build instead of a
/// card.
/// </summary>
public class PhotoSizeTests
{
    private static string Images() =>
        Path.Combine(JsonFileSourceTests.RepoRoot(), "api", "TheYard.Api", "wwwroot", "images");

    // #region photo-sizes
    [Fact]
    public void Every_photo_in_the_manifest_has_a_card_sized_copy_beside_it()
    {
        string manifestPath = Path.Combine(
            JsonFileSourceTests.RepoRoot(), "api", "TheYard.Api", "photo-manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        var missing = new List<string>();
        int counted = 0;
        foreach (var entry in manifest.RootElement.EnumerateArray())
        {
            string file = entry.GetProperty("file").GetString()!;
            counted++;
            // The JPEG card copy, and since 1.0.0.143 the WebP pair the picture
            // element offers first (ADR: Responsive photos, addendum). All three
            // are derived names, so all three are held here.
            foreach (string suffix in new[] { "-480.jpg", "-480.webp", ".webp" })
            {
                string copy = Path.Combine(Images(), file.Replace(".jpg", suffix, StringComparison.Ordinal));
                if (!File.Exists(copy))
                {
                    missing.Add(Path.GetFileName(copy));
                }
            }
        }

        Assert.True(counted > 0, "the manifest should not be empty");
        Assert.True(
            missing.Count == 0,
            $"{missing.Count} derived copies of {counted} photos are missing; run npm run images:resize: "
            + string.Join(", ", missing.Take(5)));
    }

    /// <summary>
    /// The WebP pair is offered first because it is smaller, so that is what
    /// is held, at the margin the set actually measured: 43 per cent under the
    /// JPEG at 1280, where a dense phone screen reads, and six per cent under
    /// at 480, where mozjpeg at quality 78 was already tight. A quarter at 1280
    /// leaves room for a photograph WebP does little for without letting an
    /// encode that silently wrote JPEG-sized files pass; at 480 the honest
    /// floor is only that the set is not larger.
    /// </summary>
    [Fact]
    public void The_webp_copies_are_smaller_than_the_jpegs_they_stand_in_for()
    {
        long Bytes(string pattern, Func<string, bool> keep) =>
            Directory.GetFiles(Images(), pattern).Where(keep).Sum(path => new FileInfo(path).Length);

        long jpegLarge = Bytes("*.jpg", path => !path.EndsWith("-480.jpg", StringComparison.Ordinal));
        long jpegSmall = Bytes("*-480.jpg", _ => true);
        long webpLarge = Bytes("*.webp", path => !path.EndsWith("-480.webp", StringComparison.Ordinal));
        long webpSmall = Bytes("*-480.webp", _ => true);

        Assert.True(webpLarge > 0 && webpSmall > 0, "the WebP copies should exist; run npm run images:resize");
        Assert.True(
            webpLarge < jpegLarge * 0.75,
            $"the 1280 WebP set should be at least a quarter under the JPEG set: {webpLarge / 1024} KB against {jpegLarge / 1024} KB");
        Assert.True(
            webpSmall < jpegSmall,
            $"the 480 WebP set should not be larger than the JPEG set: {webpSmall / 1024} KB against {jpegSmall / 1024} KB");
    }

    [Fact]
    public void The_card_copies_are_much_smaller_than_the_originals()
    {
        var originals = Directory.GetFiles(Images(), "*.jpg")
            .Where(path => !path.EndsWith("-480.jpg", StringComparison.Ordinal))
            .ToList();
        var copies = Directory.GetFiles(Images(), "*-480.jpg").ToList();

        Assert.Equal(originals.Count, copies.Count);

        long originalBytes = originals.Sum(path => new FileInfo(path).Length);
        long copyBytes = copies.Sum(path => new FileInfo(path).Length);

        // Measured at 91 per cent on the set this shipped with. Asserting 70
        // leaves room for a photo that was already small without letting a
        // resize that silently did nothing pass as one that worked.
        double saved = 1 - ((double)copyBytes / originalBytes);
        Assert.True(
            saved > 0.70,
            $"the card copies should be much smaller: {originalBytes / 1024} KB of originals, "
            + $"{copyBytes / 1024} KB of copies, {saved:P0} saved");
    }
    // #endregion photo-sizes
}
