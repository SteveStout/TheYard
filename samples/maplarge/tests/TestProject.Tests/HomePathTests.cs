using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>The line a path cannot cross (ADR-003), held as strings on whatever OS runs the suite.</summary>
public sealed class HomePathTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\home\files" : "/home/files";
    private readonly HomePath _home = new(Root);

    [Fact]
    public void Empty_and_null_resolve_to_the_root()
    {
        Assert.Equal(_home.Root, _home.Resolve(null));
        Assert.Equal(_home.Root, _home.Resolve(""));
        Assert.Equal(_home.Root, _home.Resolve("   "));
    }

    [Fact]
    public void A_plain_relative_path_resolves_under_the_root()
    {
        string resolved = _home.Resolve("docs/notes/todo.md");
        Assert.Equal(Path.Combine(_home.Root, "docs", "notes", "todo.md"), resolved);
        Assert.True(_home.IsInside(resolved));
    }

    [Fact]
    public void Backslashes_are_the_same_as_forward_slashes()
    {
        Assert.Equal(_home.Resolve("docs/notes"), _home.Resolve(@"docs\notes"));
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("docs/../../secrets")]
    [InlineData("docs/./notes")]
    [InlineData("..")]
    public void A_dot_or_dot_dot_segment_is_refused(string path)
    {
        var refused = Assert.Throws<PathRefusedException>(() => _home.Resolve(path));
        Assert.Contains("'.' or '..'", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData(@"\windows")]
    [InlineData(@"C:\windows")]
    public void A_rooted_path_is_refused(string path)
    {
        Assert.Throws<PathRefusedException>(() => _home.Resolve(path));
    }

    [Fact]
    public void A_name_with_a_forbidden_character_is_refused()
    {
        Assert.Throws<PathRefusedException>(() => _home.Resolve("docs/bad\0name"));
    }

    [Fact]
    public void The_root_with_a_prefix_in_common_is_outside()
    {
        // /home/files-old starts with the same characters as /home/files and is not inside it.
        Assert.False(_home.IsInside(_home.Root + "-old"));
        Assert.False(_home.IsInside(Path.Combine(_home.Root + "-old", "x")));
    }

    [Fact]
    public void Relative_is_the_wire_form()
    {
        Assert.Equal("", _home.Relative(_home.Root));
        Assert.Equal("docs/notes/todo.md", _home.Relative(Path.Combine(_home.Root, "docs", "notes", "todo.md")));
    }

    [Fact]
    public void Relative_refuses_a_path_outside()
    {
        Assert.Throws<PathRefusedException>(() => _home.Relative(Path.Combine(Path.GetTempPath(), "elsewhere")));
    }

    [Fact]
    public void Parent_and_name_read_a_wire_path()
    {
        Assert.Null(HomePath.ParentOf(""));
        Assert.Equal("", HomePath.ParentOf("docs"));
        Assert.Equal("docs", HomePath.ParentOf("docs/notes"));
        Assert.Equal("notes", HomePath.NameOf("docs/notes"));
        Assert.Equal("docs", HomePath.NameOf("docs"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    public void A_new_name_is_one_segment(string name)
    {
        Assert.Throws<PathRefusedException>(() => HomePath.ValidName(name));
    }

    [Fact]
    public void A_new_name_is_trimmed()
    {
        Assert.Equal("notes", HomePath.ValidName("  notes "));
    }

    [Fact]
    public void The_root_must_be_absolute()
    {
        Assert.Throws<ArgumentException>(() => new HomePath("relative/home"));
    }

    [Fact]
    public void Case_follows_the_operating_system()
    {
        string upper = _home.Root.ToUpperInvariant();
        Assert.Equal(OperatingSystem.IsWindows(), _home.IsInside(upper));
    }
}
