using System.Text.Json;

namespace TheBlock.Tests;

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
        Path.Combine(JsonFileSourceTests.RepoRoot(), "api", "TheBlock.Api", "wwwroot", "images");

    // #region photo-sizes
    [Fact]
    public void Every_photo_in_the_manifest_has_a_card_sized_copy_beside_it()
    {
        string manifestPath = Path.Combine(
            JsonFileSourceTests.RepoRoot(), "api", "TheBlock.Api", "photo-manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        var missing = new List<string>();
        int counted = 0;
        foreach (var entry in manifest.RootElement.EnumerateArray())
        {
            string file = entry.GetProperty("file").GetString()!;
            counted++;
            string copy = Path.Combine(Images(), file.Replace(".jpg", "-480.jpg", StringComparison.Ordinal));
            if (!File.Exists(copy))
            {
                missing.Add(Path.GetFileName(copy));
            }
        }

        Assert.True(counted > 0, "the manifest should not be empty");
        Assert.True(
            missing.Count == 0,
            $"{missing.Count} of {counted} photos have no card-sized copy; run npm run images:resize: "
            + string.Join(", ", missing.Take(5)));
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
