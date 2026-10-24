using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The campaign's text and order rules (ADR-118, the review of the email campaign F4, F8, F9, F15 and 8.2): the
/// ported tells against the Python file, the WhatTheyDo cases the review names, the cap and ramp, the recipient
/// domain, the Strength formula and its hash, the applied word, the proof clauses, the LinkedIn order and the
/// optional line. Every company and person here is made up.
/// </summary>
public sealed partial class CampaignTextTests
{
    private static string Python()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "functions", "jobhunt.Tests", "Fixtures", "ai_tells_five_signs.py");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        throw new InvalidOperationException("Fixtures/ai_tells_five_signs.py not found above " + AppContext.BaseDirectory);
    }

    private static List<string> PythonList(string python, string name)
    {
        int start = python.IndexOf(name + " = [", StringComparison.Ordinal);
        Assert.True(start >= 0, name + " is not in the Python file");
        int end = python.IndexOf("\n]", start, StringComparison.Ordinal);
        return [.. Quoted().Matches(python[start..end]).Select(m => m.Groups[1].Value)];
    }

    [Fact]
    public void F15_the_ported_tells_match_the_python_file_by_count_and_content()
    {
        string python = Python();
        var words = PythonList(python, "S1_WORDS");
        var phrases = PythonList(python, "S1_PHRASES");

        Assert.Equal(words.Count, Tells.Words.Count);
        Assert.Equal(words, Tells.Words);
        Assert.Equal(phrases.Count, Tells.Phrases.Count);
        Assert.Equal(phrases, Tells.Phrases);
        Assert.Equal(["innovative", "wide range of"], Tells.Find("An Innovative shop with a wide range of tools"));
        Assert.Empty(Tells.Find("navigated the app's router"));
    }

    [Theory]
    [InlineData("builds practice management software for dental offices", null)]
    [InlineData("insures small businesses across the Midwest", null)]
    [InlineData("runs payroll for restaurants, mostly in Texas", null)]
    [InlineData("is a leading provider of innovative healthcare solutions", "opens with \"is\"")]
    [InlineData("They build dental software.", "opens with \"They\"")]
    [InlineData("builds practice management software for dental offices, streamlining workflows and empowering teams", "a tell (streamlining, empowering)")]
    [InlineData("builds software \u2014 the kind dentists love", "a dash")]
    [InlineData("builds CoverMyMeds for McKesson", "names the company (CoverMyMeds)")]
    [InlineData("build dental software", "opens with \"build\"")]
    [InlineData("", "empty")]
    [InlineData("builds dental software.", "ends in punctuation")]
    [InlineData("builds apps", "11 characters")]
    [InlineData("makes tools, which then go on to be sold to every dentist in the country", "a comma clause longer")]
    [InlineData("builds \"smart\" dental software", "quote marks")]
    [InlineData("builds dental software at acme.com", "an address")]
    [InlineData("builds {Product} for dentists", "braces")]
    [InlineData("builds robust dental software", "a tell (robust)")]
    public void F15_what_they_do_is_one_plain_clause_or_the_lead_is_blocked_naming_the_rule(string clause, string? broken)
    {
        string? verdict = WhatTheyDoRule.Check(clause, ["CoverMyMeds"]);
        if (broken is null)
        {
            Assert.Null(verdict);
        }
        else
        {
            Assert.NotNull(verdict);
            Assert.StartsWith("WhatTheyDo: " + broken, verdict, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void F15_a_three_hundred_character_paragraph_is_blocked_by_its_length()
    {
        string paragraph = "builds " + string.Concat(Enumerable.Repeat("dental software ", 19)).Trim();
        Assert.Equal(310, paragraph.Length);
        Assert.Equal("WhatTheyDo: 310 characters, not 20 to 110", WhatTheyDoRule.Check(paragraph, ["Acme"]));
    }

    [Fact]
    public void F4_ten_a_day_at_most_and_three_five_ten_on_the_first_days_of_on()
    {
        Assert.Equal(10, CampaignRules.DailyCap);
        Assert.Equal([3, 5, 10], CampaignRules.Ramp);
        Assert.Equal([10, 3, 5, 10, 10, 10], new int?[] { null, 1, 2, 3, 4, 40 }.Select(CampaignRules.CapFor));
        Assert.Equal(TimeSpan.FromSeconds(5), CampaignRules.Spacing);
        // Eleven candidates on a day with none sent yield ten; a day already at ten yields none.
        Assert.Equal(10, Math.Min(11, CampaignRules.Left(0, null)));
        Assert.Equal(0, CampaignRules.Left(10, null));
        Assert.Equal(0, CampaignRules.Left(12, 4));
        Assert.Equal(2, CampaignRules.Left(1, 1));
    }

    [Theory]
    [InlineData("pat@acme.com", true, true, "allowed")]
    [InlineData("pat@acme.com", null, false, "MX not checked")]
    [InlineData("pat@acme.com", false, false, "no MX for acme.com")]
    [InlineData("pat.lee@gmail.com", true, false, "personal domain (gmail.com)")]
    [InlineData("pat@HOTMAIL.com", true, false, "personal domain (hotmail.com)")]
    [InlineData("pat@icloud.com", true, false, "personal domain (icloud.com)")]
    [InlineData("pat@yahoo.com", true, false, "personal domain (yahoo.com)")]
    [InlineData("pat@outlook.com", true, false, "personal domain (outlook.com)")]
    [InlineData("", true, false, "no address")]
    [InlineData("pat at acme.com", true, false, "not an address")]
    [InlineData("pat@@acme.com", true, false, "not an address")]
    public void Recipients_are_business_domains_with_mx_found(string address, bool? mx, bool allowed, string reason)
    {
        var verdict = RecipientDomainRule.Check(address, mx);
        Assert.Equal((allowed, reason), (verdict.Allowed, verdict.Reason));
    }

    private static readonly DateOnly Today = new(2026, 10, 24);

    [Fact]
    public void F9_strength_is_a_formula_over_stored_facts_with_one_bounded_model_step()
    {
        var best = new StrengthInputs(RemoteStance.Remote, 5, true, 185_000, 0, Today.AddDays(-2), Today.AddDays(-3));
        Assert.Equal(3 + 3 + 2 + 2 + 0 + 1 + 1, Strength.Score(best, Today));
        Assert.Equal(Strength.Score(best, Today) + 1, Strength.Score(best, Today, 4));
        Assert.Equal(Strength.Score(best, Today) - 1, Strength.Score(best, Today, -9));

        var ghost = best with { GhostScore = 3 };
        var worst = new StrengthInputs(RemoteStance.Onsite, 0, false, 100_000, 2, Today.AddDays(-90), null);
        Assert.Equal(13, Strength.Score(best, Today, 1));
        Assert.Equal(-8, Strength.Score(worst, Today, -1));
        Assert.True(Strength.Score(ghost, Today, 1) < Strength.Score(worst, Today, -1), "a ghost score of 3 drops below every 0 to 2");
        Assert.Equal(1, Strength.Score(best with { BandTop = 160_000 }, Today) - Strength.Score(best with { BandTop = null }, Today));
        Assert.Equal(-2, Strength.Score(best with { BandTop = 150_000 }, Today) - Strength.Score(best with { BandTop = null }, Today));
        Assert.Equal(-6, Strength.Score(best with { Remote = RemoteStance.Onsite }, Today) - Strength.Score(best, Today));
        Assert.Equal(-2, Strength.Score(best with { FirstSeen = Today.AddDays(-61) }, Today) - Strength.Score(best, Today));
        Assert.Equal(-1, Strength.Score(best with { LastDotNetPostingAt = Today.AddDays(-31) }, Today) - Strength.Score(best, Today));
    }

    [Fact]
    public void F9_the_inputs_hash_moves_with_a_fact_and_never_with_the_day()
    {
        var inputs = new StrengthInputs(RemoteStance.Remote, 2, false, null, 1, Today.AddDays(-9), null);
        string hash = Strength.Hash(inputs);

        Assert.Matches("^[0-9a-f]{16}$", hash);
        Assert.Equal(hash, Strength.Hash(inputs with { }));
        Assert.NotEqual(hash, Strength.Hash(inputs with { DotNetEvidence = 3 }));
        Assert.NotEqual(hash, Strength.Hash(inputs with { GhostScore = null }));
        Assert.NotEqual(Strength.Score(inputs, Today), Strength.Score(inputs, Today.AddDays(60)));
    }

    private static DateTimeOffset Central(int day, int hour) => new(2026, 10, day, hour, 0, 0, TimeSpan.FromHours(-5));

    [Theory]
    [InlineData(24, 8, "today")]
    [InlineData(23, 16, "yesterday")]
    [InlineData(23, 23, "yesterday")]
    [InlineData(22, 9, "on Thursday")]
    [InlineData(19, 9, "on Monday")]
    [InlineData(18, 9, "on Sunday")]
    [InlineData(17, 9, "last Saturday")]
    [InlineData(16, 9, null)]
    [InlineData(25, 9, null)]
    public void F8_the_applied_word_is_rendered_in_central_and_track_a_stops_after_seven_days(int day, int hour, string? word)
    {
        var send = Central(24, 8);
        Assert.Equal(word, AppliedWhen.Render(Central(day, hour), send));
        Assert.Equal(word is not null, AppliedWhen.Check(Central(day, hour), send).Allowed);
    }

    [Fact]
    public void F8_the_word_follows_the_central_day_across_the_clock_change()
    {
        // 2026-11-01 02:00 CDT is 01:00 CST: an application at 23:30 CDT on 10/31 is "yesterday" at 8:00 CST on 11/1.
        var applied = new DateTimeOffset(2026, 11, 1, 4, 30, 0, TimeSpan.Zero);
        var send = new DateTimeOffset(2026, 11, 1, 14, 0, 0, TimeSpan.Zero);
        Assert.Equal("yesterday", AppliedWhen.Render(applied, send));
        Assert.Equal("Track A with no AppliedAt", AppliedWhen.Check(null, send).Reason);
    }

    [Fact]
    public void Proof_keys_render_their_clause_and_a_missing_key_renders_the_ai_clause_the_templates_carry()
    {
        Assert.Equal(["reliability", "platform", "leadership", "ai"], ProofKey.Keys);
        Assert.Equal("along with the AI automation I have been building into my own work", ProofKey.Sentence(null));
        Assert.Equal(ProofKey.Sentences[ProofKey.Ai], ProofKey.Sentence("unknown"));
        Assert.Equal(ProofKey.Sentences[ProofKey.Platform], ProofKey.Sentence(" Platform "));
        foreach (string sentence in ProofKey.Sentences.Values)
        {
            Assert.StartsWith("along with ", sentence, StringComparison.Ordinal);
            Assert.Empty(Tells.Find(sentence));
            Assert.DoesNotContain('\u2014', sentence);
        }
    }

    [Fact]
    public void Linkedin_first_then_an_invite_out_three_days_then_strength_then_the_oldest()
    {
        var now = Central(24, 8);
        Assert.Equal(0, LinkedInOrder.Rank(now.AddDays(-1), now.AddDays(-5), now));
        Assert.Equal(1, LinkedInOrder.Rank(null, now.AddDays(-4), now));
        Assert.Equal(2, LinkedInOrder.Rank(null, now.AddDays(-2), now));
        Assert.Equal(2, LinkedInOrder.Rank(null, null, now));

        var order = SendOrder.Sort(
        [
            new RankedLead(1, 2, 12, now.AddDays(-9)),
            new RankedLead(2, 1, 3, now.AddDays(-1)),
            new RankedLead(3, 2, 12, now.AddDays(-20)),
            new RankedLead(4, 0, 1, now),
            new RankedLead(5, 2, 14, now),
        ]);
        Assert.Equal([4L, 2L, 5L, 3L, 1L], order.Select(l => l.LeadId));
    }

    private const string Template = "Hi {FirstName},\n\nI saw that {Company} is hiring.\nI saw the {LastDotNetPostingTitle} you posted in {LastDotNetPostingMonth}.\n\nSteve\n";

    private static readonly HashSet<string> Optional = ["LastDotNetPostingTitle", "LastDotNetPostingMonth"];

    [Fact]
    public void An_optional_line_renders_only_when_its_values_are_there_and_leaves_no_gap()
    {
        var full = TemplateFill.Fill(Template, new Dictionary<string, string?>
        {
            ["FirstName"] = "Pat",
            ["Company"] = "Acme",
            ["LastDotNetPostingTitle"] = "Senior .NET Engineer",
            ["LastDotNetPostingMonth"] = "October",
        }, Optional);
        Assert.Equal("Hi Pat,\n\nI saw that Acme is hiring.\nI saw the Senior .NET Engineer you posted in October.\n\nSteve\n", full.Text);

        var without = TemplateFill.Fill(Template, new Dictionary<string, string?> { ["FirstName"] = "Pat", ["Company"] = "Acme", ["LastDotNetPostingTitle"] = "Senior .NET Engineer" }, Optional);
        Assert.Equal("Hi Pat,\n\nI saw that Acme is hiring.\n\nSteve\n", without.Text);

        var ownParagraph = TemplateFill.Fill("A {X}.\n\nI saw the {LastDotNetPostingTitle}.\n\nSteve", new Dictionary<string, string?> { ["X"] = "b" }, Optional);
        Assert.Equal("A b.\n\nSteve", ownParagraph.Text);
    }

    [Fact]
    public void F12_a_required_blank_fills_nothing_and_names_the_blank()
    {
        var filled = TemplateFill.Fill(Template, new Dictionary<string, string?> { ["FirstName"] = " ", ["Company"] = "Acme" }, Optional);
        Assert.False(filled.Complete);
        Assert.Null(filled.Text);
        Assert.Equal(["FirstName"], filled.Blanks);
        Assert.Equal(["FirstName", "Company", "LastDotNetPostingTitle", "LastDotNetPostingMonth"], TemplateFill.Variables(Template));
    }

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex Quoted();
}
