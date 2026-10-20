using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TheYard.Tests;

// #region literal-scan
/// <summary>
/// No name of this deployment is written in the six projects' code (ADR: The onion cut into vertical
/// slices, the facts the Admin tab reads). Every string constant the compiler emitted is read back
/// from the built assemblies, so a name in an interpolation, a const or a default counts the same,
/// and none may carry the resource group's suffix, the web app's hash, the edge's project, the
/// public domain or the identity's and subscription's ids: those are configuration
/// (appsettings.json and the web app's settings, Shared/AzureResources.cs).
/// </summary>
public sealed class LiteralScanTests
{
    private static readonly string[] Projects =
        ["TheYard.Data", "TheYard.Domain", "TheYard.Application", "TheYard.Infrastructure", "TheYard.Infrastructure.Cosmos", "TheYard.Api"];

    /// <summary>The pieces of this deployment's names, each of which only configuration may carry.</summary>
    private static readonly string[] DeploymentNames =
        ["theyard-ss", "zmnetj67bn5h2", "theyard-edge", "stevenstout.biz", "2888a6ca-be1c-46a5-a1de-c666b1d193e5", "df3b718c-6d99-4904-8102-6f865941f640"];

    /// <summary>The literals allowed to carry one anyway. Empty, and a test below keeps it so.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal);

    [Fact]
    public void No_name_of_this_deployment_is_written_in_the_code()
    {
        var found = new List<string>();
        foreach (string project in Projects)
        {
            string path = Path.Combine(AppContext.BaseDirectory, project + ".dll");
            using var module = ModuleDefinition.ReadModule(path);
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    foreach (var instruction in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr))
                    {
                        string literal = (string)instruction.Operand;
                        if (!Allowed.Contains(literal) && DeploymentNames.Any(name => literal.Contains(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            found.Add($"{project}: {type.FullName}.{method.Name} writes \"{literal}\"");
                        }
                    }
                }
                foreach (var field in type.Fields.Where(f => f.HasConstant && f.Constant is string))
                {
                    string literal = (string)field.Constant;
                    if (!Allowed.Contains(literal) && DeploymentNames.Any(name => literal.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        found.Add($"{project}: {type.FullName}.{field.Name} is \"{literal}\"");
                    }
                }
            }
        }
        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    [Fact]
    public void The_allow_list_is_empty() => Assert.Empty(Allowed);

    [Fact]
    public void The_names_are_in_configuration_instead()
    {
        string settings = File.ReadAllText(Path.Combine(Repo.Root(), "api", "TheYard.Api", "appsettings.json"));
        Assert.All(DeploymentNames, name => Assert.Contains(name, settings, StringComparison.OrdinalIgnoreCase));
    }
}
// #endregion literal-scan
