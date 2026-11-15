using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>One question of a posting's application form, as a board's form reader classed it.</summary>
/// <param name="Label">The question as the form writes it.</param>
/// <param name="Kind">One of the kinds below.</param>
/// <param name="Required">True when the form will not submit without it.</param>
/// <param name="Options">How many answers a choice offers; one is a checkbox to tick.</param>
/// <param name="Description">The text under the label, plain, or empty.</param>
public sealed record FormQuestion(string Label, string Kind, bool Required, int Options = 0, string Description = "")
{
    /// <summary>A one-line answer: a name, a city, a salary, a link.</summary>
    public const string Text = "text";

    /// <summary>A long free-text answer: an essay.</summary>
    public const string LongText = "long text";

    /// <summary>A file to attach: the resume.</summary>
    public const string File = "file";

    /// <summary>A cover letter, as a file or as text.</summary>
    public const string CoverLetter = "cover letter";

    /// <summary>A pick among options.</summary>
    public const string Choice = "choice";

    /// <summary>A yes or no toggle.</summary>
    public const string YesNo = "yes/no";
}

/// <summary>
/// What a READY row's form asks that no bank answers, in the words the row carries so the card and the apply lane see
/// it before 06:00: a required essay ("FORM: essay required", or how many), a required cover letter, and each required
/// acknowledgement or agreement with its text. An acknowledgement is a required choice of one option (a box to tick),
/// or a required choice or yes/no whose label is a statement rather than a question and acknowledges, agrees,
/// consents, certifies, attests or understands, in its label or the text under it. An essay's label is never copied,
/// because the label is where a planted instruction sits; an acknowledgement's is, cut to 160 characters, its dashes
/// and bars made plain for the jobs file's tables. Whether to tick it or what to write is Steve's, never the picker's.
/// </summary>
public static partial class FormNotes
{
    /// <summary>The note for a form that could not be read.</summary>
    public const string NotRead = "FORM: not read";

    /// <summary>The longest acknowledgement text a note carries.</summary>
    public const int MaxText = 160;

    /// <summary>The row's notes, joined by "; ", empty when nothing required needs Steve; <see cref="NotRead"/> for a null form.</summary>
    public static string Of(IReadOnlyList<FormQuestion>? form)
    {
        if (form is null)
        {
            return NotRead;
        }
        var required = form.Where(q => q.Required).ToList();
        var notes = new List<string>();
        int essays = required.Count(q => q.Kind == FormQuestion.LongText);
        if (essays > 0)
        {
            notes.Add(essays == 1 ? "FORM: essay required" : $"FORM: {essays} essays required");
        }
        if (required.Any(q => q.Kind == FormQuestion.CoverLetter))
        {
            notes.Add("FORM: cover letter required");
        }
        notes.AddRange(required.Where(IsAcknowledgement).Select(q => "FORM: acknowledgement " + Plain(Ack().IsMatch(q.Label) || q.Description.Length == 0 ? q.Label : q.Label.Trim() + ": " + q.Description)));
        return string.Join("; ", notes);
    }

    /// <summary>True when the question is a box to tick or a statement to agree to.</summary>
    public static bool IsAcknowledgement(FormQuestion q) =>
        (q.Kind == FormQuestion.Choice && q.Options == 1)
        || (q.Kind is FormQuestion.Choice or FormQuestion.YesNo && !q.Label.TrimEnd().EndsWith('?') && Ack().IsMatch(q.Label + " " + q.Description));

    private static string Plain(string text)
    {
        string flat = Spaces().Replace(text.Replace('\u2014', '-').Replace('\u2013', '-').Replace('|', '/').Replace('\u00a0', ' '), " ").Trim();
        if (flat.Length <= MaxText)
        {
            return flat;
        }
        int cut = flat.LastIndexOf(' ', MaxText);
        return flat[..(cut > 0 ? cut : MaxText)].TrimEnd(' ', ',', ';', '.') + "...";
    }

    [GeneratedRegex(@"\b(acknowledg\w*|agree|agrees|agreed|consent\w*|certif\w*|attest\w*|understand)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Ack();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
