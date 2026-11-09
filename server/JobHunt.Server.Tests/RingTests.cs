using System.Text.RegularExpressions;
using TheYard.Rules;

namespace JobHunt.Server.Tests;

/// <summary>The rings lean inward and the inner two touch nothing (ADR-117, the addendum on the machine lanes).</summary>
public sealed class RingTests
{
    private static readonly Regex Reach = new(@"\b(File|Directory|Process|HttpClient|Environment|Console|DateTime(Offset)?\.(Utc)?Now)\b[.(]", RegexOptions.Compiled);

    [Theory]
    [InlineData("JobHunt.Server.Domain", new string[0])]
    [InlineData("JobHunt.Server.Application", new[] { "JobHunt.Server.Domain" })]
    [InlineData("JobHunt.Server.Infrastructure", new[] { "JobHunt.Server.Application", "JobHunt.Server.Domain" })]
    public void Each_ring_leans_only_inward(string ring, string[] allowed)
    {
        string project = File.ReadAllText(Path.Combine(Repo.Root(), "server", ring, ring + ".csproj"));
        string[] references = [.. Regex.Matches(project, @"ProjectReference Include=""[^""]*\\([\w.]+)\.csproj""").Select(m => m.Groups[1].Value)];
        Assert.Equal(allowed, references);
    }

    [Theory]
    [InlineData("JobHunt.Server.Domain")]
    [InlineData("JobHunt.Server.Application")]
    public void Domain_and_Application_reach_nothing(string ring)
    {
        var wrong = Repo.Sources(ring).Where(path => Reach.IsMatch(Code(path))).Select(path => Path.GetRelativePath(Repo.Root(), path)).ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Theory]
    [InlineData("JobHunt.Server.Domain")]
    [InlineData("JobHunt.Server.Application")]
    [InlineData("JobHunt.Server.Infrastructure")]
    public void A_file_s_namespace_is_its_folder(string ring)
    {
        var wrong = NamespaceRule.NotTheirFolder(Path.Combine(Repo.Root(), "server", ring), ring, Repo.Root());
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void No_production_file_runs_past_the_length_rule()
    {
        var files = new[] { "JobHunt.Server", "JobHunt.Server.Domain", "JobHunt.Server.Application", "JobHunt.Server.Infrastructure" }.SelectMany(Repo.Sources);
        var wrong = LengthRule.TooLong(files, Repo.Root());
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Nothing_in_the_process_starts_a_run_without_its_permission_check()
    {
        string forbidden = "--dangerously-" + "skip-permissions";
        var files = new[] { "JobHunt.Server", "JobHunt.Server.Domain", "JobHunt.Server.Application", "JobHunt.Server.Infrastructure" }.SelectMany(Repo.Sources);
        Assert.DoesNotContain(files, path => File.ReadAllText(path).Contains(forbidden, StringComparison.Ordinal));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(Repo.Root(), "server", "systemd")), path => File.ReadAllText(path).Contains(forbidden, StringComparison.Ordinal));
    }

    private static string Code(string path) =>
        string.Join('\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
