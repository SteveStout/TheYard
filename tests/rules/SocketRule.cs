using System.Reflection;
using Mono.Cecil;

namespace TheYard.Rules;

// #region socket-rule
/// <summary>
/// One rule, one body, compiled into both test projects by a link (ADR-075, the addendum of 1.0.4.70): only
/// Infrastructure opens a socket. A type that names an HTTP client, a handler or a client factory, in a field, a
/// signature or a method body, is a door to the network no adapter test sees. Each suite hands it the assemblies
/// that must hold no door and the types its record lets keep one.
/// </summary>
public static class SocketRule
{
    /// <summary>The types that open a socket or make something that does.</summary>
    public static readonly string[] Doors =
    [
        "System.Net.Http.HttpClient",
        "System.Net.Http.HttpMessageHandler",
        "System.Net.Http.SocketsHttpHandler",
        "System.Net.Http.DelegatingHandler",
        "System.Net.Http.IHttpClientFactory",
    ];

    /// <summary>Every type in the assembly, but those <paramref name="allowed"/> lets through, that names a door, as a line naming both.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <param name="allowed">Types left out by their outermost type's name, with the reason their caller gives.</param>
    public static List<string> Opens(Assembly assembly, Func<string, bool>? allowed = null)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        var wrong = new List<string>();
        foreach (var type in module.GetTypes())
        {
            var outer = type;
            while (outer.DeclaringType is { } declaring)
            {
                outer = declaring;
            }
            if (allowed?.Invoke(outer.Name) == true || outer.Name.StartsWith('<'))
            {
                continue;
            }
            foreach (string door in Named(type).Where(name => Doors.Contains(name, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal))
            {
                wrong.Add($"{outer.FullName} names {door}");
            }
        }
        return [.. wrong.Distinct(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> Named(TypeDefinition type)
    {
        var references = new List<TypeReference?> { type.BaseType };
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
                    TypeReference reference => reference,
                    MemberReference member => member.DeclaringType,
                    _ => null,
                });
            }
        }
        return references.OfType<TypeReference>().SelectMany(Expand).Select(reference => reference.FullName);
    }

    private static IEnumerable<TypeReference> Expand(TypeReference reference) => reference switch
    {
        GenericInstanceType generic => generic.GenericArguments.SelectMany(Expand).Prepend(generic.ElementType),
        TypeSpecification spec => Expand(spec.ElementType),
        _ => [reference],
    };
}
// #endregion socket-rule
