using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

// #region function-cqrs-tests
/// <summary>
/// CQRS in the job hunt's Function (ADR-115, the addendum of 1.0.4.75, and its addendum below). The site's rule is a
/// query or a command with one handler, because the site answers requests. The Function answers none: its timers each
/// run one use case, so there is no request record to name. What carries over is the half that keeps reads from
/// writing: a use case that only reads is handed reading ports only, a port that reads and writes is split into its
/// reading half and the half that extends it, and nothing is a bag of use cases behind one name.
/// </summary>
/// <remarks>
/// Which use cases those are is found rather than listed. Until 1.0.4.126 the first rule below ran over a dictionary
/// of two, <c>InboxTriage</c> and <c>MergerHealth</c>, so a use case nobody added to it was never asked the question
/// at all. The dictionary is gone: <c>tests/rules/UseCaseRule.cs</c> reads the ring for every type handed a port and
/// asks the compiled code which of them write, and the rule runs over the ones that do not. It finds the same two it
/// used to be told, which is the point - the number did not change, the way of arriving at it did.
/// </remarks>
public sealed partial class CqrsTests
{
    private static readonly Assembly Application = typeof(IMergeStore).Assembly;

    /// <summary>
    /// The verbs a member that writes starts with: the site's ten (ADR-115, the addendum of 1.0.4.68) widened by the
    /// Function's own, which moves files on a share, appends to logs, archives mail and marks rows.
    /// </summary>
    private const string WritingVerbs = "Keep|Write|Save|Upsert|Delete|Add|Remove|Record|Put|Set|Append|Move|Replace|Ensure|Archive|Mark|Block";

    /// <summary>Every use case the Application ring holds, with its ports and whether its code reaches a write.</summary>
    private static List<TheYard.Rules.UseCaseRule.UseCase> Cases() => TheYard.Rules.UseCaseRule.Discover(Application, WritingVerbs);

    [Fact]
    public void Every_use_case_the_ring_holds_is_found()
    {
        // A rule over a discovered list is worth nothing if the discovery can come back empty, and an empty list
        // satisfies the rule below in silence. So the count is asserted, both ways: the ring holds use cases, and
        // some of them only read. Eighteen and two when the dictionary was replaced.
        var cases = Cases();
        Assert.True(cases.Count >= 16, $"only {cases.Count} use cases were found in {Application.GetName().Name}");
        var reading = cases.Where(found => !found.Writes).Select(found => found.Type.Name).Order(StringComparer.Ordinal).ToList();
        Assert.True(reading.Count >= 2, "no use case was found that only reads, so the rule below asks nothing");
        Assert.All(cases, found => Assert.NotEmpty(found.Ports));
    }

    [Fact]
    public void A_use_case_that_writes_is_not_read_as_one_that_only_reads()
    {
        // The discovery's own failure mode, held to a type that proves it. TrackerMerger is handed the writing port
        // and calls nothing that writes: it makes a MergeContext and the merge steps and lets them write. Measured
        // one method deep it reads as a use case that only reads holding a port that writes, which is the next
        // test's failure message aimed at the type whose whole job is writing. A discovery that cannot see through
        // delegation fails here before it can fail there.
        var writes = Cases().Where(found => found.Writes).Select(found => found.Type).ToList();
        Assert.Contains(typeof(TrackerMerger), writes);
        Assert.Contains(typeof(MergeContext), writes);
        Assert.DoesNotContain(typeof(InboxTriage), writes);
    }

    [Fact]
    public void A_use_case_that_only_reads_is_handed_reading_ports_only()
    {
        var wrong = TheYard.Rules.UseCaseRule.ReadOnlyCasesHandedAWrite(Application, WritingVerbs);
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void A_port_named_as_a_reader_has_no_member_that_writes()
    {
        var wrong = Application.GetTypes()
            .Where(type => type.IsInterface && type.Name.EndsWith("Reader", StringComparison.Ordinal))
            .Select(type => (type, writes: Writing(type)))
            .Where(found => found.writes.Count > 0)
            .Select(found => $"{found.type.Name} can {string.Join(", ", found.writes)}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        Assert.Contains(typeof(IShareReader), typeof(IMergeStore).GetInterfaces());
        Assert.Contains(typeof(IInboxReader), typeof(IInbox).GetInterfaces());
    }

    [Fact]
    public void Nothing_in_the_rings_is_a_service()
    {
        var services = new[] { typeof(TrackerCsvText).Assembly, Application, typeof(ShareMergeStore).Assembly }
            .SelectMany(ring => ring.GetTypes())
            .Where(type => type.Name.EndsWith("Service", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToList();
        Assert.True(services.Count == 0, "types named *Service: " + string.Join(", ", services));
    }

    [Fact]
    public void The_practices_page_quotes_the_counts_this_build_has()
    {
        // A number in prose goes stale silently, so the page's table is read back against the build, the way the
        // site's own CqrsTests reads its four counts.
        string page = File.ReadAllText(Path.Combine(Repo(), "docs", "best-practices", "CQRS.md"));
        var cases = Cases();
        var counted = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["the Function's use cases"] = cases.Count,
            ["of them that only read"] = cases.Count(found => !found.Writes),
        };
        var wrong = new List<string>();
        foreach (var (shape, actual) in counted)
        {
            var quoted = Regex.Match(page, $@"^\| {Regex.Escape(shape)} \| (\d+) \|", RegexOptions.Multiline);
            if (!quoted.Success)
            {
                wrong.Add($"docs/best-practices/CQRS.md has no row for {shape}");
            }
            else if (int.Parse(quoted.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) != actual)
            {
                wrong.Add($"docs/best-practices/CQRS.md says {quoted.Groups[1].Value} for {shape} and this build has {actual}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>Every member, counting inherited interfaces, of one of the Function's own types whose name starts with a writing verb.</summary>
    private static List<string> Writing(Type port)
    {
        if (port.Namespace?.StartsWith("JobHunt.", StringComparison.Ordinal) != true)
        {
            return [];
        }
        IEnumerable<Type> contracts = port.IsInterface ? port.GetInterfaces().Prepend(port) : [port];
        return [.. contracts
            .SelectMany(contract => contract.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(member => member is not MethodInfo { IsSpecialName: true } && WritingVerb().IsMatch(member.Name))
            .Select(member => member.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static string Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Domain")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/JobHunt.Domain not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex("^(" + WritingVerbs + ")(?=[A-Z_]|$)")]
    private static partial Regex WritingVerb();
}
// #endregion function-cqrs-tests
