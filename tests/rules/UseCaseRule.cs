using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace TheYard.Rules;

// #region use-case-rule
/// <summary>
/// One rule, one body, compiled into both test projects by a link (ADR-075, the addendum of 1.0.4.70):
/// a use case that only reads is handed reading ports only. The ring is asked which use cases it holds
/// and which of them write, so the rule covers every one of them and not the ones somebody remembered
/// to write down.
/// </summary>
/// <remarks>
/// <para>The question this answers is "does this use case hold a write it never makes", and a list of
/// names cannot answer it. Until 1.0.4.126 the job hunt's Function held its read-only use cases to
/// reading ports through a dictionary of two, so the rule was exactly as good as somebody's memory: a
/// new use case that reads the share and takes the writing half of its port was never looked at,
/// because it was not in the dictionary. What makes the rule self-maintaining is reading which use
/// cases exist, and then asking the build itself which of them write.</para>
/// <para>Whether a use case writes is read from its compiled code and not from its name, because a
/// name is a promise and nothing checks a promise. A use case writes when the code it owns reaches a
/// call to a member whose name starts with a writing verb on one of this family's types - and
/// "reaches" is the whole difficulty. Two hops are invisible to a scan of one method body, and both of
/// them are in this codebase.</para>
/// <para>The first is the state machine. An <c>async</c> method's own body is builder plumbing: the
/// calls a reader sees in the source live in the <c>MoveNext</c> of a nested compiler-generated type,
/// which the builder starts generically, so no call instruction joins the two. The edge is taken from
/// <c>AsyncStateMachineAttribute</c> (and the iterator's equivalent), which names that type.</para>
/// <para>The second is delegation. <c>TrackerMerger</c> is handed the writing port and calls nothing
/// that writes: it makes a <c>MergeContext</c> and the merge steps and lets them write. Measured one
/// method deep it reads as a use case that only reads and holds a port that writes, which is this
/// rule's own failure message, on the type whose whole job is writing. So writes travel along the
/// calls a method makes, and the walk is over methods rather than types on purpose: collapsed to
/// types, <c>MergerHealth</c> would read as a writer for calling <c>TrackerMerger.IsSpoolFile</c>, a
/// static that tests a file name.</para>
/// <para>What is a port is read narrowly, for the same reason. A record is never a port: its
/// compiler-generated equality takes its own type, so every record in a ring would read as a type
/// handed one of this family's types, and a property that happens to be a noun spelled like a verb
/// (<c>TrackerFrame.Record</c>) would read as a member that writes. A port is one of this family's
/// interfaces, or one of its classes that is not a record - which is how <c>MergeContext</c>, a class
/// holding the share's writing half behind two safe ways to change the master, is a port.</para>
/// <para>The writing verbs are the site's (<c>CqrsTests</c>, ADR-115's addendum of 1.0.4.68) widened
/// by the Function's own, and the caller passes them, so one ring's vocabulary does not silently
/// become another's.</para>
/// </remarks>
public static class UseCaseRule
{
    /// <summary>
    /// One of this family's types handed to a use case, with what it can do: the port, the parameter
    /// that carries it, where that parameter comes in, and the names of its members that write.
    /// </summary>
    /// <param name="Port">The port's type.</param>
    /// <param name="Where">The constructor or method that takes it, so a reader can find it.</param>
    /// <param name="Name">The parameter's name, as the code spells it.</param>
    /// <param name="Writes">Its members whose names start with a writing verb, counting every interface it inherits; empty for a reading port.</param>
    public sealed record Handed(Type Port, string Where, string Name, IReadOnlyList<string> Writes);

    /// <summary>
    /// One use case the ring holds: a type handed at least one port, the ports it is handed, and
    /// whether its own code ever reaches a write.
    /// </summary>
    /// <param name="Type">The use case's type.</param>
    /// <param name="Ports">Every port it is handed, by constructor or by a method of its own.</param>
    /// <param name="Writes">True when the code it owns reaches a writing member, directly or through the calls it makes.</param>
    public sealed record UseCase(Type Type, IReadOnlyList<Handed> Ports, bool Writes);

