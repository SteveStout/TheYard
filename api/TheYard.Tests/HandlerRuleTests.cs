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
    /// <summary>The slices whose routes have their handlers, by the namespace of their folder.</summary>
    private static readonly string[] Ruled = ["TheYard.Api.Features.Bids", "TheYard.Api.Features.Vehicles", "TheYard.Api.Features.Accounts"];

    /// <summary>The handlers' home in the Application ring.</summary>
    private const string Handlers = "TheYard.Application.Features";

    /// <summary>Every route a ruled slice maps: its method, its pattern, the method that answers it and the status codes it declares.</summary>
    private List<(string Method, string Route, MethodInfo Answer, int[] Statuses)> RuledRoutes()
    {
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(endpoint => (Endpoint: endpoint, Answer: endpoint.Metadata.GetMetadata<MethodInfo>()))
            .Where(pair => pair.Answer?.DeclaringType?.Namespace is { } ns && Ruled.Contains(ns))
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
        var wrong = RuledRoutes()
            .Select(route => (route, handlers: route.Answer.GetParameters().Count(parameter => parameter.ParameterType.Namespace?.StartsWith(Handlers, StringComparison.Ordinal) == true)))
            .Where(pair => pair.handlers != 1)
            .Select(pair => $"{pair.route.Method} {pair.route.Route} asks {pair.handlers} handlers")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_status_a_ruled_route_declares_has_an_integration_case()
    {
        var covered = typeof(HandlerRuleTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetCustomAttributes<CoversAttribute>())
            .Select(covers => (covers.Method, covers.Route, covers.Status))
            .ToHashSet();
        var missing = RuledRoutes()
            .SelectMany(route => route.Statuses.Select(status => (route.Method, route.Route, status)))
            .Where(case_ => !covered.Contains(case_))
            .Select(case_ => $"{case_.Method} {case_.Route} {case_.status}")
            .ToList();
        Assert.True(missing.Count == 0, "no integration case covers: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_handler_is_tested_with_fakes_and_no_host()
    {
        var handlers = typeof(TheYard.Application.Auction).Assembly.GetTypes()
            .Where(type => type.IsClass && type.Namespace?.StartsWith(Handlers, StringComparison.Ordinal) == true && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .ToList();
        Assert.NotEmpty(handlers);

        using var tests = ModuleDefinition.ReadModule(typeof(HandlerRuleTests).Assembly.Location);
        var constructedWithoutAHost = tests.GetTypes()
            .Where(type => !UsesAHost(type))
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .SelectMany(method => method.Body.Instructions
                .Where(instruction => instruction.OpCode == OpCodes.Newobj)
                .Select(instruction => ((MethodReference)instruction.Operand).DeclaringType.FullName))
            .ToHashSet(StringComparer.Ordinal);
        var untested = handlers.Where(handler => !constructedWithoutAHost.Contains(handler)).ToList();
        Assert.True(untested.Count == 0, "no host-free test constructs: " + string.Join(", ", untested));
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
