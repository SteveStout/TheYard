using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The roll words on the first screen and in the review stops (ADR-095, profile1110-f1). The first screen said "rolled
/// blue and green, with no downtime" beside a button that opens ADR-095, which says the second copy of each site that
/// blue-green needs was not created. The claim a manager reads first may not be refuted by the record one click away,
/// so while the record says that, neither the first screen's roll readout nor any review stop names blue-green or no
/// downtime.
/// </summary>
public sealed class RollWordsTests
{
    private static readonly Regex Unbuilt = new("blue-green|blue and green|no downtime|zero downtime", RegexOptions.IgnoreCase);

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Repo.Root(), .. parts]));

    [Fact]
    public void Adr_095_still_says_the_second_copy_was_not_created()
    {
        Assert.Contains("was not created", Read("docs", "decisions", "ADR-095-blue-green-measured-and-held.md"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src", "components", "landing", "PulseBand", "PulseBand.tsx")]
    [InlineData("src", "lib", "reviewStops.ts")]
    public void The_first_screen_and_the_review_stops_never_call_a_roll_blue_green(params string[] path)
    {
        Assert.DoesNotMatch(Unbuilt, Read(path));
    }
}
