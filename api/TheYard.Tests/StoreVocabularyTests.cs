using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace TheYard.Tests;

// #region store-vocabulary
/// <summary>
/// The Application ring speaks no store's language (ADR: Onion and SOLID, how this codebase holds
/// them). OnionTests holds what the ring references, and a ring can reference nothing but .NET and
/// still name a Cosmos DB request charge in every other record. So this reads the names themselves:
/// every type, property, field, method, event and parameter the Application assembly declares,
/// split into its words and refused when one of them is the vocabulary of a particular store. The
/// adapters name these things for what they are; above the port the same figure is a charge, a
/// scope, a shard or a statement, and the wire keeps its old field names through JsonPropertyName.
/// </summary>
public sealed partial class StoreVocabularyTests
{
    /// <summary>
    /// The names that may keep a store's word, each with why. A name here that no longer exists, or no
    /// longer carries the word, fails the test, so the list cannot outlive what it excuses. The list
    /// is empty: every name the ring had was renamed rather than excused. StoreKeys keeps the values
    /// "sql" and "cosmos", which are the header's and the claim's words on the wire, and a value is
    /// not a name.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void Application_names_no_store_s_vocabulary()
    {
        var found = Names().Where(name => SpeaksAStore(name.Word)).Select(name => name.Where).Distinct(StringComparer.Ordinal).ToList();
        var wrong = found.Where(where => !Allowed.ContainsKey(where)).Select(where => $"{where} names a store's vocabulary").ToList();
        wrong.AddRange(Allowed.Keys.Where(where => !found.Contains(where)).Select(where => $"{where} is allowed but no longer names a store's vocabulary"));
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void A_renamed_figure_keeps_its_field_name_on_the_wire()
    {
        // The page, the kept rings and the other site read these by the names they had before the
        // ring stopped speaking a store's language; a rename in C# is not a rename on the wire.
        var wire = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        string Fields(object value) => string.Join(",", JsonSerializer.SerializeToElement(value, value.GetType(), wire).EnumerateObject().Select(field => field.Name));
        var at = DateTimeOffset.UnixEpoch;
        Assert.Equal(
            "at,container,kind,text,parameters,partition,physical_partitions,request_charge,duration_ms,outcome,request,request_id",
            Fields(new StoreOperation(at, "bids", StoreOperationKind.Query, "", [], "cross-partition", 1, 2.5, 3, "200", null, null)));
        Assert.Equal("query,partitions,request_charge,duration_ms,documents", Fields(new ExperimentRow("q", "1 logical", 2.5, 3, 1)));
        Assert.Equal("available,reason,container,physical_partitions,documents,rows,ran_at", Fields(new ExperimentResult(true, null, "vehicles", 1, 1, [], at)));
        string bucket = Fields(new MachineBucket(at, 1, 0, 0, 0, 0, null, null, 1, 2, 3, 4, 5));
        Assert.Contains("sql_cpu_percent,sql_memory_percent,sql_data_io_percent,request_units,operations", bucket, StringComparison.Ordinal);
        string minute = Fields(new MachineMinute(at, "site", 0, 0, 0, 0, null, null, 1, 2, 3, 4, 5));
        Assert.Contains("sql_cpu_percent,sql_memory_percent,sql_data_io_percent,request_units,operations", minute, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RequestCharge")]
    [InlineData("SeedRequestUnits")]
    [InlineData("PhysicalPartitions")]
    [InlineData("ISqlLog")]
    [InlineData("SqlCpuPercent")]
    [InlineData("_sqlLog")]
    [InlineData("CosmosVisitorStore")]
    [InlineData("SeedRu")]
    public void The_rule_refuses_the_words_it_names(string name) => Assert.True(SpeaksAStore(name), name);

    [Theory]
    [InlineData("Charge")]
    [InlineData("Scope")]
    [InlineData("Shards")]
    [InlineData("IStatementLog")]
    [InlineData("StoreCpuPercent")]
    [InlineData("Runs")]
    [InlineData("Rules")]
    public void The_rule_lets_a_neutral_name_through(string name) => Assert.False(SpeaksAStore(name), name);

    /// <summary>
    /// True when a name carries a store's word: SQL, SQLite or Cosmos anywhere in it, a request charge or
    /// a request unit, a partition, or RU on its own as a word.
    /// </summary>
    private static bool SpeaksAStore(string name) =>
        Vocabulary().IsMatch(name) || Words().Matches(name).Any(word => word.Value.Equals("ru", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("sql|cosmos|request_?charge|request_?unit|partition", RegexOptions.IgnoreCase)]
    private static partial Regex Vocabulary();

    /// <summary>The words of a PascalCase or camelCase name: a run of capitals, or a capital and its lower-case tail.</summary>
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Words();

    /// <summary>Every name the Application assembly declares, with where it is, leaving out the names the compiler writes.</summary>
    private static IEnumerable<(string Where, string Word)> Names()
    {
        using var module = ModuleDefinition.ReadModule(typeof(StandingReader).Assembly.Location);
        var names = new List<(string, string)>();
        foreach (var type in module.GetTypes().Where(type => IsWritten(type.Name) && OuterNamespace(type).StartsWith("TheYard.Application", StringComparison.Ordinal)))
        {
            string at = type.FullName.Replace('/', '.');
            names.Add((at, type.Name));
            names.AddRange(type.Properties.Where(member => IsWritten(member.Name)).Select(member => ($"{at}.{member.Name}", member.Name)));
            names.AddRange(type.Fields.Where(member => IsWritten(member.Name)).Select(member => ($"{at}.{member.Name}", member.Name)));
            names.AddRange(type.Events.Where(member => IsWritten(member.Name)).Select(member => ($"{at}.{member.Name}", member.Name)));
            foreach (var method in type.Methods.Where(method => IsWritten(method.Name)))
            {
                if (!method.IsSpecialName)
                {
                    names.Add(($"{at}.{method.Name}", method.Name));
                }
                names.AddRange(method.Parameters.Where(parameter => IsWritten(parameter.Name)).Select(parameter => ($"{at}.{method.Name}({parameter.Name})", parameter.Name)));
            }
        }
        return names;
    }

    /// <summary>False for a name the compiler wrote (a backing field, a closure, a state machine), which carries a written name inside angle brackets.</summary>
    private static bool IsWritten(string name) => !name.Contains('<', StringComparison.Ordinal) && !name.Contains('$', StringComparison.Ordinal);

    /// <summary>A nested type has no namespace of its own; it is in the namespace of the type it sits inside.</summary>
    private static string OuterNamespace(TypeDefinition type)
    {
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }
        return type.Namespace ?? "";
    }
}
// #endregion store-vocabulary
