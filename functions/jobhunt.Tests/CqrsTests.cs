using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

// #region function-cqrs-tests
/// <summary>
/// CQRS in the job hunt's Function (ADR-115, the addendum of 1.0.4.75). The site's rule is a query or a command with one
/// handler, because the site answers requests. The Function answers none: its timers each run one use case, so there
/// is no request record to name. What carries over is the half that keeps reads from writing: a use case that only
/// reads is handed reading ports only, a port that reads and writes is split into its reading half and the half that
/// extends it, and nothing is a bag of use cases behind one name.
/// </summary>
public sealed partial class CqrsTests
{
    private static readonly Assembly Application = typeof(IMergeStore).Assembly;

    /// <summary>The use cases that only read, each with what it reads; the build holds each to reading ports.</summary>
    private static readonly Dictionary<Type, string> Reads = new()
    {
        [typeof(InboxTriage)] = "the triage window: the inbox's unjudged ids and the share's seen list",
        [typeof(MergerHealth)] = "the Merger's health: the two spools' listings and merge.log's age",
    };

    [Fact]
    public void A_use_case_that_only_reads_is_handed_reading_ports_only()
    {
        var wrong = Reads.Keys
            .SelectMany(type => type.GetConstructors().SelectMany(c => c.GetParameters())
                .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).SelectMany(m => m.GetParameters()))
                .Select(parameter => (type, parameter, writes: Writing(parameter.ParameterType))))
            .Where(found => found.writes.Count > 0)
            .Select(found => $"{found.type.Name} takes {found.parameter.ParameterType.Name} {found.parameter.Name}, which can {string.Join(", ", found.writes)}")
            .Distinct(StringComparer.Ordinal)
            .ToList();
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

    [GeneratedRegex(@"^(Keep|Write|Save|Upsert|Delete|Add|Remove|Record|Put|Set|Append|Move|Replace|Ensure|Archive|Mark|Block)(?=[A-Z_]|$)")]
    private static partial Regex WritingVerb();
}
// #endregion function-cqrs-tests
