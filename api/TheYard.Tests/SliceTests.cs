using System.Reflection;
using NetArchTest.Rules;

namespace TheYard.Tests;

// #region slice-tests
/// <summary>
/// The second axis of the onion (ADR: The onion cut into vertical slices): the rules that hold a
/// feature to one folder per ring and its dependencies to what it is handed. The ring rules are in
/// OnionTests; these are the rules the record added beside them, one rung of its ladder at a time.
/// </summary>
public sealed class SliceTests
{
    /// <summary>The host's assembly, read by name so a type that no longer exists is a null and not a compile error.</summary>
    private static readonly System.Reflection.Assembly Host = typeof(Program).Assembly;

    [Fact]
    public void Rule_1_a_slice_depends_only_on_its_own_folder_and_the_shared_ring()
    {
        // A slice is the namespace of its folder under Features/. It may use anything that is not a
        // slice (the shared part of the host, and the inner rings) and its own types; never another
        // slice's. The shared part of the host uses no slice at all: Program.cs finds the modules
        // by reflection, which is no dependency.
        var slices = Host.GetTypes()
            .Select(type => SliceOf(type.Namespace))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(slices.Count >= 8, $"only {slices.Count} slices were found: {string.Join(", ", slices)}");

        var wrong = new List<string>();
        foreach (string slice in slices)
        {
            string[] others = [.. slices.Where(other => other != slice && !other.StartsWith(slice + ".", StringComparison.Ordinal) && !slice.StartsWith(other + ".", StringComparison.Ordinal))];
            var result = Types.InAssembly(Host)
                .That().ResideInNamespace(slice)
                .ShouldNot().HaveDependencyOnAny(others)
                .GetResult();
            wrong.AddRange((result.FailingTypes ?? []).Select(type => $"{type.FullName} reaches another slice"));
        }
        var shared = Types.InAssembly(Host)
            .That().DoNotResideInNamespace(FeaturesNamespace)
            .And().DoNotHaveName("Program")
            .ShouldNot().HaveDependencyOnAny([FeaturesNamespace])
            .GetResult();
        wrong.AddRange((shared.FailingTypes ?? []).Select(type => $"{type.FullName} is shared and reaches a slice"));
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Rule_1_in_the_inner_rings_too_a_slice_uses_no_other_slice()
    {
        // The same folder name in every ring: Application's and Infrastructure's slices are the
        // namespaces under their Features, and none reaches another's.
        foreach (var ring in new[] { typeof(TheYard.Application.Shared.Auction.StandingReader).Assembly, typeof(TheYard.Infrastructure.YardDbContext).Assembly })
        {
            string root = ring.GetName().Name + ".Features";
            string[] slices = [.. ring.GetTypes().Select(type => type.Namespace).OfType<string>()
                .Where(ns => ns.StartsWith(root + ".", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)];
            foreach (string slice in slices)
            {
                string[] others = [.. slices.Where(other => other != slice)];
                if (others.Length == 0)
                {
                    continue;
                }
                var result = Types.InAssembly(ring).That().ResideInNamespace(slice).ShouldNot().HaveDependencyOnAny(others).GetResult();
                Assert.True(result.IsSuccessful, string.Join(", ", (result.FailingTypes ?? []).Select(type => $"{type.FullName} reaches another slice")));
            }
        }
    }

    [Fact]
    public void Rule_1_by_folder_a_file_under_a_slice_declares_that_slice_s_namespace()
    {
        // Rule 1 is held by namespace, so a file is in a slice by what it declares. A file under
        // Features/Links/ that declared the host's root namespace would be shared by its words and a
        // slice's by its place; the folder and the namespace say the same thing or this is red.
        string features = Path.Combine(Repo.Root(), "api", "TheYard.Api", "Features");
        var wrong = Directory.EnumerateFiles(features, "*.cs", SearchOption.AllDirectories)
            .Select(path => (path, expected: "TheYard.Api.Features." + Path.GetRelativePath(features, Path.GetDirectoryName(path)!).Replace(Path.DirectorySeparatorChar, '.'),
                declared: System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), @"(?m)^namespace\s+([\w.]+)").Groups[1].Value))
            .Where(file => file.declared != file.expected)
            .Select(file => $"{Path.GetRelativePath(Repo.Root(), file.path)} declares {file.declared}, its folder says {file.expected}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Rule_1_a_shared_type_one_slice_alone_uses_lives_in_that_slice()
    {
        // The shared part of the host is what more than one place uses. A shared type whose every user
        // sits in one slice is that slice's code standing outside its folder, reachable by every
        // other slice; it moves in. Its users are read from the compiled host: fields, signatures,
        // base types and every type a method body names. Since ADR-114 the shared part is Shared/,
        // one namespace per concern, and the root namespace holds only Program.
        var lonely = LonelySharedTypes();
        Assert.True(lonely.Count == 0, string.Join(Environment.NewLine, lonely));
    }

    /// <summary>
    /// Every shared type (the root namespace or a Shared concern) that exactly one slice uses and no
    /// shared code does, with the slice named; HostLayoutTests reads the same list.
    /// </summary>
    internal static List<string> LonelySharedTypes()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(Host.Location);
        var users = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var type in module.GetTypes())
        {
            string user = Outermost(type).FullName;
            foreach (string used in Named(type))
            {
                if (used != user)
                {
                    (users.TryGetValue(used, out var set) ? set : users[used] = new HashSet<string>(StringComparer.Ordinal)).Add(user);
                }
            }
        }
        return [.. module.Types
            .Where(type => (type.Namespace == "TheYard.Api" || type.Namespace.StartsWith("TheYard.Api.Shared", StringComparison.Ordinal)) && !type.Name.StartsWith('<'))
            .Select(type => (type.FullName, slices: users.TryGetValue(type.FullName, out var set) ? set.Select(user => SliceOf(module.GetType(user)?.Namespace) ?? "shared").Distinct(StringComparer.Ordinal).ToList() : []))
            .Where(type => type.slices.Count == 1 && type.slices[0] != "shared" && !StaysShared.ContainsKey(type.FullName))
            .Select(type => $"{type.FullName} is used only by {type.slices[0]}")];
    }

