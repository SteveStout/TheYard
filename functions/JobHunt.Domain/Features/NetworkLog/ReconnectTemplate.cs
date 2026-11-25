using System.Globalization;
using System.Net;
using JobHunt.Domain.Shared.Applications;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>Which second question an email reconnect asks.</summary>
public enum ReconnectQuestion
{
    /// <summary>The conversation was about a role that is still live: "Do you have any status?".</summary>
    Status,

    /// <summary>No specific role, or it closed, moved, or was onsite or hybrid: the remote question.</summary>
    Remote,
}

/// <summary>
/// RECONNECT-v1, the email reconnect in Steve's own shape: the first name, the topic, the
/// question whether to stay in contact, one second question, the six portfolio links and his sign-off. No greeting
/// word, no blog line and no ask for a call. The HTML is Gmail's own div a line, the links a bulleted list of bold
/// labels that never print an address; the plain part says the same lines with each address after its label. Every
/// link carries the send's one code, the recommendations page through its counted pass-through. It changes only with
/// Steve's yes, and a new version is a new constant.
/// </summary>
public static class ReconnectTemplate
{
    /// <summary>The template's name and version.</summary>
    public const string Version = "RECONNECT-v1";

    /// <summary>The question for a live role.</summary>
    public const string StatusQuestion = "Do you have any status?";

    /// <summary>The question for everything else, and the one asked when unsure.</summary>
    public const string RemoteQuestion = "Has anything fully remote come across your desk?";

    /// <summary>The question every reconnect asks first.</summary>
    public const string StayInContact = "Is it a bad idea to stay in contact?";

    /// <summary>The six links in their order, each carrying the code.</summary>
    /// <param name="code">The send's six-character code.</param>
    public static IReadOnlyList<(string Label, string Url)> Links(string code) =>
    [
        ("Resume", $"{BlogLine.Site}/?view=review&r={code}"),
        ("Site", $"{BlogLine.Site}/?r={code}"),
        ("GitHub", $"{BlogLine.Site}/api/go/github?r={code}"),
        ("LinkedIn", $"{BlogLine.Site}/api/go/linkedin?r={code}"),
        ("About", $"{BlogLine.Site}/?doc=author&r={code}"),
        ("Recommendations", $"{BlogLine.Site}/api/go/linkedin-recs?r={code}"),
    ];

    /// <summary>The reconnect for a person, a topic, the thread's subject, the second question and the send's code.</summary>
    public static FollowUpDraft Render(string firstName, string topicLine, string subject, ReconnectQuestion question, string code)
    {
        string name = (firstName ?? string.Empty).Trim();
        string topic = (topicLine ?? string.Empty).Trim().TrimEnd('.', '!', '?', ';', ',').Trim();
        string second = question == ReconnectQuestion.Status ? StatusQuestion : RemoteQuestion;
        var links = Links(code);
        string[] head = [$"{name},", "", $"It's been a few weeks since we talked about {topic}.", "", StayInContact, "", second, ""];
        string[] tail = ["", "Thanks,", "Steve"];
        string text = string.Join("\n", [.. head, "Portfolio", .. links.Select(l => $"{l.Label} -> {l.Url}"), .. tail]);
        string html = string.Concat(head.Select(Div)) + "<div><b>Portfolio</b></div><ul>"
            + string.Concat(links.Select(l => $"<li><a href=\"{WebUtility.HtmlEncode(l.Url)}\"><b>{l.Label}</b></a></li>")) + "</ul>"
            + string.Concat(tail.Select(Div));
        return new FollowUpDraft(DraftTemplate.Subject(subject), text, html);
    }

    /// <summary>
    /// The second question from the tracker: the status question only while an application to the company is open,
    /// one in the last 60 days with no REJECTION row after it; the remote question for everything else and when the
    /// company is not known.
    /// </summary>
    /// <param name="company">The row's company or its domain.</param>
    /// <param name="master">The tracker's rows.</param>
    /// <param name="today">The Central day.</param>
    public static ReconnectQuestion QuestionFor(string? company, IReadOnlyList<string[]> master, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(company) || ApplicationCap.Applications(company, master, today) is not { Count: > 0 } open)
        {
            return ReconnectQuestion.Remote;
        }
        int newest = open.Max(a => Count(a.Count));
        bool rejected = master.Any(row => row.Length >= 7 && ApplicationCap.Same(row[2], company) && row[6].TrimStart().StartsWith("REJECTION", StringComparison.OrdinalIgnoreCase) && Count(row[0]) > newest);
        return rejected ? ReconnectQuestion.Remote : ReconnectQuestion.Status;
    }

    private static int Count(string cell) => int.TryParse(cell.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;

    private static string Div(string line) => line.Length == 0 ? "<div><br></div>" : $"<div>{WebUtility.HtmlEncode(line)}</div>";

    /// <summary>Every check the reconnect fails, empty when it may go: FOLLOWUP-v1's dash, tell, blank and subject checks, two questions, and every link carrying the code.</summary>
    /// <param name="draft">The rendered reconnect.</param>
    /// <param name="code">The code its links must carry.</param>
    public static IReadOnlyList<string> Check(FollowUpDraft draft, string code)
    {
        var failed = DraftTemplate.Check(draft with { Text = Questionless(draft.Text) + "?" }).ToList();
        if (draft.Text.StartsWith(",", StringComparison.Ordinal) || draft.Text.Contains("about .", StringComparison.Ordinal))
        {
            failed.Add("blank");
        }
        if (!draft.Text.Contains(StayInContact, StringComparison.Ordinal) || !(draft.Text.Contains(StatusQuestion, StringComparison.Ordinal) ^ draft.Text.Contains(RemoteQuestion, StringComparison.Ordinal)))
        {
            failed.Add("questions");
        }
        if (code.Length != 6 || Links(code).Any(l => !draft.Text.Contains(l.Url, StringComparison.Ordinal) || !draft.Html.Contains(WebUtility.HtmlEncode(l.Url), StringComparison.Ordinal))
            || draft.Html.Contains(">http", StringComparison.Ordinal))
        {
            failed.Add("links");
        }
        return [.. failed.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The text with the two questions and the link lines taken out, so FOLLOWUP-v1's one-question check reads the rest.</summary>
    private static string Questionless(string text) =>
        string.Join("\n", text.Split('\n').Where(line => line != StayInContact && line != StatusQuestion && line != RemoteQuestion && !line.Contains(" -> ", StringComparison.Ordinal)));
}
