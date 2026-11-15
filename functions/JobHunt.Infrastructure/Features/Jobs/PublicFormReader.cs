using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobHunt.Infrastructure.Features.Jobs;

/// <summary>
/// A posting's application form from the three public boards, keyless, the way each serves it to an applicant:
/// Greenhouse's job with questions=true, the posting Ashby's hosted page asks its GraphQL for (ApiJobPosting, its
/// applicationForm), and Lever's apply page, whose question cards carry their fields as JSON in a hidden baseTemplate
/// input. Each question is classed for FormNotes: a long text box is an essay, a field named or titled cover letter is
/// the cover letter, a select or a checkbox is a choice with its option count, a toggle is yes or no, a file is a file,
/// the rest is one line. Greenhouse's hidden location fields and its voluntary compliance questions are not read. A board
/// that does not answer within <see cref="Timeout"/>, answers an error, or answers a body of another shape is null, so one
/// form never stops the pick.
/// </summary>
public sealed partial class PublicFormReader(IHttpClientFactory http) : IPostingForms
{
    /// <summary>The address Ashby's hosted job page asks for a posting.</summary>
    public const string AshbyGraphQl = "https://jobs.ashbyhq.com/api/non-user-graphql?op=ApiJobPosting";

    private const string AshbyQuery = "query ApiJobPosting($organizationHostedJobsPageName: String!, $jobPostingId: String!) { jobPosting(organizationHostedJobsPageName: $organizationHostedJobsPageName, jobPostingId: $jobPostingId) { id title applicationForm { sections { title fieldEntries { ... on FormFieldEntry { id field isRequired descriptionHtml } } } } } }";