    /// <summary>
    /// Shared types one slice alone uses that stay where they are, each with why: a record decided
    /// where they live, or they share a documented region with what the shared part builds.
    /// </summary>
    private static readonly Dictionary<string, string> StaysShared = new(StringComparer.Ordinal)
    {
        ["TheYard.Api.Shared.Wire.VehiclePage"] = "ADR-076 keeps every named reply the endpoints answer with in Replies.cs, one file the API's description is read from",
        ["TheYard.Api.Shared.Wire.BidResult"] = "ADR-076 keeps every named reply the endpoints answer with in Replies.cs, one file the API's description is read from",
        ["TheYard.Api.Shared.Wire.TickResult"] = "ADR-076 keeps every named reply the endpoints answer with in Replies.cs, one file the API's description is read from",
        ["TheYard.Api.Shared.Wire.HealthReport"] = "ADR-076 keeps every named reply the endpoints answer with in Replies.cs, one file the API's description is read from",
        ["TheYard.Api.Shared.Wire.ForgotReply"] = "ADR-076 keeps every named reply the endpoints answer with in Replies.cs, one file the API's description is read from",
        ["TheYard.Api.Shared.Observability.StoreMetrics"] = "the store-ring region of AdminObservability.cs, beside the ring it sums, which the shared registration builds",
        ["TheYard.Api.Shared.Instance.Routes"] = "the routes region of Peer.cs, beside the peer reader the shared registration builds",
    };

    /// <summary>Every type of this module a type names: its base, its interfaces, its fields, its signatures and every operand of its bodies, the outermost type of each.</summary>
    private static IEnumerable<string> Named(Mono.Cecil.TypeDefinition type)
    {
        IEnumerable<Mono.Cecil.TypeReference> Expand(Mono.Cecil.TypeReference? reference) => reference switch
        {
            null => [],
            Mono.Cecil.GenericInstanceType generic => generic.GenericArguments.SelectMany(Expand).Prepend(generic.ElementType),
            Mono.Cecil.TypeSpecification spec => Expand(spec.ElementType),
            _ => [reference],
        };
        var references = new List<Mono.Cecil.TypeReference?> { type.BaseType };
        references.AddRange(type.Interfaces.Select(contract => contract.InterfaceType));
        references.AddRange(type.Fields.Select(field => field.FieldType));
        references.AddRange(type.Properties.Select(property => property.PropertyType));
        foreach (var method in type.Methods)
        {
            references.Add(method.ReturnType);
            references.AddRange(method.Parameters.Select(parameter => parameter.ParameterType));
            if (!method.HasBody)
            {
                continue;
            }
            references.AddRange(method.Body.Variables.Select(variable => variable.VariableType));
            foreach (var operand in method.Body.Instructions.Select(instruction => instruction.Operand))
            {
                references.Add(operand switch
                {
                    Mono.Cecil.TypeReference reference => reference,
                    Mono.Cecil.MemberReference member => member.DeclaringType,
                    _ => null,
                });
                if (operand is Mono.Cecil.MethodReference called)
                {
                    references.Add(called.ReturnType);
                    references.AddRange(called.Parameters.Select(parameter => parameter.ParameterType));
                }
            }
        }
        return references.SelectMany(Expand)
            .Where(reference => reference is Mono.Cecil.TypeDefinition definition && definition.Module == type.Module)
            .Select(reference =>
            {
                var outer = reference;
                while (outer.DeclaringType is { } declaring)
                {
                    outer = declaring;
                }
                return outer.FullName;
            })
            .Distinct(StringComparer.Ordinal);
    }

