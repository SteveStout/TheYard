using System.Reflection;
using Mono.Cecil;

namespace TheYard.Rules;

// #region slice-rule
/// <summary>
/// Rule 1 of the slices across the rings (ADR-107), one body compiled into both test projects by a link: a type under
/// &lt;Ring&gt;.Features.&lt;Slice&gt; uses only that slice's types and the shared ones in any ring, and a shared type, under
/// &lt;Ring&gt;.Shared or the ring's root, uses no slice at all. A slice is its name under Features in every ring. Read
/// from the compiled assemblies: base types, interfaces, fields, signatures and every type a method body names.
/// </summary>
public static class SliceRule
{
    /// <summary>Every use of another slice's type, or of a slice's type from shared code, as a line naming both.</summary>
    /// <param name="rings">The process's ring assemblies.</param>
    /// <param name="exempt">Types left out by full name, with the reason their caller gives.</param>
    public static List<string> Crossings(IReadOnlyList<Assembly> rings, Func<string, bool>? exempt = null)
    {
        string[] roots = [.. rings.Select(ring => ring.GetName().Name!)];
        string? SliceOf(string? ns) => roots
            .Select(root => root + ".Features.")
            .Where(prefix => ns is not null && ns.StartsWith(prefix, StringComparison.Ordinal))
            .Select(prefix => ns![prefix.Length..].Split('.')[0])
            .FirstOrDefault();
        var wrong = new List<string>();
        foreach (var ring in rings)
        {
            using var module = ModuleDefinition.ReadModule(ring.Location);
            foreach (var type in module.GetTypes())
            {
                var outer = Outermost(type);
                if (outer.Name.StartsWith('<') || exempt?.Invoke(outer.FullName) == true)
                {
                    continue;
                }
                string? mine = SliceOf(outer.Namespace);
                foreach (var used in Named(type).Select(Outermost).DistinctBy(reference => reference.FullName))
                {
                    if (!roots.Any(root => used.Namespace == root || used.Namespace.StartsWith(root + ".", StringComparison.Ordinal)))
                    {
                        continue;
                    }
                    string? theirs = SliceOf(used.Namespace);
                    if (theirs is not null && theirs != mine)
                    {
                        wrong.Add(mine is null
                            ? $"{outer.FullName} is shared and uses {used.FullName} of slice {theirs}"
                            : $"{outer.FullName} (slice {mine}) uses {used.FullName} of slice {theirs}");
                    }
                }
            }
        }
        return [.. wrong.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static TypeReference Outermost(TypeReference type)
    {
        var outer = type;
        while (outer.DeclaringType is { } declaring)
        {
            outer = declaring;
        }
        return outer;
    }

    private static IEnumerable<TypeReference> Named(TypeDefinition type)
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
                if (operand is MethodReference called)
                {
                    references.Add(called.ReturnType);
                    references.AddRange(called.Parameters.Select(parameter => parameter.ParameterType));
                }
            }
        }
        return references.OfType<TypeReference>().SelectMany(Expand).Where(reference => reference is not GenericParameter);
    }

    private static IEnumerable<TypeReference> Expand(TypeReference reference) => reference switch
    {
        GenericInstanceType generic => generic.GenericArguments.SelectMany(Expand).Prepend(generic.ElementType),
        TypeSpecification spec => Expand(spec.ElementType),
        _ => [reference],
    };
}
// #endregion slice-rule
