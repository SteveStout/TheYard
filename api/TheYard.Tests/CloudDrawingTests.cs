using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The Cloud infrastructure drawing names every card the rail files under it (ADR: One frame for every
/// resource, the addendum on the storage cards): the plan and each key of CLOUD_RESOURCES in
/// src/lib/workbench.ts, so a card added there and not drawn fails here, as the share and the blob once would have.
/// </summary>
public sealed partial class CloudDrawingTests
{
    [Fact]
    public void The_drawing_names_every_cloud_card_the_rail_lists()
    {
        string workbench = File.ReadAllText(Path.Combine(Repo.Root(), "src", "lib", "workbench.ts"));
        string list = workbench[workbench.IndexOf("export const CLOUD_RESOURCES = [", StringComparison.Ordinal)..];
        list = list[..list.IndexOf(']', StringComparison.Ordinal)];
        string[] keys = ["plan", .. Key().Matches(list).Select(m => m.Groups[1].Value)];
        string drawing = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "images", "cloud-infrastructure.svg"));

        Assert.Contains("share", keys);
        Assert.All(keys, key => Assert.Contains($"class=\"title\">{key}</text>", drawing, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"'([a-z]+)'")]
    private static partial Regex Key();
}