    [Fact]
    public void Rule_2_every_hosted_service_is_a_named_concern_with_one_constructor_and_no_container()
    {
        // A hosted service runs beside the requests for the life of the process; each is one concern,
        // named here with what it does, and takes what it needs by one constructor. A new one fails
        // here until it is named, and one that asks a container for its dependencies fails always.
        var services = Host.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(type))
            .ToList();
        var wrong = new List<string>();
        foreach (var service in services)
        {
            if (!HostedConcerns.ContainsKey(service.Name))
            {
                wrong.Add($"{service.FullName} is a hosted service not named in HostedConcerns");
            }
            var constructors = service.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (constructors.Length != 1)
            {
                wrong.Add($"{service.FullName} has {constructors.Length} public constructors");
            }
            if (constructors.SelectMany(ctor => ctor.GetParameters()).Any(parameter => parameter.ParameterType == typeof(IServiceProvider)))
            {
                wrong.Add($"{service.FullName} takes the service container");
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong) + $"{Environment.NewLine}hosted services found: {string.Join(", ", services.Select(type => type.Name).Order(StringComparer.Ordinal))}");
        Assert.DoesNotContain(HostedConcerns.Keys, name => services.All(type => type.Name != name));
    }

    /// <summary>Every hosted service the host runs, by name, with the one concern it carries.</summary>
    private static readonly Dictionary<string, string> HostedConcerns = new(StringComparer.Ordinal)
    {
        ["ActivityCollector"] = "keeps the visitor counts off the request path",
        ["CatalogueWarm"] = "warms each store's catalogue once at start",
        ["CloudRecorder"] = "reads and keeps the resource cards' minutes and hours",
        ["CostRecorder"] = "reads Cost Management once an hour and keeps the answer",
        ["ErrorRingKeeping"] = "keeps the error rings across a restart",
        ["HostedSiteStopper"] = "stops the guest sites with the host",
        ["HourlyReadings"] = "keeps the hourly readings",
        ["KeepWarm"] = "keeps the sites warm between visits",
        ["KeepWarmAtRoll"] = "warms both names after a roll",
        ["LinkOpenCollector"] = "keeps tagged-link opens off the request path",
        ["LogCollector"] = "keeps the log off the request path",
        ["MachineRecorder"] = "keeps each finished minute of the machine's samples",
        ["MachineSampler"] = "samples the container's machine",
        ["PageSweepAtRoll"] = "sweeps the served pages after a roll",
        ["PlanRecorder"] = "reads and keeps the plan card's minutes and hours",
        ["ScratchDatabaseCleanup"] = "deletes the scratch database at stop",
        ["StoreAnnouncement"] = "says which store each site serves at start",
        ["StoreSecondChance"] = "asks a store that did not come up again, for a window, and attaches it",
    };

    /// <summary>The namespace every slice sits under.</summary>
    private const string FeaturesNamespace = "TheYard.Api.Features";

    /// <summary>The slice a namespace belongs to: the folder under Features/ that holds a Module, or null outside Features.</summary>
    private static string? SliceOf(string? ns) =>
        ns is not null && ns.StartsWith(FeaturesNamespace + ".", StringComparison.Ordinal)
            && Host.GetType(ns + ".Module") is not null
            ? ns
            : null;

    [Fact]
    public void Rule_3_the_sites_and_the_stores_are_lookups_with_no_store_or_host_written_in()
    {
        // A third site or a third store is a setting (Shared/Wiring/SiteSettings.cs), so the two types
        // that decide which store and which site a request gets carry no store key and no name.
        string[] types = ["TheYard.Api.Shared.Instance.SiteHosts", "TheYard.Api.Shared.Instance.Site", "TheYard.Api.Shared.Stores.Backends", "TheYard.Api.Shared.Stores.CurrentBackend"];
        Assert.Empty(Literals(types, literal =>
            literal.Equals("sql", StringComparison.OrdinalIgnoreCase)
            || literal.Equals("cosmos", StringComparison.OrdinalIgnoreCase)
            || literal.Contains("stevenstout", StringComparison.OrdinalIgnoreCase)
            || literal.Contains("theyard", StringComparison.OrdinalIgnoreCase)
            || literal.Contains("theshed", StringComparison.OrdinalIgnoreCase)
            || literal.Contains(".example", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Rule_3_a_store_key_is_written_once_in_StoreKeys()
    {
        // A third store is a line in StoreKeys and its registration, not an edit in every file that
        // asked for a store by name. The source is read, not the IL, because a constant is copied
        // into every place that uses it. LiveSamples' "sql" is a code block's language, not a store.
        string root = Repo.Root();
        string[] allowed = ["StoreKeys.cs", "LiveSamples.cs"];
        var written = new[] { Path.Combine(root, "api", "TheYard.Api"), Path.Combine(root, "api", "TheYard.Application") }
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !allowed.Contains(Path.GetFileName(path)))
            .SelectMany(path => File.ReadAllLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => !entry.line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                && System.Text.RegularExpressions.Regex.IsMatch(entry.line, "\"(sql|cosmos)\""))
            .Select(entry => $"{Path.GetRelativePath(root, entry.path).Replace('\\', '/')}:{entry.index + 1} writes a store key: {entry.line.Trim()}")
            .ToList();
        Assert.True(written.Count == 0, string.Join(Environment.NewLine, written));
    }

    [Fact]
    public void Rule_4_a_backend_names_no_adapter_and_both_stores_answer_one_diagnostics_port()
    {
        var adapterTypes = new[] { "TheYard.Infrastructure", "TheYard.Infrastructure.Cosmos", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore.Identity" };
        // Every type a member names, however deep: a Func<IServiceProvider, IUserStore<YardUser>?> names
        // Infrastructure's user entity two generic levels down, and 1.0.4.0's one-level walk let it through
        // (the review of 1.0.4.0, finding 5).
        static IEnumerable<Type> Named(Type type) =>
            type.IsGenericType ? type.GetGenericArguments().SelectMany(Named).Prepend(type) : [type];
        bool Adapter(Type type) => Named(type).Any(named => adapterTypes.Any(ns => named.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true));
        var members = new List<string>();
        foreach (var holder in new[] { typeof(Backend), typeof(StoreAttachment) })
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            members.AddRange(holder.GetProperties(all).Where(property => Adapter(property.PropertyType)).Select(property => $"{holder.Name}.{property.Name} : {property.PropertyType.Name}"));
            members.AddRange(holder.GetFields(all).Where(field => !field.Name.Contains('<', StringComparison.Ordinal) && Adapter(field.FieldType)).Select(field => $"{holder.Name}.{field.Name} : {field.FieldType.Name}"));
            members.AddRange(holder.GetConstructors(all).SelectMany(ctor => ctor.GetParameters()).Where(parameter => Adapter(parameter.ParameterType)).Select(parameter => $"{holder.Name}({parameter.Name} : {parameter.ParameterType.Name})"));
            members.AddRange(holder.GetMethods(all).Where(method => !method.IsSpecialName).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType).Where(Adapter).Select(type => $"{holder.Name}.{method.Name} : {type.Name}")));
        }
        Assert.True(members.Count == 0, "Backend names an adapter: " + string.Join(", ", members));
        Assert.True(typeof(TheYard.Application.Shared.Stores.IStoreDiagnostics).IsAssignableFrom(typeof(TheYard.Infrastructure.Shared.Stores.RelationalDiagnostics)));
        Assert.True(typeof(TheYard.Application.Shared.Stores.IStoreDiagnostics).IsAssignableFrom(typeof(TheYard.Infrastructure.Cosmos.Shared.Stores.DocumentDiagnostics)));
    }

    [Fact]
    public void Rule_5_a_guest_site_stands_behind_one_interface_and_the_shed_is_one_implementation()
    {
        var sites = Host.GetTypes().Where(type => type.IsClass && typeof(IHostedSite).IsAssignableFrom(type)).ToList();
        Assert.Contains(sites, type => type.Name == "ShedSite");
        Assert.All(sites, type => Assert.True(type.IsSealed, $"{type.Name} should be sealed"));
        // Nothing else holds The Shed's application: the mount knows only the interface.
        Assert.Null(Host.GetType("TheYard.Api.ShedSlot"));
        Assert.Null(Host.GetType("TheYard.Api.ShedMount"));
    }

    /// <summary>Every string literal the named types' method bodies load that the test flags, with where it was found.</summary>
    private static List<string> Literals(string[] typeNames, Func<string, bool> flagged)
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(Host.Location);
        return [.. module.GetTypes()
            .Where(type => typeNames.Contains(Outermost(type).FullName))
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .SelectMany(method => method.Body.Instructions
                .Where(instruction => instruction.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && instruction.Operand is string literal && flagged(literal))
                .Select(instruction => $"{method.DeclaringType.FullName}.{method.Name} writes \"{instruction.Operand}\""))];
    }

    /// <summary>The type a nested or compiler-written type sits inside, all the way out.</summary>
    private static Mono.Cecil.TypeDefinition Outermost(Mono.Cecil.TypeDefinition type)
    {
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }
        return type;
    }

    [Fact]
    public void Rule_2_the_composition_bag_is_retired_and_startup_is_one_hosted_service_per_concern()
    {
        // The record every registration took whole, and the one method that read eleven fields off
        // it, are gone: each registration is handed what it needs by name, and what ran at startup
        // is a hosted service per concern, each taking its dependencies by constructor.
        Assert.Null(Host.GetType("TheYard.Api.YardComposition"));
        Assert.Null(Host.GetType("TheYard.Api.Startup"));
        string[] concerns = ["StoreAnnouncement", "ScratchDatabaseCleanup", "ErrorRingKeeping", "CatalogueWarm", "PageSweepAtRoll", "KeepWarmAtRoll", "HostedSiteStopper"];
        foreach (string concern in concerns)
        {
            var type = Host.GetType("TheYard.Api.Shared.Hosted." + concern);
            Assert.True(type is not null && typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(type), $"{concern} is not a hosted service");
        }
    }

    [Fact]
    public void The_slices_drawing_names_only_files_the_tree_has_and_the_rings_the_slices_reach()
    {
        // The drawing on the Hosting page is the record's picture. At 1.0.4.0 it drew the design
        // as planned, twenty-eight file names the tree never had and folders in rings that have
        // none (the review of 1.0.4.0, finding 4); the picture is a claim, and this holds it.
        string drawing = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "images", "vertical-slices.mjs"));
        var named = System.Text.RegularExpressions.Regex.Matches(drawing, @"[A-Za-z][A-Za-z0-9.]*\.cs").Select(match => match.Value).Distinct().ToList();
        var have = Directory.EnumerateFiles(Path.Combine(Repo.Root(), "api"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);
        var missing = named.Where(name => !have.Contains(name)).ToList();
        Assert.True(missing.Count == 0, "the drawing names files the tree does not have: " + string.Join(", ", missing));
        Assert.DoesNotContain("Features/Admin/Cloud/Sql", drawing, StringComparison.Ordinal);
    }

    /// <summary>
    /// The types Entity Framework maps stay at the relational adapter's root, each named: the migrations and their
    /// snapshot name every mapped type by its namespace, so moving one would read to the tools as a new table.
    /// </summary>
    private static readonly string[] MappedAtTheRoot =
    [
        "ActivityRows.cs", "BlogRows.cs", "CatalogueRows.cs", "CatalogueWords.cs", "Rows.cs", "SeedResult.cs", "YardUser.cs",
        "YardDbContext.cs",
    ];

    [Fact]
    public void Every_inner_ring_file_sits_in_a_slice_or_a_shared_concern_and_declares_its_folder()
    {
        // The rule the host has held since ADR-114, in the four inner rings (ADR: The onion cut into vertical slices,
        // the addendum of 1.0.4.46): a file is under Features/<Slice>/ or Shared/<Concern>/, and its namespace is its
        // folder, so a reader finds a type by its feature in every ring.
        var wrong = new List<string>();
        foreach (string ring in new[] { "TheYard.Domain", "TheYard.Application", "TheYard.Infrastructure", "TheYard.Infrastructure.Cosmos" })
        {
            string root = Path.Combine(Repo.Root(), "api", ring);
            foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                string relative = Path.GetRelativePath(root, path);
                string? folder = Path.GetDirectoryName(relative);
                if (string.IsNullOrEmpty(folder))
                {
                    if (!(ring == "TheYard.Infrastructure" && MappedAtTheRoot.Contains(relative, StringComparer.Ordinal)))
                    {
                        wrong.Add($"{ring}/{relative} sits at the ring's root");
                    }
                    continue;
                }
                string expected = ring + "." + folder.Replace(Path.DirectorySeparatorChar, '.');
                string declared = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), @"(?m)^namespace\s+([\w.]+)").Groups[1].Value;
                if (!folder.StartsWith("Features", StringComparison.Ordinal) && !folder.StartsWith("Shared", StringComparison.Ordinal))
                {
                    wrong.Add($"{ring}/{relative} is in neither Features/ nor Shared/");
                }
                else if (declared != expected)
                {
                    wrong.Add($"{ring}/{relative} declares {declared}, its folder says {expected}");
                }
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
}
// #endregion slice-tests
