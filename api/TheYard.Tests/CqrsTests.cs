using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Application.Shared;

namespace TheYard.Tests;

// #region cqrs-tests
/// <summary>
/// A request is a query or a command (ADR-115). A request is a record named <c>&lt;Verb&gt;&lt;Noun&gt;Query</c> or
/// <c>&lt;Verb&gt;&lt;Noun&gt;Command</c> in its slice's Application folder, with exactly one handler of the same name,
/// and every handler is one of the two interfaces in Shared/Requests.cs. A query handler is handed nothing that
/// writes: no parameter of its constructor is a store or a writer, only a reader. There is no bus: the slice's
/// Module maps a route to its one handler, and the route takes that handler and nothing else this codebase defines.
/// </summary>
public sealed class CqrsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The Application ring, where every request and its handler live.</summary>
    private static readonly Assembly Application = typeof(IQueryHandler<,>).Assembly;

    /// <summary>The slices' namespace in the Application ring.</summary>
    private const string Features = "TheYard.Application.Features";

    /// <summary>Every request in the slices whose name ends in this word: Query or Command.</summary>
    private static List<Type> Requests(string word) =>
        [.. Application.GetTypes().Where(type => type.IsClass && type.Namespace?.StartsWith(Features, StringComparison.Ordinal) == true && type.Name.EndsWith(word, StringComparison.Ordinal))];

    /// <summary>Every class in the Application ring's root or its slices whose name ends in Service.</summary>
    private static List<string> Services() =>
        [.. Application.GetTypes()
            .Where(type => type.IsClass && !type.Name.StartsWith('<') && type.Name.EndsWith("Service", StringComparison.Ordinal)
                && (type.Namespace == "TheYard.Application" || type.Namespace?.StartsWith(Features, StringComparison.Ordinal) == true))
            .Select(type => type.Name)];

    /// <summary>Every handler in the Application ring: a class whose name ends in Handler.</summary>
    private static List<Type> HandlerTypes() =>
        [.. Application.GetTypes().Where(type => type.IsClass && !type.IsAbstract && type.Name.EndsWith("Handler", StringComparison.Ordinal) && type.Namespace?.StartsWith(Features, StringComparison.Ordinal) == true)];

    /// <summary>The query or command interfaces a type implements, with the request each names.</summary>
    private static List<(Type Contract, Type Request, bool Query)> Contracts(Type handler) =>
        [.. handler.GetInterfaces()
            .Where(contract => contract.IsGenericType && (contract.GetGenericTypeDefinition() == typeof(IQueryHandler<,>) || contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .Select(contract => (contract, contract.GetGenericArguments()[0], contract.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))];

    [Fact]
    public void Every_handler_is_exactly_one_query_or_command_handler_named_for_its_request()
    {
        var handlers = HandlerTypes();
        Assert.True(handlers.Count >= 27, $"only {handlers.Count} handlers were found");
        var wrong = new List<string>();
        foreach (var handler in handlers)
        {
            var contracts = Contracts(handler);
            if (contracts.Count != 1)
            {
                wrong.Add($"{handler.Name} implements {contracts.Count} of IQueryHandler and ICommandHandler; it implements exactly one");
                continue;
            }
            var (_, request, query) = contracts[0];
            string expected = handler.Name[..^"Handler".Length] + (query ? "Query" : "Command");
            if (request.Name != expected)
            {
                wrong.Add($"{handler.Name} handles {request.Name}; its request is named {expected}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_query_and_command_has_exactly_one_handler()
    {
        var requests = Requests("Query").Concat(Requests("Command")).ToList();
        Assert.True(requests.Count >= 27, $"only {requests.Count} requests were found");
        var handled = HandlerTypes().SelectMany(Contracts).GroupBy(contract => contract.Request).ToDictionary(group => group.Key, group => group.ToList());
        var wrong = new List<string>();
        foreach (var request in requests)
        {
            int count = handled.TryGetValue(request, out var found) ? found.Count : 0;
            if (count != 1)
            {
                wrong.Add($"{request.Name} has {count} handlers; it has exactly one");
            }
            else if (request.Name.EndsWith("Query", StringComparison.Ordinal) != found![0].Query)
            {
                wrong.Add($"{request.Name} is handled as a {(found[0].Query ? "query" : "command")}; its name says otherwise");
            }
            if (!request.IsSealed)
            {
                wrong.Add($"{request.Name} is not sealed");
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void A_query_handler_is_handed_no_store_and_no_writer()
    {
        // Interface Segregation as a rule: a query reads, so the ports it takes end in Reader or Catalogue,
        // or are none of the two writing kinds; a store or a writer handed to a query is a write it could make.
        static string Bare(Type type) => (Nullable.GetUnderlyingType(type) ?? type).Name;
        var wrong = HandlerTypes()
            .Where(handler => Contracts(handler).Any(contract => contract.Query))
            .SelectMany(handler => handler.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => Bare(parameter.ParameterType).EndsWith("Store", StringComparison.Ordinal) || Bare(parameter.ParameterType).EndsWith("Writer", StringComparison.Ordinal))
                .Select(parameter => $"{handler.Name} takes {Bare(parameter.ParameterType)} {parameter.Name}; a query takes the reader"))
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void No_type_in_the_Application_ring_s_slices_or_root_is_a_service()
    {
        // A service is a bag of use cases behind one name; a request has one handler instead. BidService,
        // InventoryService and MarketService were the last three, and Auction composed them: they are
        // BidBook, Stock and Room behind a reading and a writing interface each, and the handlers.
        var services = Services();
        Assert.True(services.Count == 0, "types named *Service: " + string.Join(", ", services));
    }

    [Fact]
    public void Every_route_that_asks_a_handler_asks_a_query_or_a_command_handler()
    {
        // HandlerRuleTests holds a route to one handler; this holds that handler to one of the two shapes, so a
        // route can never ask a bag of use cases. The routes HandlerRuleTests names as exempt read the host's own
        // state and ask no handler yet; the Admin reads among them become queries in ADR-115's fourth rung.
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.Metadata.GetMetadata<MethodInfo>())
            .OfType<MethodInfo>()
            .Where(answer => answer.DeclaringType?.Namespace?.StartsWith("TheYard.Api.Features.", StringComparison.Ordinal) == true)
            .ToList();
        Assert.NotEmpty(routes);
        var wrong = routes
            .SelectMany(answer => answer.GetParameters()
                .Where(parameter => parameter.ParameterType.Namespace?.StartsWith(Features, StringComparison.Ordinal) == true && parameter.ParameterType.Name.EndsWith("Handler", StringComparison.Ordinal))
                .Where(parameter => Contracts(parameter.ParameterType).Count != 1)
                .Select(parameter => $"{answer.DeclaringType!.Name}.{answer.Name} asks {parameter.ParameterType.Name}, which is not a query or a command handler"))
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void The_practices_page_quotes_the_counts_this_build_has()
    {
        // A number in prose goes stale silently, so the page's table is read back against the build, the
        // way SealedByDefaultTests reads SEALED.md.
        string page = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "best-practices", "CQRS.md"));
        var counted = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["queries"] = Requests("Query").Count,
            ["commands"] = Requests("Command").Count,
            ["handlers"] = HandlerTypes().Count,
            ["services"] = Services().Count,
        };
        var wrong = new List<string>();
        foreach (var (shape, actual) in counted)
        {
            var quoted = Regex.Match(page, $@"^\| {shape} \| (\d+) \|", RegexOptions.Multiline);
            if (!quoted.Success)
            {
                wrong.Add($"docs/best-practices/CQRS.md has no row for {shape}");
            }
            else if (int.Parse(quoted.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) != actual)
            {
                wrong.Add($"docs/best-practices/CQRS.md says {quoted.Groups[1].Value} {shape} and this build has {actual}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
}
// #endregion cqrs-tests
