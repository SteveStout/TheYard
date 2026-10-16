using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TestProject.Application;
using TestProject.Composition;
using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>
/// Checks how the app picks its home directory from configuration. The tests call
/// FilesRegistration.HomeFor, the same method the running app uses, so they cover the real setup.
/// No setting means the sample-home folder beside the project, created if missing, so a fresh
/// copy runs with no setup. An absolute setting is used as given; a relative one goes under the
/// content root. (more in docs/ADR-003-the-line-a-path-cannot-cross.md)
/// </summary>
public sealed class HomeForTests
{
    [Fact]
    public void Empty_configuration_means_the_sample_home_beside_the_project()
    {
        using var root = new TempHome();
        HomePath home = FilesRegistration.HomeFor(new FilesOptions(), root.Root);
        Assert.Equal(Path.Combine(root.Root, "sample-home"), home.Root);
        Assert.True(Directory.Exists(home.Root));
    }

    [Fact]
    public void An_absolute_home_is_used_as_given_and_a_relative_one_is_under_the_content_root()
    {
        using var root = new TempHome();
        Assert.Equal(root.Root, FilesRegistration.HomeFor(new FilesOptions { Home = root.Root }, "/elsewhere").Root);
        Assert.Equal(Path.Combine(root.Root, "data"), FilesRegistration.HomeFor(new FilesOptions { Home = "data" }, root.Root).Root);
    }
}

/// <summary>
/// Checks that the Files limits are checked once, as the app starts. A search limit below one, a
/// ceiling below the default limit, or an upload limit of zero would otherwise surface later as a
/// 500 from the first search or upload; here the app refuses to start and says which settings.
/// (more in docs/ADR-003-the-line-a-path-cannot-cross.md)
/// </summary>
public sealed class FilesSettingsTests
{
    [Theory]
    [InlineData(200, 1000, 1, true)]
    [InlineData(1, 1, 1, true)]
    [InlineData(0, 1000, 1, false)]
    [InlineData(200, 100, 1, false)]
    [InlineData(200, 1000, 0, false)]
    public void Limits_are_usable_only_when_they_can_work_together(int limit, int ceiling, long maxUpload, bool usable)
    {
        var options = new FilesOptions { SearchLimit = limit, SearchCeiling = ceiling, MaxUploadBytes = maxUpload };
        Assert.Equal(usable, FilesRegistration.IsUsable(options));
    }

    [Fact]
    public void A_ceiling_below_one_stops_the_app_as_it_starts_and_names_the_settings()
    {
        using var home = new TempHome();
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Files:Home", home.Root);
            builder.UseSetting("Files:SearchCeiling", "0");
        });
        Exception refused = Assert.ThrowsAny<Exception>(() =>
        {
            using HttpClient client = factory.CreateClient();
        });
        Assert.Contains("SearchCeiling", refused.ToString(), StringComparison.Ordinal);
    }
}
