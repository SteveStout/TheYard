using System.Reflection;

namespace TheYard.Api;

/// <summary>Every slice's module in this host, found once and always in the same order.</summary>
public static class Modules
{
    /// <summary>
    /// One of every concrete <see cref="IModule"/> in this assembly, ordered by its namespace, which
    /// is the slice's folder, so the order routes are mapped in does not depend on the compiler.
    /// </summary>
    public static IReadOnlyList<IModule> InThisHost() =>
        [.. Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IModule)Activator.CreateInstance(type)!)];
}
