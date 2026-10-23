using System.Text.RegularExpressions;

namespace TheYard.Tests;

// #region host-layout-tests
/// <summary>
/// Where a file in the host lives (ADR-114: The host root holds one file). The root of
/// api/TheYard.Api holds Program.cs and nothing else; every other file is one slice's code under
/// Features/&lt;Slice&gt;/ or one concern's code under Shared/&lt;Concern&gt;/, and neither folder holds a
/// loose file. A Shared concern is a row in the record's table before it is a folder, so a new one
/// is a decision first. Placement is by measured use: SliceTests rule 1 reads the compiled host,
/// and a shared type one slice alone uses is red here too, with the slice named.
/// </summary>
public sealed class HostLayoutTests
{
    /// <summary>The host project's folder.</summary>
    private static string Api() => Path.Combine(Repo.Root(), "api", "TheYard.Api");

    /// <summary>The record that names every Shared concern, read by the test so the table and the tree agree.</summary>
    private static string Record() => Path.Combine(Repo.Root(), "docs", "decisions", "ADR-114-the-host-root-holds-one-file.md");

    [Fact]
    public void The_host_root_holds_Program_cs_alone()
    {
        string[] loose = [.. Directory.EnumerateFiles(Api(), "*.cs").Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal)];
        Assert.True(loose.SequenceEqual(["Program.cs"]), $"the host root holds {loose.Length} .cs files: {string.Join(", ", loose)}");
    }

    [Fact]
    public void Features_and_Shared_hold_no_loose_file()
    {
        var loose = new[] { "Features", "Shared" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(Api(), folder), "*.cs"))
            .Select(path => Path.GetRelativePath(Api(), path).Replace('\\', '/'))
            .ToList();
        Assert.True(loose.Count == 0, "loose files: " + string.Join(", ", loose));
    }

    [Fact]
    public void No_folder_but_Features_Shared_and_the_build_s_own_sits_at_the_host_root()
    {
        // The root Cloud/ folder moved whole into Features/Admin/Cloud; a folder of code beside
        // Features/ and Shared/ is the shape this record retired.
        string[] allowed = ["Features", "Shared", "Properties", "wwwroot", "bin", "obj"];
        var other = Directory.EnumerateDirectories(Api())
            .Select(path => Path.GetFileName(path)!)
            .Where(name => !allowed.Contains(name, StringComparer.Ordinal))
            .ToList();
        Assert.True(other.Count == 0, "folders beside Features/ and Shared/: " + string.Join(", ", other));
    }

    [Fact]
    public void Every_Shared_concern_is_a_row_in_the_record_and_every_row_is_a_folder()
    {
        var rows = Regex.Matches(File.ReadAllText(Record()), @"(?m)^\|\s*`Shared/(\w+)/`\s*\|")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var folders = Directory.EnumerateDirectories(Path.Combine(Api(), "Shared"))
            .Select(path => Path.GetFileName(path)!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(rows.Count > 0, "the record's Shared table has no rows");
        Assert.True(folders.SetEquals(rows),
            $"folders not in the record: {string.Join(", ", folders.Except(rows))}; rows with no folder: {string.Join(", ", rows.Except(folders))}");
    }

    [Fact]
    public void A_file_under_a_Shared_concern_declares_that_concern_s_namespace()
    {
        string shared = Path.Combine(Api(), "Shared");
        var wrong = Directory.EnumerateFiles(shared, "*.cs", SearchOption.AllDirectories)
            .Select(path => (path, concern: Path.GetRelativePath(shared, path).Split(Path.DirectorySeparatorChar)[0],
                declared: Regex.Match(File.ReadAllText(path), @"(?m)^namespace\s+([\w.]+)").Groups[1].Value))
            .Where(file => file.declared != "TheYard.Api.Shared." + file.concern)
            .Select(file => $"{Path.GetRelativePath(Repo.Root(), file.path).Replace('\\', '/')} declares {file.declared}, its folder says TheYard.Api.Shared.{file.concern}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Nothing_sits_in_the_host_s_root_namespace()
    {
        // Program is the top-level statements' class, in the global namespace; TheYard.Api itself holds nothing.
        var root = typeof(Program).Assembly.GetTypes()
            .Where(type => type.Namespace == "TheYard.Api" && !type.Name.StartsWith('<'))
            .Select(type => type.FullName)
            .ToList();
        Assert.True(root.Count == 0, "types in the root namespace: " + string.Join(", ", root));
    }

    [Fact]
    public void A_shared_type_one_slice_alone_uses_is_red_with_the_slice_named()
    {
        // The inverse of placing by use: a type under Shared/ whose every user is one slice is that
        // slice's code. The list is SliceTests' own, read from the compiled host, so the two never disagree.
        var lonely = SliceTests.LonelySharedTypes();
        Assert.True(lonely.Count == 0, string.Join(Environment.NewLine, lonely));
    }
}
// #endregion host-layout-tests
