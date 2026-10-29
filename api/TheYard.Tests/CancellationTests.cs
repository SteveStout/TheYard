using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// What is left of the cancellation count, and the one reading mistake that made it look larger
/// than it was (ADR: The numbers that may only fall, the addendum on reading a whole signature).
///
/// <para>The ratchet counts async methods declared with no <c>CancellationToken</c>. It read a
/// declaration's parameters as "everything up to the first closing bracket", and three ordinary
/// shapes put a bracket in front of the token: an attribute with arguments of its own, a tuple
/// parameter, and a signature wide enough to wrap onto the next line. Eight methods that do take
/// a token were counted as methods that do not, and the cheapest way to make that number fall
/// would have been to reorder an Azure Functions trigger's parameters to suit a regular
/// expression. So the scanner reads the whole signature, and this holds both halves: that the
/// reading is right, and that the code those eight are in is right.</para>
/// </summary>
public class CancellationTests
{
    private static string Scanner() => File.ReadAllText(Path.Combine(Repo.Root(), ".github", "quality.py")).ReplaceLineEndings("\n");

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(Repo.Root(), Path.Combine(parts))).ReplaceLineEndings("\n");

    // #region whole-signature
    /// <summary>
    /// The scanner's reading, ported: the text between a declaration's own brackets, across as
    /// many lines as it takes. Written here as the Python is written there, and held against the
    /// Python's own source by the test below, which is how this codebase holds a rule that lives
    /// in a script (<c>CampaignTextTests</c> does the same for the campaign's word lists).
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

    private static readonly Regex Declaration = new(
        @"\b(public|internal|private|protected)\s+(?:static\s+|override\s+|virtual\s+)*async\s+[\w<>\[\],? ]+\s+(\w+)\s*\(");

    /// <summary>The parameters the scanner's rule reads for the named method in the named file.</summary>
    private static string ParametersOf(string method, params string[] parts)
    {
        string[] lines = Source(parts).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var match = Declaration.Match(lines[i].Split("//")[0]);
            if (match.Success && match.Groups[2].Value == method)
            {
                string? read = Parameters(lines, i, match.Index + match.Length - 1);
                Assert.NotNull(read);
                return read;
            }
        }
        Assert.Fail($"{method} is not declared in {string.Join("/", parts)}");
        return "";
    }

    /// <summary>
    /// The six timers Azure Functions hands a token to. Each one's trigger attribute carries a
    /// cron expression in brackets, which is the bracket the old reading stopped at.
    /// </summary>
    public static TheoryData<string> Timers =>
    [
        "AtsSweepTimer", "CampaignSendTimer", "DailyCardTimer", "Merger", "Nightly", "SheetMirrorTimer",
    ];

    [Theory]
    [MemberData(nameof(Timers))]
    public void Every_timer_the_Function_runs_declares_the_token_Azure_hands_it_and_uses_it(string timer)
    {
        string file = Path.Combine("functions", "jobhunt", timer + ".cs");
        string taken = ParametersOf("RunAsync", "functions", "jobhunt", timer + ".cs");

        Assert.Contains("TimerTrigger", taken, StringComparison.Ordinal);
        Assert.Contains("CancellationToken ct", taken, StringComparison.Ordinal);

        // Declaring it is not using it, which is the whole reason this rung was not finished by a
        // count: the body has to hand it on, or the signature is decoration.
        string body = Source("functions", "jobhunt", timer + ".cs");
        int uses = Regex.Matches(body, @"\bct\b").Count;
        Assert.True(uses > 1, $"{file} declares the token and never passes it on");
    }

    [Fact]
    public void A_tuple_parameter_and_a_wrapped_signature_do_not_hide_the_token_either()
    {
        Assert.Contains(
            "CancellationToken cancellation",
            ParametersOf("LeadAsync", "api", "TheYard.Infrastructure.Cosmos", "Shared", "Auction", "CosmosCatalogueKeysets.cs"),
            StringComparison.Ordinal);

        // The tuple is what the old reading stopped at here, and it is still in the signature.
        Assert.Contains(
            "(string Path, bool Descending) key",
            ParametersOf("LeadAsync", "api", "TheYard.Infrastructure.Cosmos", "Shared", "Auction", "CosmosCatalogueKeysets.cs"),
            StringComparison.Ordinal);

        // This one's declaration line ends on a comma: the token is two lines further down.
        Assert.Contains(
            "CancellationToken cancellation",
            ParametersOf("PrepareAsync", "api", "TheYard.Infrastructure", "Shared", "Stores", "YardDatabase.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_scanner_reads_a_whole_signature_rather_than_stopping_at_the_first_bracket()
    {
        string scanner = Scanner();

        // The reading is a named block, so a reader who finds a number surprising can see how it
        // was counted without reading the whole script.
        Assert.Contains("# #region whole-signature", scanner, StringComparison.Ordinal);
        Assert.Contains("def parameters(lines, start, open_at):", scanner, StringComparison.Ordinal);
        Assert.Contains("RUN_ON = 12", scanner, StringComparison.Ordinal);

        // And the truncating capture is gone from the rule that used it. `([^)]*)` elsewhere in the
        // script is another rule's business; this asserts the async rule reads its parameters by
        // calling the block above.
        int rule = scanner.IndexOf("smell[\"async_no_token\"]", StringComparison.Ordinal);
        Assert.True(rule > 0, "the scanner should still count async methods with no token");
        string around = scanner[(scanner.LastIndexOf("        m = re.search", rule, StringComparison.Ordinal))..rule];
        Assert.Contains("taken = parameters(all_lines", around, StringComparison.Ordinal);
        Assert.DoesNotContain("([^)]*)", around);
    }
    // #endregion whole-signature

    /// <summary>
    /// What the count is allowed to be. One, and the one is an override of a framework type whose
    /// signature this codebase does not own, so it can never take a token and the floor is not
    /// zero (ADR: The numbers that may only fall, the addendum of 1.0.4.116).
    /// </summary>
    [Fact]
    public void The_only_async_method_left_with_no_token_is_the_framework_override_that_cannot_take_one()
    {
        string refuse = Source("api", "TheYard.Api", "Features", "Accounts", "RefuseSession.cs");
        Assert.Contains("public override async Task TokenValidated(TokenValidatedContext context)", refuse, StringComparison.Ordinal);

        using var file = System.Text.Json.JsonDocument.Parse(Source(".github", "quality-ratchet.json"));
        Assert.Equal(1, file.RootElement.GetProperty("async_without_cancellation").GetInt32());
    }
}
