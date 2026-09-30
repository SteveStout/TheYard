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
