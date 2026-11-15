using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The application form read at pick time, so the card and the apply lane see before 06:00 what a READY row's form
/// asks that no bank answers: a required essay, a required acknowledgement or agreement, a required cover letter. Each
/// board's answer is written here in that board's own shape (Greenhouse's job with questions, Ashby's posting with its
/// application form, Lever's apply page with its question cards) and carries the labels the 9 November walk recorded
/// for MOXFIVE, Headway and Homebound. The answers themselves are Steve's; the picker only names them.
/// </summary>
public sealed class ReadyRowFormTests
{
    /// <summary>The Headway agreement as the walk recorded it.</summary>
    public const string HeadwayReferences = "I understand and agree that Headway may contact additional references beyond the references you provide";

    /// <summary>The Homebound acknowledgement as the walk recorded it.</summary>
    public const string HomeboundReferences = "I acknowledge that Homebound will conduct business reference checks before extending a formal offer";

    private static string Ashby(params string[] entries) =>
        "{\"data\":{\"jobPosting\":{\"id\":\"x\",\"title\":\"t\",\"applicationForm\":{\"sections\":[{\"title\":null,\"fieldEntries\":[" + string.Join(",", entries) + "]}]}}}}";

    private static string Entry(string type, string title, bool required, int options = 0, string description = "") =>
        $"{{\"id\":\"e\",\"isRequired\":{(required ? "true" : "false")},\"descriptionHtml\":{System.Text.Json.JsonSerializer.Serialize(description)},\"field\":{{\"path\":\"p\",\"type\":\"{type}\",\"title\":{System.Text.Json.JsonSerializer.Serialize(title)},"
        + $"\"selectableValues\":[{string.Join(",", Enumerable.Range(0, options).Select(i => $"{{\"label\":\"o{i}\",\"value\":\"o{i}\"}}"))}]}}}}";

    [Fact]
    public void MOXFIVE_s_three_required_essays_are_one_note_and_no_label_is_copied()
    {
        string json = Ashby(Entry("String", "Name", true), Entry("Email", "Email", true), Entry("File", "Resume", true),
            Entry("LongText", "Describe an AI agent system you took from prototype to production. How did you evaluate it?", true),
            Entry("LongText", "Tell us about a high-stakes architectural decision you made with incomplete information.", true),
            Entry("LongText", "How do you use coding agents in your daily work?", true));

        Assert.Equal("FORM: 3 essays required", FormNotes.Of(PublicFormReader.ReadAshby(json)));
    }

    [Fact]
    public void Headway_s_single_option_agreement_is_an_acknowledgement_and_its_yes_no_questions_are_not()
    {
        string json = Ashby(Entry("String", "Full Name", true), Entry("File", "Cover Letter", false),
            Entry("String", "Who is your current or previous employer?", true),
            Entry("Boolean", "Are you authorized to work in the United States for any employer?", true),
            Entry("ValueSelect", HeadwayReferences, true, options: 1));

        Assert.Equal("FORM: acknowledgement " + HeadwayReferences, FormNotes.Of(PublicFormReader.ReadAshby(json)));
    }

    [Fact]
    public void Homebound_s_reference_check_policy_names_its_acknowledgement_from_the_description_and_optional_fields_say_nothing()
    {
        string json = Ashby(Entry("File", "Cover Letter", false), Entry("LongText", "Why Homebound?", false),
            Entry("Boolean", "Reference Check Policy", true, description: "<p>" + HomeboundReferences + ".</p>"));

        Assert.Equal("FORM: acknowledgement Reference Check Policy: " + HomeboundReferences + ".", FormNotes.Of(PublicFormReader.ReadAshby(json)));
    }

    [Fact]
    public void A_Greenhouse_form_names_a_required_essay_a_checkbox_and_a_cover_letter_and_not_its_ordinary_questions()
    {
        const string json = """
            {"id": 1, "title": "Staff Engineer", "questions": [
              {"required": true, "label": "First Name", "description": null, "fields": [{"name": "first_name", "type": "input_text", "values": []}]},
              {"required": true, "label": "Resume/CV", "description": null, "fields": [{"name": "resume", "type": "input_file", "values": []}, {"name": "resume_text", "type": "textarea", "values": []}]},
              {"required": true, "label": "Cover Letter", "description": null, "fields": [{"name": "cover_letter", "type": "input_file", "values": []}, {"name": "cover_letter_text", "type": "textarea", "values": []}]},
              {"required": true, "label": "What is your expected yearly salary?", "description": null, "fields": [{"name": "question_1", "type": "input_text", "values": []}]},
              {"required": true, "label": "Do you have any agreement with a current or past employer that limits the companies you may work for?", "description": null, "fields": [{"name": "question_2", "type": "multi_value_single_select", "values": [{"label": "Yes", "value": 1}, {"label": "No", "value": 0}]}]},
              {"required": true, "label": "Why do you want to work on this team?", "description": null, "fields": [{"name": "question_3", "type": "textarea", "values": []}]},
              {"required": true, "label": "I certify that the information I provide is true", "description": null, "fields": [{"name": "question_4", "type": "multi_value_multi_select", "values": [{"label": "Yes", "value": 1}]}]},
              {"required": false, "label": "Anything else?", "description": null, "fields": [{"name": "question_5", "type": "textarea", "values": []}]}
            ], "location_questions": [{"required": true, "label": "Latitude", "fields": [{"name": "latitude", "type": "input_hidden", "values": []}]}]}
            """;

        Assert.Equal("FORM: essay required; FORM: cover letter required; FORM: acknowledgement I certify that the information I provide is true", FormNotes.Of(PublicFormReader.ReadGreenhouse(json)));
    }

    [Fact]
    public void A_Lever_apply_page_s_required_card_question_is_read_from_its_template()
    {
        string template = System.Net.WebUtility.HtmlEncode("{\"text\":\"Questions\",\"fields\":[{\"type\":\"textarea\",\"text\":\"Tell us about a system you scaled.\",\"required\":true},{\"type\":\"multiple-choice\",\"text\":\"Do you need sponsorship?\",\"required\":true,\"options\":[{\"text\":\"Yes\"},{\"text\":\"No\"}]},{\"type\":\"text\",\"text\":\"GitHub\",\"required\":false}]}");
        string page = $"<form><input type=\"hidden\" value=\"{template}\" name=\"cards[abc][baseTemplate]\"><textarea name=\"cards[abc][field0]\" required=\"required\"></textarea></form>";

        Assert.Equal("FORM: essay required", FormNotes.Of(PublicFormReader.ReadLever(page)));
        Assert.Equal("", FormNotes.Of(PublicFormReader.ReadLever("<form><input name=\"name\"></form>")));
    }

    [Fact]
    public void A_form_that_could_not_be_read_says_so_and_a_clean_one_says_nothing()
    {
        Assert.Equal("FORM: not read", FormNotes.Of(null));
        Assert.Equal("", FormNotes.Of([]));
        Assert.Null(PublicFormReader.ReadAshby("{\"data\":{\"jobPosting\":null}}"));
        Assert.Equal("FORM: acknowledgement I agree to the terms - and the policy / page", FormNotes.Of([new FormQuestion("I agree to the terms \u2014 and the policy | page", FormQuestion.Choice, true, 1)]));
        Assert.EndsWith("...", FormNotes.Of([new FormQuestion("I acknowledge " + string.Join(' ', Enumerable.Repeat("word", 60)), FormQuestion.YesNo, true)]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_picker_writes_each_feed_row_s_form_note_in_the_Form_column_and_reads_no_alert_s_form()
    {
        using var scratch = new Scratch();
        scratch.Write("nightly/leads-2026-11-09.md", "\n## 1a | LinkedIn digest, x | 2026-11-09 01:10 Central | s | 1 requisitions\n\n| Company | Title | Location / remote line as written | Link | Source Gmail id | Filter |\n|---|---|---|---|---|---|\n| Globex | Staff .NET Engineer | Remote | https://www.linkedin.com/jobs/view/2/ | 1a | pass |\n");
        Candidate Feed(string company, string kind, string token, string id) =>
            new Candidate("feed:" + kind, company, "Principal Software Engineer", $"https://jobs.example/{token}/{id}", kind, new DateOnly(2026, 11, 8), "Remote, US", "Remote", 210_000, "feed", "C# on Azure")
            { BoardToken = token, PostingId = id };
        var postings = new PickFeed { Rows = { (1, Feed("MOXFIVE", "ashby", "moxfive", "eaa48261")), (2, Feed("Homebound", "ashby", "Homebound", "fb4c15fb")), (3, Feed("Acme", "greenhouse", "acme", "1")) } };
        var forms = new ScriptedForms
        {
            ["ashby|moxfive|eaa48261"] = [new FormQuestion("Q1", FormQuestion.LongText, true)],
            ["ashby|Homebound|fb4c15fb"] = [new FormQuestion("Reference Check Policy", FormQuestion.YesNo, true, 0, HomeboundReferences)],
        };

        var result = await new JobsPick(scratch.Store, postings, new NoTracker(), forms, new At(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero))).RunAsync(CancellationToken.None);
        var rows = scratch.Text(result.Path).Split('\n').Skip(2).TakeWhile(l => l != "# FAILED").Select(l => l.Split('\t')).ToDictionary(r => r[1], r => r[JobsFiles.Columns.Count - 1]);

        Assert.Equal("Form", JobsFiles.Columns[^1]);
        Assert.Equal("FORM: essay required", rows["MOXFIVE"]);
        Assert.Equal("FORM: acknowledgement Reference Check Policy: " + HomeboundReferences, rows["Homebound"]);
        Assert.Equal("FORM: not read", rows["Acme"]);
        Assert.Equal("", rows["Globex"]);
        Assert.Equal(3, forms.Asked.Count);
    }

    /// <summary>A form reader that answers from a table keyed "kind|token|id" and null for anything else, as a board that did not answer.</summary>
    internal sealed class ScriptedForms : Dictionary<string, IReadOnlyList<FormQuestion>>, IPostingForms
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Asked { get; } = [];

        public Task<IReadOnlyList<FormQuestion>?> ReadAsync(string kind, string token, string postingId, CancellationToken ct)
        {
            string key = $"{kind}|{token}|{postingId}";
            Asked.Add(key);
            return Task.FromResult(TryGetValue(key, out var form) ? form : null);
        }
    }

    private sealed class PickFeed : IJobsPostings
    {
        public List<(long Key, Candidate Candidate)> Rows { get; } = [];

        public Task<bool> ReadyAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<(long Key, Candidate Candidate)>> OpenAsync(DateTimeOffset seenSince, CancellationToken ct) => Task.FromResult<IReadOnlyList<(long, Candidate)>>(Rows);

        public Task<IReadOnlyDictionary<long, string>> TextsAsync(IReadOnlyList<long> keys, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<long, string>>(new Dictionary<long, string>());

        public Task<IReadOnlyList<FreshPosting>> FreshAsync(DateTimeOffset seenSince, CancellationToken ct) => Task.FromResult<IReadOnlyList<FreshPosting>>([]);
    }

    private sealed class NoTracker : ITrackerRows
    {
        public Task<IReadOnlyList<string[]>> RowsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string[]>>([]);

        public Task<IReadOnlyList<TrackerTouch>> SinceAsync(DateTimeOffset since, CancellationToken ct) => Task.FromResult<IReadOnlyList<TrackerTouch>>([]);
    }

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>A form reader that never reads a form, for the picker's tests that are not about forms.</summary>
internal sealed class NoForms : IPostingForms
{
    public Task<IReadOnlyList<FormQuestion>?> ReadAsync(string kind, string token, string postingId, CancellationToken ct) => Task.FromResult<IReadOnlyList<FormQuestion>?>([]);
}

/// <summary>An HTTP factory whose client is never asked for: the network is not a test's to call.</summary>
internal sealed class NoNetwork : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => throw new InvalidOperationException("no network in a test");
}
