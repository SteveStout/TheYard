using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// Sealed by default, held here rather than in a review comment. The practice
/// is written up on the Best Practices page this class is named after,
/// docs/SEALED.md: a class in this application is sealed unless it is a base
/// somebody actually derives from, and the analyzer that catches the internal
/// half of it is on as a warning, which the gate reads as red.
///
/// <para>A practice nothing checks lasts until the first hurried afternoon.
/// The house had sealed almost everything already and one class had slipped
/// through, which is the argument for a test rather than against one: the
/// slip is never the class somebody thought about.</para>
///
/// <para>Records are counted here and left to their own rule. A record is a
/// value shape whose equality the compiler writes, and sealing all of them is
/// a change to fifty-eight files rather than a practice, so the page says what
/// the count is and does not claim more.</para>
/// </summary>
public class SealedByDefaultTests
{
    // #region practice
    /// <summary>
    /// The five projects that make up the application. The test project is not
    /// one of them: a test class is instantiated by a runner that needs no help
    /// from the type system, and three of them do inherit a shared base on
    /// purpose.
    /// </summary>
    private static readonly string[] Projects =
    [
        "TheYard.Domain",
        "TheYard.Application",
        "TheYard.Infrastructure",
        "TheYard.Infrastructure.Cosmos",
        "TheYard.Api",
    ];

    /// <summary>
    /// Classes that must stay open, each with the reason it is open, checked
    /// by the test below so that an entry cannot outlive its reason. Empty, and
    /// an entry here is a decision rather than an oversight.
    /// </summary>
    private static readonly Dictionary<string, string> Open = new(StringComparer.Ordinal)
    {
        // "TheYard.Api.Example" = "the reason this one is written to be inherited",
    };

    private static List<Type> Written() =>
        Projects
            .SelectMany(name => Assembly.Load(new AssemblyName(name)).GetTypes())
            .Where(IsWrittenHere)
            .ToList();

    /// <summary>
    /// A type somebody in this repository typed. The compiler writes plenty of
    /// its own, from closures to the regex source generator's output to the
    /// attributes it emits for nullability, and none of those are anybody's
    /// practice: they carry a generated marker, sit outside the project's own
    /// namespaces, or carry a name no C# file can spell.
    /// </summary>
    private static bool IsWrittenHere(Type type) =>
        OneOfOurs(type)
        && !(type.FullName ?? string.Empty).Contains('<', StringComparison.Ordinal)
        && !IsGenerated(type)
        && !typeof(Delegate).IsAssignableFrom(type);

    /// <summary>
    /// A type in one of the project's own namespaces, or in none at all:
    /// Program has no namespace, because the host is written as top-level
    /// statements and the class the compiler generates for them lands in the
    /// global one. It is still this repository's class and the practice covers
    /// it.
    /// </summary>
    private static bool OneOfOurs(Type type) =>
        type.Namespace is null
        || type.Namespace.StartsWith("TheYard", StringComparison.Ordinal);

    private static bool IsGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || type.IsDefined(typeof(GeneratedCodeAttribute), inherit: false)
        || (type.DeclaringType is not null && IsGenerated(type.DeclaringType));

    /// <summary>
    /// A record, either kind. The compiler gives a record class a clone method
    /// no C# file can call, and a record struct the member printer behind its
    /// ToString; both are how a reader of reflection tells a record from the
    /// class or struct it is compiled to.
    /// </summary>
    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null
        || (type.IsValueType
            && type.GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null);

    /// <summary>
    /// The five shapes the page counts. Anything that is not a class and not a
    /// record is not this rule's business: an interface has nothing to seal, an
    /// enum cannot be inherited, and a plain struct is sealed by the runtime.
    /// </summary>
    private static string Shape(Type type)
    {
        if (IsRecord(type))
        {
            return "records";
        }
        if (!type.IsClass)
        {
            return string.Empty;
        }
        if (type.IsAbstract && type.IsSealed)
        {
            return "static";
        }
        if (type.IsAbstract)
        {
            return "abstract";
        }
        return type.IsSealed ? "sealed" : "open";
    }

    private static Dictionary<string, int> Counted()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["sealed"] = 0,
            ["static"] = 0,
            ["abstract"] = 0,
            ["open"] = 0,
            ["records"] = 0,
        };
        foreach (Type type in Written())
        {
            string shape = Shape(type);
            if (counts.ContainsKey(shape))
            {
                counts[shape]++;
            }
        }
        return counts;
    }

    [Fact]
    public void Every_class_in_the_application_is_sealed_static_abstract_or_inherited()
    {
        var written = Written();

        // A scan that reads nothing passes, which is the failure this class
        // exists to prevent somewhere else. The floor moves with the project
        // and should never move down by much.
        Assert.True(written.Count > 100, $"only {written.Count} types were read across {Projects.Length} projects");

        var everything = written
            .Concat(typeof(SealedByDefaultTests).Assembly.GetTypes())
            .ToList();

        var unexplained = new List<string>();
        foreach (Type type in written.Where(type => Shape(type) == "open"))
        {
            bool inherited = everything.Any(other => other != type && other.BaseType == type);
            if (inherited || Open.ContainsKey(type.FullName ?? type.Name))
            {
                continue;
            }
            unexplained.Add(
                $"{type.FullName} is open, nothing in the solution derives from it, "
                + "and it is not on the allow-list in this file");
        }

        Assert.True(unexplained.Count == 0, string.Join(Environment.NewLine, unexplained));
    }

    [Fact]
    public void The_allow_list_holds_no_entry_that_has_stopped_being_open()
    {
        var written = Written();
        var stale = new List<string>();

        foreach (string name in Open.Keys)
        {
            Type? listed = written.Find(candidate => candidate.FullName == name);
            if (listed is null || Shape(listed) != "open")
            {
                stale.Add($"{name} is on the allow-list and is not an open class in these projects");
            }
        }

        Assert.True(stale.Count == 0, string.Join(Environment.NewLine, stale));
    }
    // #endregion practice

    // #region page-numbers
    /// <summary>
    /// The page states the shape of this build in a table, and a number in
    /// prose goes stale silently, so the table is read back against what the
    /// build actually holds. The same rule the record that explains the API
    /// host is held to.
    /// </summary>
    [Fact]
    public void The_page_quotes_the_shape_this_build_has()
    {
        string page = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "SEALED.md"));
        var wrong = new List<string>();

        foreach ((string shape, int actual) in Counted())
        {
            var quoted = Regex.Match(page, $@"^\| {shape} \| (\d+) \|", RegexOptions.Multiline);
            if (!quoted.Success)
            {
                wrong.Add($"docs/SEALED.md has no row for {shape}");
                continue;
            }
            if (int.Parse(quoted.Groups[1].Value) != actual)
            {
                wrong.Add($"docs/SEALED.md says {quoted.Groups[1].Value} {shape} and this build has {actual}");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// The analyzer half of the practice. CA1852 is the rule that names an
    /// internal class nothing derives from, and the page says it is on as a
    /// warning here, which matters because the gate treats any warning as red.
    /// A page that claims a setting is a page that can be wrong about it.
    /// </summary>
    [Fact]
    public void The_analyzer_that_catches_the_internal_half_is_on_as_a_warning()
    {
        string editorconfig = File.ReadAllText(Path.Combine(Repo.Root(), ".editorconfig"));

        Assert.Contains("dotnet_diagnostic.CA1852.severity = warning", editorconfig, StringComparison.Ordinal);
        Assert.Contains("CA1852", File.ReadAllText(Path.Combine(Repo.Root(), "docs", "SEALED.md")), StringComparison.Ordinal);
    }
    // #endregion page-numbers
}
