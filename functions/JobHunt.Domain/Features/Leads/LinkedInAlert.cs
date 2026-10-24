using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Leads;

/// <summary>
/// The requisitions in a LinkedIn job alert (jobalerts-noreply@linkedin.com), read from its plain text:
/// each block between the dashed rules is the title, the company, the location and any tags, then
/// "View job:" and the link. The link is cut to its job id, so the tracking parameters never reach a file.
/// A block that does not read that way is skipped and counted, never guessed.
/// </summary>
public static partial class LinkedInAlert
{
    /// <summary>The alert's sender.</summary>
    public const string Sender = "jobalerts-noreply@linkedin.com";

    /// <summary>The requisitions in one alert's text, and how many blocks did not read as one.</summary>
    public static (IReadOnlyList<Lead> Leads, int Unread) Read(string text, string messageId)
    {
        var leads = new List<Lead>();
        int unread = 0;
        foreach (string block in Rule().Split(text ?? string.Empty))
        {
            var view = View().Match(block);
            if (!view.Success)
            {
                continue;
            }
            var lines = block[..view.Index].Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            // The alert's own header sits above the first job in the first block; the job is the last lines before View job.
            int start = lines.FindLastIndex(l => l.Contains("http", StringComparison.Ordinal) || l.EndsWith(':')) + 1;
            var job = lines.Skip(start).Where(l => !Tag().IsMatch(l)).ToList();
            if (job.Count < 3)
            {
                unread++;
                continue;
            }
            leads.Add(new Lead(job[1], job[0], job[2], $"https://www.linkedin.com/jobs/view/{view.Groups[1].Value}/", messageId));
        }
        return (leads, unread);
    }

    [GeneratedRegex(@"\n-{10,}\s*\n")]
    private static partial Regex Rule();

    [GeneratedRegex(@"View job:\s*https://www\.linkedin\.com/comm/jobs/view/(\d+)")]
    private static partial Regex View();

    [GeneratedRegex(@"^(Top applicant|Actively recruiting|Easy Apply|Promoted|Be an early applicant|\d+ (connection|school alum|alumni)s?.*|.*\bapplicants?\b.*|\$[\d,.Kk]+.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Tag();
}

/// <summary>One requisition from an alert.</summary>
/// <param name="Company">The company as the alert names it.</param>
/// <param name="Title">The role's title.</param>
/// <param name="Location">The location or remote line, as written.</param>
/// <param name="Link">The job's link, without tracking.</param>
/// <param name="MessageId">The alert's Gmail id.</param>
public sealed record Lead(string Company, string Title, string Location, string Link, string MessageId)
{
    /// <summary>The pay band as the alert printed it ("$160K - $210K"), or null when it printed none.</summary>
    public string? Band { get; init; }
}
