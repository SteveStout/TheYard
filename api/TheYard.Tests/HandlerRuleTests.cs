using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TheYard.Tests;

// #region handler-rules
/// <summary>
/// Rule 7 of the slices (ADR: The onion cut into vertical slices): one handler per request. Every
/// route a ruled slice maps asks exactly one Application handler; every handler is tested with
/// fakes at its ports, with no host; and every status code a route declares has an integration
/// case that says it covers it. A slice joins <see cref="Ruled"/> in the commit that gives its
/// routes their handlers.
/// </summary>
public sealed class HandlerRuleTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>Every slice is ruled: a route under Features/ answers through one handler or is named in <see cref="Exempt"/> with why.</summary>
    private const string Slices = "TheYard.Api.Features.";

    /// <summary>
    /// The routes that read the host's own state, not a use case, each with its reason: the health
    /// report and the Admin tab's operations read every backend's diagnostics and the process's own
    /// rings, and the documents are files the host serves. A route here asks no handler and is out of
    /// rule 7 whole, the case per status included, until its slice gets handlers; the list is that
    /// rung's work, by name. Keyed by the answering method's declaring type and name.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        ["DocumentationEndpoints.Picture"] = "a file the host serves from the repository",
        ["DocumentationEndpoints.Diagram"] = "a drawing the host renders from the repository",
        ["DocumentationEndpoints.About"] = "a page the host renders from its own words",
        ["DocumentationEndpoints.Bicep"] = "a file the host serves from the repository",
        ["DocumentationEndpoints.Resume"] = "a file the host serves from the repository",
        ["DocumentationEndpoints.ResumeAtItsAddress"] = "a file the host serves from the repository",
        ["RecommendationEndpoints.Read"] = "a file the host serves from the repository",
        ["HealthEndpoints.Version"] = "the build the host is",
        ["HealthEndpoints.Liveness"] = "the process answering",
        ["HealthEndpoints.Readiness"] = "every backend's readiness",
        ["HealthEndpoints.Health"] = "every backend's health and the host's own",
        ["PulseEndpoints.Read"] = "the host's own pulse reader",
        ["ErrorEndpoints.Recent"] = "the host's error rings",
        ["ErrorEndpoints.BrowserReport"] = "a browser's error into the host's rings",
        ["ErrorEndpoints.SelfTest"] = "the host throwing on purpose, behind a cooldown",
    };

    /// <summary>Parameters a route may take beside its one handler, by type name, each with its reason; anything else from this codebase is a second dependency.</summary>
    private static readonly Dictionary<string, string> Beside = new(StringComparer.Ordinal)
    {
        ["TokenIssuer"] = "writes the session cookie, which is the host's and not a use case's",
        ["CurrentBackend"] = "names the request's store to the handler, the request's own fact",
        ["SiteHosts"] = "the names this process answers to, for a link's origin",
        ["AdminKey"] = "the operator's key, checked before the handler runs",
        ["AdminSettings"] = "whether the visitor rows are served at all, read with the key before the handler runs",
        ["VisitorTokens"] = "the day's keyed hash a blog read and its events are kept under, the request's own fact",
        ["Credentials"] = "the register and login forms' body",
        ["VehicleQueryParams"] = "the list's query string, bound as parameters",
    };

    /// <summary>The handlers' home in the Application ring.</summary>
    private const string Handlers = "TheYard.Application.Features";

    /// <summary>Every route a ruled slice maps: its method, its pattern, the method that answers it and the status codes it declares.</summary>
    private List<(string Method, string Route, MethodInfo Answer, int[] Statuses)> RuledRoutes()
    {
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(endpoint => (Endpoint: endpoint, Answer: endpoint.Metadata.GetMetadata<MethodInfo>()))
            .Where(pair => pair.Answer?.DeclaringType?.Namespace is { } ns && ns.StartsWith(Slices, StringComparison.Ordinal))
            .SelectMany(pair => (pair.Endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Select(method => (
                method,
                "/" + pair.Endpoint.RoutePattern.RawText!.TrimStart('/'),
                pair.Answer!,
                pair.Endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Select(produces => produces.StatusCode).Distinct().Order().ToArray())))
            .ToList();
        Assert.NotEmpty(routes);
        return routes;
    }

    [Fact]
    public void Every_route_in_a_ruled_slice_asks_exactly_one_handler()
    {
        // A handler is a type in the Application ring's Features; any other parameter this codebase
        // defines (not a request body, not a framework type) is a second dependency unless Beside names it.
        static bool Ours(Type type) => type.Namespace?.StartsWith("TheYard", StringComparison.Ordinal) == true;
        static bool Body(Type type) => type.Name.EndsWith("Request", StringComparison.Ordinal) || type.Name.EndsWith("Body", StringComparison.Ordinal);
        var wrong = RuledRoutes()
            .Where(route => !Exempt.ContainsKey($"{route.Answer.DeclaringType!.Name}.{route.Answer.Name}"))
            .Select(route => (route,
                handlers: route.Answer.GetParameters().Count(parameter => parameter.ParameterType.Namespace?.StartsWith(Handlers, StringComparison.Ordinal) == true),
                others: route.Answer.GetParameters().Select(parameter => parameter.ParameterType)
                    .Where(type => Ours(type) && type.Namespace?.StartsWith(Handlers, StringComparison.Ordinal) != true && !Body(type) && !Beside.ContainsKey(type.Name))
                    .Select(type => type.Name).ToList()))
            .Where(pair => pair.handlers != 1 || pair.others.Count > 0)
            .Select(pair => $"{pair.route.Method} {pair.route.Route} ({pair.route.Answer.DeclaringType!.Name}.{pair.route.Answer.Name}) asks {pair.handlers} handlers{(pair.others.Count > 0 ? " and takes " + string.Join(", ", pair.others) : "")}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_status_a_ruled_route_declares_has_an_integration_case()
    {
        // A [Covers] counts only when the method's body asserts that status: the code as a number or
        // an HttpStatusCode, loaded in the same body as a call into Xunit's Assert.
        using var tests = ModuleDefinition.ReadModule(typeof(HandlerRuleTests).Assembly.Location);
        var asserted = tests.GetTypes()
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .GroupBy(method => Outermost(method.DeclaringType).FullName + "." + StateMachineOwner(method))
            .ToDictionary(group => group.Key, group => group.SelectMany(method => method.Body.Instructions).ToList(), StringComparer.Ordinal);
        var covered = typeof(HandlerRuleTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetCustomAttributes<CoversAttribute>().Select(covers => (method, covers)))
            .Where(pair => asserted.TryGetValue($"{pair.method.DeclaringType!.FullName}.{pair.method.Name}", out var body) && AssertsStatus(body, pair.covers.Status))
            .Select(pair => (pair.covers.Method, pair.covers.Route, pair.covers.Status))
            .ToHashSet();
        var missing = RuledRoutes()
            .Where(route => !Exempt.ContainsKey($"{route.Answer.DeclaringType!.Name}.{route.Answer.Name}"))
            .SelectMany(route => route.Statuses.Select(status => (route.Method, route.Route, status)))
            .Where(case_ => !covered.Contains(case_))
            .Select(case_ => $"{case_.Method} {case_.Route} {case_.status}")
            .ToList();
        Assert.True(missing.Count == 0, "no integration case covers: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_handler_is_tested_with_fakes_and_no_host()
    {
        var handlers = typeof(TheYard.Application.StandingReader).Assembly.GetTypes()
            .Where(type => type.IsClass && type.Namespace?.StartsWith(Handlers, StringComparison.Ordinal) == true && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .ToList();
        Assert.NotEmpty(handlers);

        using var tests = ModuleDefinition.ReadModule(typeof(HandlerRuleTests).Assembly.Location);
        // Constructed in a test that asserts: the same method (or its async state machine) calls Xunit's Assert.
        var constructedWithoutAHost = tests.GetTypes()
            .Where(type => !UsesAHost(type))
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .GroupBy(method => Outermost(method.DeclaringType).FullName + "." + StateMachineOwner(method))
            .Where(group => group.SelectMany(method => method.Body.Instructions).Any(IsAssert))
            .SelectMany(group => group.SelectMany(method => method.Body.Instructions)
                .Where(instruction => instruction.OpCode == OpCodes.Newobj)
                .Select(instruction => ((MethodReference)instruction.Operand).DeclaringType.FullName))
            .ToHashSet(StringComparer.Ordinal);
        var untested = handlers.Where(handler => !constructedWithoutAHost.Contains(handler)).ToList();
        Assert.True(untested.Count == 0, "no host-free test constructs: " + string.Join(", ", untested));
    }

    /// <summary>A call into Xunit's Assert, or into Problem.Is, which asserts a failure's status and shape.</summary>
    private static bool IsAssert(Instruction instruction) =>
        instruction.Operand is MethodReference called && called.DeclaringType.FullName is "Xunit.Assert" or "TheYard.Tests.Problem";

    /// <summary>True when the body calls Assert and loads the status as a number or as an HttpStatusCode value.</summary>
    private static bool AssertsStatus(List<Instruction> body, int status) =>
        body.Any(IsAssert) && body.Any(instruction =>
            (instruction.OpCode == OpCodes.Ldc_I4 || instruction.OpCode == OpCodes.Ldc_I4_S) && Convert.ToInt32(instruction.Operand, System.Globalization.CultureInfo.InvariantCulture) == status);

    /// <summary>The source method a compiler-written state machine or lambda belongs to, by its name ("&lt;Name&gt;d__3" is Name); a plain method is itself.</summary>
    private static string StateMachineOwner(MethodDefinition method)
    {
        for (var type = method.DeclaringType; type.DeclaringType is not null; type = type.DeclaringType)
        {
            var match = System.Text.RegularExpressions.Regex.Match(type.Name, @"^<([^>]+)>d__");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        var lambda = System.Text.RegularExpressions.Regex.Match(method.Name, @"^<([^>]+)>b__");
        return lambda.Success ? lambda.Groups[1].Value : method.Name;
    }

    /// <summary>The type a nested or compiler-written type sits inside, all the way out.</summary>
    private static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }
        return type;
    }

    /// <summary>True for a test class, or a type inside one, that boots the host.</summary>
    private static bool UsesAHost(TypeDefinition type)
    {
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }
        return type.Interfaces.Any(contract => contract.InterfaceType.FullName.Contains("WebApplicationFactory", StringComparison.Ordinal))
            || type.Methods.Any(method => method.Parameters.Any(parameter => parameter.ParameterType.FullName.Contains("WebApplicationFactory", StringComparison.Ordinal)))
            || type.Fields.Any(field => field.FieldType.FullName.Contains("WebApplicationFactory", StringComparison.Ordinal));
    }
}
// #endregion handler-rules
