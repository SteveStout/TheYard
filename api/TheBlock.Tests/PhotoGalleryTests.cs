using TheBlock.Domain;
using TheBlock.Data;

namespace TheBlock.Tests;

public class PhotoGalleryTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<PhotoEntry>> Pools =
        TestData.Pools(("suv", TestData.SuvPool));

    [Fact]
    public void Returns_four_distinct_photos_from_the_pool()
    {
        var photos = PhotoGallery.SelectPhotos("some-id", "Kia", "SUV", Pools);

        Assert.Equal(PhotoGallery.GallerySize, photos.Count);
        Assert.Equal(photos.Count, photos.Distinct().Count());
        Assert.All(photos, file => Assert.Contains(TestData.SuvPool, p => p.File == file));
    }

    [Fact]
    public void Is_deterministic_per_id_and_varies_across_ids()
    {
        var first = PhotoGallery.SelectPhotos("aaa", "Kia", "SUV", Pools);
        var again = PhotoGallery.SelectPhotos("aaa", "Kia", "SUV", Pools);
        Assert.Equal(first, again);

        var leadPhotos = new[] { "a", "b", "c", "d", "e", "f", "g", "h" }
            .Select(id => PhotoGallery.SelectPhotos(id, "Kia", "SUV", Pools)[0])
            .Distinct();
        Assert.True(leadPhotos.Count() > 1, "different ids should not all pick the same lead photo");
    }

    [Fact]
    public void Prefers_photos_of_the_vehicles_make()
    {
        var photos = PhotoGallery.SelectPhotos("any-id", "Ford", "SUV", Pools);

        // Both Ford photos in the pool must lead the gallery, in some rotation.
        Assert.Contains(photos[0], new[] { "suv-01.jpg", "suv-02.jpg" });
        Assert.Contains(photos[1], new[] { "suv-01.jpg", "suv-02.jpg" });
    }

    [Fact]
    public void Matches_make_case_insensitively()
    {
        var photos = PhotoGallery.SelectPhotos("any-id", "FORD", "suv", Pools);
        Assert.Contains(photos[0], new[] { "suv-01.jpg", "suv-02.jpg" });
    }

    [Fact]
    public void Returns_empty_for_a_body_style_without_a_pool()
    {
        Assert.Empty(PhotoGallery.SelectPhotos("any-id", "Ford", "van", Pools));
    }

    [Fact]
    public void Fnv1a_matches_known_vectors()
    {
        // Standard FNV-1a test vectors — parity with the frontend's hash.
        Assert.Equal(2166136261u, Fnv1a.Hash(""));
        Assert.Equal(0xe40c292cu, Fnv1a.Hash("a"));
        Assert.Equal(0xbf9cf968u, Fnv1a.Hash("foobar"));
    }
}