    /// <summary>How long one form may take to answer.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FormQuestion>?> ReadAsync(string kind, string token, string postingId, CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(Timeout);
        try
        {
            using var request = Request(kind, token, postingId);
            if (request is null)
            {
                return null;
            }
            using var answer = await http.CreateClient().SendAsync(request, cap.Token);
            if (!answer.IsSuccessStatusCode)
            {
                return null;
            }
            string body = await answer.Content.ReadAsStringAsync(cap.Token);
            return kind switch
            {
                "greenhouse" => ReadGreenhouse(body),
                "ashby" => ReadAshby(body),
                _ => ReadLever(body),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    /// <summary>The request for one posting's form, or null for a board this reader does not know.</summary>
    public static HttpRequestMessage? Request(string kind, string token, string postingId) => kind switch
    {
        "greenhouse" => new HttpRequestMessage(HttpMethod.Get, $"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(token)}/jobs/{Uri.EscapeDataString(postingId)}?questions=true"),
        "lever" => new HttpRequestMessage(HttpMethod.Get, $"https://jobs.lever.co/{Uri.EscapeDataString(token)}/{Uri.EscapeDataString(postingId)}/apply"),
        "ashby" => new HttpRequestMessage(HttpMethod.Post, AshbyGraphQl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                operationName = "ApiJobPosting",
                variables = new { organizationHostedJobsPageName = token, jobPostingId = postingId },
                query = AshbyQuery,
            }), Encoding.UTF8, "application/json"),
        },
        _ => null,
    };

    /// <summary>Greenhouse's job with questions to the form's questions.</summary>
    public static IReadOnlyList<FormQuestion> ReadGreenhouse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("no questions in the answer");
        }
        var form = new List<FormQuestion>();
        foreach (var q in questions.EnumerateArray())
        {
            var fields = q.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array ? f.EnumerateArray().ToList() : [];
            var names = fields.Select(x => Text(x, "name")).ToList();
            var types = fields.Select(x => Text(x, "type")).ToList();
            int options = fields.Select(x => x.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : 0).DefaultIfEmpty(0).Max();
            string kind = names.Any(n => n.StartsWith("cover_letter", StringComparison.Ordinal)) ? FormQuestion.CoverLetter
                : names.Any(n => n.StartsWith("resume", StringComparison.Ordinal)) ? FormQuestion.File
                : types.Contains("textarea") ? FormQuestion.LongText
                : types.Contains("input_file") ? FormQuestion.File
                : types.Any(t => t.StartsWith("multi_value", StringComparison.Ordinal)) ? FormQuestion.Choice
                : FormQuestion.Text;
            if (types.Count > 0 && types.All(t => t == "input_hidden"))
            {
                continue;
            }
            form.Add(new FormQuestion(Text(q, "label"), kind, q.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True, options, Plain(Text(q, "description"))));
        }
        return form;
    }

    /// <summary>Ashby's ApiJobPosting answer to the form's questions, or null when the posting is gone.</summary>
    public static IReadOnlyList<FormQuestion>? ReadAshby(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var posting = doc.RootElement.GetProperty("data").GetProperty("jobPosting");
        if (posting.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var form = new List<FormQuestion>();
        foreach (var section in posting.GetProperty("applicationForm").GetProperty("sections").EnumerateArray())
        {
            foreach (var entry in section.GetProperty("fieldEntries").EnumerateArray().Where(e => e.TryGetProperty("field", out var x) && x.ValueKind == JsonValueKind.Object))
            {
                var field = entry.GetProperty("field");
                string title = Text(field, "title");
                int options = field.TryGetProperty("selectableValues", out var values) && values.ValueKind == JsonValueKind.Array ? values.GetArrayLength() : 0;
                string kind = Text(field, "type") switch
                {
                    "LongText" => FormQuestion.LongText,
                    "File" when CoverLetter().IsMatch(title + " " + Text(field, "path")) => FormQuestion.CoverLetter,
                    "File" => FormQuestion.File,
                    "Boolean" => FormQuestion.YesNo,
                    "ValueSelect" or "MultiValueSelect" => FormQuestion.Choice,
                    _ => FormQuestion.Text,
                };
                bool required = entry.TryGetProperty("isRequired", out var r) && r.ValueKind == JsonValueKind.True;
                form.Add(new FormQuestion(title, kind, required, options, Plain(Text(entry, "descriptionHtml"))));
            }
        }
        return form;
    }

    /// <summary>Lever's apply page to its question cards' fields; a page with no cards asks nothing beyond the standard form.</summary>
    public static IReadOnlyList<FormQuestion> ReadLever(string html)
    {
        var form = new List<FormQuestion>();
        foreach (Match card in Template().Matches(html ?? string.Empty))
        {
            using var doc = JsonDocument.Parse(WebUtility.HtmlDecode(card.Groups["value"].Value));
            string heading = Text(doc.RootElement, "text");
            if (!doc.RootElement.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var field in fields.EnumerateArray())
            {
                int options = field.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array ? o.GetArrayLength() : 0;
                string kind = Text(field, "type") switch
                {
                    "textarea" => FormQuestion.LongText,
                    "multiple-choice" or "multiple-select" or "dropdown" => FormQuestion.Choice,
                    "file-upload" => FormQuestion.File,
                    _ => FormQuestion.Text,
                };
                form.Add(new FormQuestion(Text(field, "text"), kind, field.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True, options, heading));
            }
        }
        return form;
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static string Plain(string html) => Spaces().Replace(WebUtility.HtmlDecode(Tag().Replace(html, " ")), " ").Trim();

    [GeneratedRegex(@"<input\b[^>]*?\bvalue=""(?<value>[^""]*)""[^>]*?\bname=""cards\[[^\]]+\]\[baseTemplate\]""|<input\b[^>]*?\bname=""cards\[[^\]]+\]\[baseTemplate\]""[^>]*?\bvalue=""(?<value>[^""]*)""")]
    private static partial Regex Template();

    [GeneratedRegex(@"cover[ _-]?letter", RegexOptions.IgnoreCase)]
    private static partial Regex CoverLetter();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
