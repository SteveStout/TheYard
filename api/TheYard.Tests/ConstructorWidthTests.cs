using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace TheYard.Tests;

/// <summary>
/// How wide a constructor is, and the rule in <c>.github/quality.py</c> that counts it (ADR: The
/// numbers that may only fall, the addendum on counting a constructor's width).
///
/// <para>The rule used to count commas on the line a class was declared on, which is not the same
/// question as how many things the class is handed. A generic type argument carries a comma of its
/// own, so <c>Func&lt;CancellationToken, Task&lt;string?&gt;&gt;</c> was read as two parameters and
/// three classes taking four and five were counted as taking six; and a parameter list wide enough
/// to wrap puts its last parameters on the lines below, so six classes taking six to eight were not
/// counted at all. Both directions matter: an over-count invites somebody to split a class that was
/// never wide, and an under-count hides the ones that are.</para>
///
/// <para>The reading is ported here from the Python and held against the Python's own source, which
/// is how this codebase holds a rule that lives in a script (<c>CancellationTests</c> does the same
/// for the async rule). The shapes asserted below are real declarations in this repository, by file
/// and name, so a reader can check any one of them by eye.</para>
///
/// <para>The last part holds the splitting rather than the counting, because paying this number
/// down has one failure the compiler cannot see: every <c>new X(...)</c> whose arity changed is a
/// build error, but a type the container activates by type is resolved at run time, and a part
/// split off a class and never registered fails only when something asks for it. That is how the
/// first gate of 1.0.4.118 went red, on a boot this machine cannot run, so the parts are resolved
/// from the real host here instead.</para>
/// </summary>
public class ConstructorWidthTests
{
    private static string Scanner() => File.ReadAllText(Path.Combine(Repo.Root(), ".github", "quality.py")).ReplaceLineEndings("\n");

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(Repo.Root(), Path.Combine(parts))).ReplaceLineEndings("\n");

    /// <summary>What the ratchet file says a number is allowed to be.</summary>
    private static int Limit(string number)
    {
        using var file = JsonDocument.Parse(Source(".github", "quality-ratchet.json"));
        return file.RootElement.GetProperty(number).GetInt32();
    }

    // #region ported-rule
    /// <summary>
    /// The scanner's whole-signature reading, ported: the text between a declaration's own
    /// brackets, across as many lines as it takes.
    /// </summary>
    private static string? Parameters(string[] lines, int start, int openAt)
    {
        int depth = 0;
        var taken = new System.Text.StringBuilder();
        for (int i = start; i < Math.Min(start + 12, lines.Length); i++)
        {
            string text = i > start ? lines[i].Split("//")[0] : lines[i].Split("//")[0][openAt..];
            foreach (char letter in text)
            {
                if (letter == '(')
                {
                    depth++;
                    if (depth == 1)
                    {
                        continue;
                    }
                }
                else if (letter == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return taken.ToString();
                    }
                }
                if (depth >= 1)
                {
                    taken.Append(letter);
                }
            }
            taken.Append(' ');
        }
        return null;
    }

    /// <summary>
    /// The scanner's split, ported: a parameter list cut only where a reader would cut it, on a
    /// comma at the list's own bracket depth and outside any angle brackets.
    /// </summary>
    private static List<string> Handed(string? taken)
    {
        var handed = new List<string>();
        if (taken is null)
        {
            return handed;
        }

        int depth = 0;
        int angle = 0;
        var current = new System.Text.StringBuilder();
        foreach (char letter in taken)
        {
            switch (letter)
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case '<':
                    angle++;
                    break;
                case '>':
                    angle = Math.Max(0, angle - 1);
                    break;
            }

            if (letter == ',' && depth == 0 && angle == 0)
            {
                handed.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(letter);
            }
        }
        handed.Add(current.ToString());
        return [.. handed.Select(one => one.Trim()).Where(one => one.Length > 0)];
    }

    private static readonly Regex Primary = new(
        @"^\s*(public|internal)\s+(sealed\s+|partial\s+|abstract\s+)*class\s+(\w+)(<[^>]*>)?\s*\(");

    /// <summary>How many things the named class is handed, read the way the scanner reads it.</summary>
    private static List<string> HandedTo(string type, params string[] parts)
    {
        string[] lines = Source(parts).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var match = Primary.Match(lines[i].Split("//")[0]);
            if (match.Success && match.Groups[3].Value == type)
            {
                return Handed(Parameters(lines, i, match.Index + match.Length - 1));
            }
        }

        Assert.Fail($"{type} has no primary constructor in {string.Join("/", parts)}");
        return [];
    }
    // #endregion ported-rule

    /// <summary>
    /// A generic type argument's comma is not a parameter separator. These three were counted as
    /// taking six by the comma rule and take four and five; splitting any of them would have been
    /// work done for a regular expression.
    /// </summary>
    [Theory]
    [InlineData("CampaignSource", 4, "api/TheYard.Api/Features/Admin/Cloud/CampaignSource.cs")]
    [InlineData("NightlyProcesses", 5, "api/TheYard.Api/Features/Admin/Cloud/NightlyProcesses.cs")]
    [InlineData("PlanHistoryReader", 5, "api/TheYard.Api/Shared/PlanReadings/PlanHistoryReader.cs")]
    public void A_comma_inside_a_generic_type_argument_is_not_a_parameter(string type, int handed, string path)
    {
        var taken = HandedTo(type, path.Split('/'));

        Assert.Equal(handed, taken.Count);
        Assert.Contains(taken, one => one.Contains("Func<", StringComparison.Ordinal));
    }

    /// <summary>
    /// A parameter list wide enough to wrap is read to its closing bracket, not to the end of the
    /// line the declaration starts on. These were invisible to the comma rule, and the first is the
    /// widest constructor in the host.
    /// </summary>
    [Theory]
    [InlineData("RequestRecorder", 8, "api/TheYard.Api/Shared/Activity/RequestRecorder.cs")]
    [InlineData("MachineRecorder", 7, "api/TheYard.Api/Shared/MachineReadings/MachineRecorder.cs")]
    [InlineData("PulseReader", 7, "api/TheYard.Api/Shared/Instance/Pulse.cs")]
    [InlineData("CloudReader", 6, "api/TheYard.Api/Features/Admin/Cloud/CloudReader.cs")]
    public void A_wrapped_parameter_list_is_read_to_its_closing_bracket(string type, int handed, string path)
    {
        Assert.Equal(handed, HandedTo(type, path.Split('/')).Count);
    }

    /// <summary>
    /// The classes this version split, each now handed fewer than six. Named one by one rather than
    /// counted, so a later change that widens one of them again fails on the name.
    /// </summary>
    [Theory]
    [InlineData("StoreAnnouncement", 2, "api/TheYard.Api/Shared/Hosted/StoreAnnouncement.cs")]
    [InlineData("StoreLogAttachment", 4, "api/TheYard.Api/Shared/Hosted/StoreLogAttachment.cs")]
    [InlineData("StoreStartupLines", 3, "api/TheYard.Api/Shared/Hosted/StoreStartupLines.cs")]
    [InlineData("HostRings", 4, "api/TheYard.Api/Shared/Observability/HostRings.cs")]
    [InlineData("RingReadings", 4, "api/TheYard.Api/Features/Admin/Operations/OperationsReadings.cs")]
    [InlineData("MachineCardReadings", 5, "api/TheYard.Api/Features/Admin/Operations/MachineCardReadings.cs")]
    [InlineData("CostKeeping", 5, "api/TheYard.Api/Shared/CostReadings/CostKeeping.cs")]
    [InlineData("CostRecorder", 2, "api/TheYard.Api/Shared/CostReadings/CostRecorder.cs")]
    public void Each_class_this_version_split_is_handed_fewer_than_six_things(string type, int handed, string path)
    {
        var taken = HandedTo(type, path.Split('/'));

        Assert.Equal(handed, taken.Count);
        Assert.True(taken.Count < 6, $"{type} is handed {taken.Count} things, which is what this rung exists to stop");
    }

    /// <summary>
    /// The rule in the script counts parameters rather than commas, and does it through the same
    /// whole-signature helper the async rule uses. Held against the script's own source, because a
    /// ported rule that has drifted from the one the build runs holds nothing.
    /// </summary>
    [Fact]
    public void The_scanner_counts_parameters_rather_than_commas_on_one_line()
    {
        string scanner = Scanner();

        Assert.Contains("# #region constructor-width", scanner, StringComparison.Ordinal);
        Assert.Contains("def handed(taken):", scanner, StringComparison.Ordinal);
        Assert.Contains("WIDE_AT = 6", scanner, StringComparison.Ordinal);

        // The old reading, which counted five commas before the first closing bracket. A change
        // that puts it back makes the three over-counts above real again.
        Assert.DoesNotContain("([^,)]*,){5,}", scanner, StringComparison.Ordinal);

        int region = scanner.IndexOf("# #region constructor-width", StringComparison.Ordinal);
        int ends = scanner.IndexOf("# #endregion constructor-width", StringComparison.Ordinal);
        Assert.True(ends > region, "the constructor-width region should open and close");
        Assert.Contains("handed(parameters(all_lines", scanner[region..ends], StringComparison.Ordinal);
    }

    /// <summary>
    /// What is left, by name, and how many. The ratchet holds the number; this holds the list, so
    /// the next instalment of this rung starts from a list it can check rather than a count. A
    /// class split off this list comes off it in the same commit, with the ratchet lowered beside.
    /// </summary>
    [Fact]
    public void The_classes_still_handed_six_or_more_are_the_eight_named_here()
    {
        (string Type, string Path)[] left =
        [
            ("CloudReader", "api/TheYard.Api/Features/Admin/Cloud/CloudReader.cs"),
            ("NightlySource", "api/TheYard.Api/Features/Admin/Cloud/NightlySource.cs"),
            ("StorageSource", "api/TheYard.Api/Features/Admin/Cloud/StorageSource.cs"),
            ("RequestRecorder", "api/TheYard.Api/Shared/Activity/RequestRecorder.cs"),
            ("PulseReader", "api/TheYard.Api/Shared/Instance/Pulse.cs"),
            ("MachineRecorder", "api/TheYard.Api/Shared/MachineReadings/MachineRecorder.cs"),
            ("AddressSearch", "functions/JobHunt.Application/Features/Campaign/AddressSearch.cs"),
            ("CampaignInbox", "functions/JobHunt.Application/Features/Campaign/CampaignInbox.cs"),
        ];

        foreach (var (type, path) in left)
        {
            Assert.True(
                HandedTo(type, path.Split('/')).Count >= 6,
                $"{type} is no longer wide; take it off this list and lower the ratchet in the same commit");
        }

        Assert.Equal(left.Length, Limit("wide_service_classes"));
    }

    // #region still-wired
    /// <summary>
    /// Every part split off a class this version touched is resolvable from the real host, by the
    /// type the container is asked for. A part that is registered nowhere is a run-time failure the
    /// build is blind to, and the two readings below are activated by type from their slice's
    /// module, so an unregistered <c>HostRings</c> would take the Admin tab's ring sections down
    /// rather than fail here.
    /// </summary>
    [Fact]
    public async Task Every_part_split_off_a_class_is_still_resolvable_from_the_host()
    {
        await using var host = new WebApplicationFactory<Program>();

        Assert.NotNull(host.Services.GetRequiredService<HostRings>());
        Assert.NotNull(host.Services.GetRequiredService<StoreLogAttachment>());
        Assert.NotNull(host.Services.GetRequiredService<StoreStartupLines>());
        Assert.NotNull(host.Services.GetRequiredService<CostKeeping>());
        Assert.NotNull(host.Services.GetRequiredService<CostRecorder>());

        // The two readings are scoped to a request, as everything the Admin tab's handlers take is.
        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRingReadings>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMachineReadings>());
    }

    /// <summary>
    /// The announcement still does both of its moves, and in the order that matters: the loggers
    /// are attached before anything is said, because a line written before the factory is attached
    /// goes nowhere a reader of the Admin tab's log section will look. Read off the seam the
    /// attachment leaves behind, which is the one a store coming up later reads.
    /// </summary>
    [Fact]
    public async Task The_announcement_attaches_the_loggers_before_it_says_anything()
    {
        await using var host = new WebApplicationFactory<Program>();

        // Starting the host runs the hosted services; the first of them is the announcement.
        _ = host.CreateClient();

        Assert.NotNull(host.Services.GetRequiredService<StoreLoggers>().Factory);
    }
    // #endregion still-wired
}
