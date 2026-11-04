using System.Net;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>A rendered follow-up: the subject, the plain body and the HTML body in Gmail's own shape.</summary>
/// <param name="Subject">Re: and the original subject.</param>
/// <param name="Text">The plain body.</param>
/// <param name="Html">The HTML body, a div a line.</param>
public sealed record FollowUpDraft(string Subject, string Text, string Html);

/// <summary>
/// FOLLOWUP-v1, the one warm follow-up, as code: it changes only with Steve's yes, and a new version is a new
/// constant. Render fills it; Check is what a draft must pass before it is written anywhere: no dash of the
/// model's kind, none of the tells, no blank, one question, and a subject on one line.
/// </summary>
public static class DraftTemplate
{
    /// <summary>The template's name and version.</summary>
    public const string Version = "FOLLOWUP-v1";

    /// <summary>The body, with its three variables.</summary>
    public const string Body = "Hi {FirstName},\n\nIt's been a few weeks since we talked about {TopicLine}.\n\nWould it be a bad idea to catch up for 15 minutes in the next couple of weeks?\n\nSteve";

    /// <summary>The draft for a person, a topic and the thread's subject.</summary>
    public static FollowUpDraft Render(string firstName, string topicLine, string subject)
    {
        string topic = (topicLine ?? string.Empty).Trim().TrimEnd('.', '!', '?', ';', ',').Trim();
        string text = Body.Replace("{FirstName}", (firstName ?? string.Empty).Trim(), StringComparison.Ordinal).Replace("{TopicLine}", topic, StringComparison.Ordinal);
        string html = string.Concat(text.Split('\n').Select(line => line.Length == 0 ? "<div><br></div>" : $"<div>{WebUtility.HtmlEncode(line)}</div>"));
        return new FollowUpDraft(Subject(subject), text, html);
    }

    /// <summary>Re: and the original subject, with an existing Re: not doubled.</summary>
    public static string Subject(string original)
    {
        string s = (original ?? string.Empty).Trim();
        while (s.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
        {
            s = s[3..].TrimStart();
        }
        return "Re: " + s;
    }

    /// <summary>Every check the draft fails, empty when it may be written.</summary>
    public static IReadOnlyList<string> Check(FollowUpDraft draft)
    {
        var failed = new List<string>();
        string all = draft.Subject + "\n" + draft.Text;
        if (all.Contains('\u2014', StringComparison.Ordinal) || all.Contains('\u2013', StringComparison.Ordinal))
        {
            failed.Add("dash");
        }
        if (Tells.Find(draft.Text).Count > 0)
        {
            failed.Add("tell");
        }
        if (draft.Text.Contains('{', StringComparison.Ordinal) || draft.Text.StartsWith("Hi ,", StringComparison.Ordinal) || draft.Text.Contains("about .", StringComparison.Ordinal))
        {
            failed.Add("blank");
        }
        if (draft.Text.Count(c => c == '?') != 1)
        {
            failed.Add("question");
        }
        if (draft.Subject.IndexOfAny(['\r', '\n']) >= 0 || draft.Subject.Trim() == "Re:" || !draft.Subject.StartsWith("Re: ", StringComparison.Ordinal))
        {
            failed.Add("subject");
        }
        return failed;
    }
}