    /// <summary>
    /// Every use case in a ring, each with its ports and whether it writes. The list is the rule's
    /// subject: a caller holds the read-only ones to reading ports and asserts the count is a count
    /// and not nothing, because a discovery that finds no use cases would satisfy every rule written
    /// over it.
    /// </summary>
    /// <param name="ring">The ring assembly whose use cases and ports are wanted.</param>
    /// <param name="writingVerbs">The verbs a writing member's name starts with, as alternatives for a regular expression.</param>
    public static List<UseCase> Discover(Assembly ring, string writingVerbs)
    {
        var verb = new Regex("^(" + writingVerbs + ")(?=[A-Z_]|$)", RegexOptions.Compiled);
        string family = Family(ring);
        var writers = Writers(ring.Location, family, verb);
        return [.. ring.GetTypes()
            .Where(type => !type.IsInterface && !type.IsEnum && type.DeclaringType is null && !type.Name.StartsWith('<'))
            .Select(type => new UseCase(type, Ports(type, family, verb), writers.Contains(type.FullName ?? type.Name)))
            .Where(found => found.Ports.Count > 0)
            .OrderBy(found => found.Type.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Every read-only use case that is handed a port it could write through, as a line naming the use
    /// case, the parameter and what that parameter can do. Empty is the rule holding.
    /// </summary>
    /// <param name="ring">The ring assembly to read.</param>
    /// <param name="writingVerbs">The verbs a writing member's name starts with.</param>
    public static List<string> ReadOnlyCasesHandedAWrite(Assembly ring, string writingVerbs) =>
        [.. Discover(ring, writingVerbs)
            .Where(found => !found.Writes)
            .SelectMany(found => found.Ports
                .Where(port => port.Writes.Count > 0)
                .Select(port => $"{found.Type.Name} only reads and takes {port.Port.Name} {port.Name} by {port.Where}, which can {string.Join(", ", port.Writes)}; a use case that only reads takes the reading half"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    /// <summary>The family a ring belongs to, as a namespace prefix: everything before the first dot of its assembly name, so TheYard.Application reads TheYard types and JobHunt.Application reads JobHunt ones.</summary>
    private static string Family(Assembly ring) => (ring.GetName().Name ?? "").Split('.')[0] + ".";

    /// <summary>Whether a type is one of this family's, by its namespace.</summary>
    private static bool Ours(string? space, string family) => space?.StartsWith(family, StringComparison.Ordinal) == true;

    /// <summary>
    /// Whether a type may be handed as a port: one of this family's interfaces, or one of its classes
    /// that is not a record. A record is a value a use case is given to work on, never a seam it works
    /// through, and its compiler-generated equality would make every record in the ring look like one.
    /// </summary>
    private static bool IsPort(Type type, string family) =>
        Ours(type.Namespace, family) && !type.IsEnum
        && (type.IsInterface || (type.IsClass && type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is null));

    /// <summary>
    /// Every port a type is handed: by its constructors, and by the methods it declares itself, which
    /// is how a use case written as a record with a static reader is caught beside one written as a
    /// class. Compiler-generated members and operators are left out, because a record's equality takes
    /// its own type and is not a port being handed anywhere.
    /// </summary>
    private static List<Handed> Ports(Type type, string family, Regex verb)
    {
        static Type Bare(Type type) => Nullable.GetUnderlyingType(type) ?? type;
        bool Written(MemberInfo member) =>
            member is not MethodInfo { IsSpecialName: true }
            && member.GetCustomAttribute<CompilerGeneratedAttribute>() is null
            && verb.IsMatch(member.Name);
        List<string> Writes(Type port)
        {
            IEnumerable<Type> contracts = port.IsInterface ? port.GetInterfaces().Prepend(port) : [port];
            return [.. contracts
                .SelectMany(contract => contract.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(Written)
                .Select(member => member.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];
        }

        var taken = new List<(Type Port, string Where, string Name)>();
        foreach (var maker in type.GetConstructors())
        {
            taken.AddRange(maker.GetParameters().Select(parameter => (Bare(parameter.ParameterType), "its constructor", parameter.Name ?? "?")));
        }
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<CompilerGeneratedAttribute>() is null))
        {
            taken.AddRange(method.GetParameters().Select(parameter => (Bare(parameter.ParameterType), method.Name, parameter.Name ?? "?")));
        }
        return [.. taken
            .Where(found => IsPort(found.Port, family))
            .DistinctBy(found => found.Port.FullName + " " + found.Name, StringComparer.Ordinal)
            .Select(found => new Handed(found.Port, found.Where, found.Name, Writes(found.Port)))];
    }

    /// <summary>
    /// The full name of every outermost type in the ring whose code reaches a writing member. Read from
    /// the compiled module: a method writes directly when it calls a writing member of this family's,
    /// and then a write travels backwards along every call, so a use case that delegates its writing is
    /// a writer. The async and iterator state machines are joined to the methods they belong to,
    /// because a generic builder start leaves no call instruction between them.
    /// </summary>
    private static HashSet<string> Writers(string module, string family, Regex verb)
    {
        using var read = ModuleDefinition.ReadModule(module);
        var all = read.GetTypes().SelectMany(type => type.Methods).ToList();
        var calls = all.ToDictionary(method => method, _ => new List<MethodDefinition>());
        var writes = new HashSet<MethodDefinition>();

        MethodDefinition? Here(MethodReference called)
        {
            try
            {
                var found = called.Resolve();
                return found is not null && found.Module == read ? found : null;
            }
            catch (AssemblyResolutionException)
            {
                return null;
            }
        }

        foreach (var method in all)
        {
            foreach (var built in method.CustomAttributes
                .Where(attribute => attribute.AttributeType.Name is "AsyncStateMachineAttribute" or "IteratorStateMachineAttribute")
                .Where(attribute => attribute.ConstructorArguments.Count > 0)
                .Select(attribute => (attribute.ConstructorArguments[0].Value as TypeReference)?.Resolve())
                .OfType<TypeDefinition>())
            {
                calls[method].AddRange(built.Methods.Where(calls.ContainsKey));
            }
            if (!method.HasBody)
            {
                continue;
            }
            foreach (var called in method.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>())
            {
                if (Ours(called.DeclaringType.Namespace, family) && verb.IsMatch(called.Name))
                {
                    writes.Add(method);
                }
                if (Here(called) is { } target)
                {
                    calls[method].Add(target);
                }
            }
        }

        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in all.Where(method => !owners.Contains(Outermost(method.DeclaringType))))
        {
            var seen = new HashSet<MethodDefinition> { start };
            var waiting = new Queue<MethodDefinition>([start]);
            while (waiting.Count > 0)
            {
                var at = waiting.Dequeue();
                if (writes.Contains(at))
                {
                    owners.Add(Outermost(start.DeclaringType));
                    break;
                }
                foreach (var next in calls[at].Where(seen.Add))
                {
                    waiting.Enqueue(next);
                }
            }
        }
        return owners;
    }

    /// <summary>The full name of the type a nested type sits in, however deep: the type a reader of the source would say owns the code.</summary>
    private static string Outermost(TypeReference type)
    {
        var owner = type;
        while (owner.DeclaringType is { } holder)
        {
            owner = holder;
        }
        return owner.FullName;
    }
}
// #endregion use-case-rule
