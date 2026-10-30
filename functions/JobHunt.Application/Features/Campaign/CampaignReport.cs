using System.Text;

namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// The shadow week's daily report (ADR-118, Decision 7; the prompt's rung 7): each morning's pass writes one file
/// to campaign/shadow/ on the share with the day's counts, every hold by its reason, the addresses found by the
/// kind of public page they came from, and every would-send whole, so the would-sends are read against the rules
/// every morning by code and by Steve's eyes, with no session needed to collect them. The share is private; the
/// card, which is public, carries counts only.
/// </summary>
public static class CampaignReport
{
    /// <summary>The folder on the share.</summary>
    public const string Folder = "campaign/shadow";

    /// <summary>The kind of public page an address came from, by its host: the five kinds the lead run may use.</summary>
    /// <param name="sourceUrl">The page's address.</param>
    /// <param name="companyKey">The company's key, so its own site is named as such.</param>
    public static string SourceKind(string sourceUrl, string companyKey)
    {
        string host = Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";
        string key = CompanyKey.Of(host);
        return host.Length == 0 ? "unreadable"
            : host is "github.com" or "gist.github.com" ? "GitHub"
            : host.Contains("greenhouse.io", StringComparison.Ordinal) || host.Contains("lever.co", StringComparison.Ordinal) || host.Contains("ashbyhq.com", StringComparison.Ordinal) || host.Contains("workday", StringComparison.Ordinal) ? "the posting"
            : key == companyKey ? "the company's site"
            : host.Contains("linkedin.com", StringComparison.Ordinal) ? "a public profile"
            : "another public page";
    }

    /// <summary>The file on the share every real send is reported in, one Rule 46 block a send.</summary>
    public const string SentLog = "campaign/sent.log";

    /// <summary>
    /// Each real send of a pass as Rule 46 in the job hunt's systemRules.md asks for it: a three-line block,
    /// the markers on lines of their own, SENT, TO and SUBJECT in bold. The shape is the point. A pass that
    /// sent is read later by a person scanning a log for who it wrote to, and a block whose boundaries are on
    /// their own lines can be found and counted by eye without the log being parsed.
    /// </summary>
    /// <param name="result">The pass, in on.</param>
    public static string Sent(CampaignPassResult result) => string.Concat(result.Emails.Select(e =>
        $"--\r\n**SENT** - **TO:** {e.Lead.ContactName} {e.Lead.ContactEmail} - **SUBJECT:** {e.Email.Subject}\r\n--\r\n"));

    /// <summary>The folder make_ref.py spools tagged-link rows to, which opens_report.py folds into link-tokens.csv.</summary>
    public const string LinkTokenSpool = "inbox/link-tokens";

    /// <summary>
    /// One spool file a real send, the review's F17: the lead run mints the code when it writes the lead (Steve's rule,
    /// make_ref.py the one minter), and the send appends a row with the same code and the true send time, so
    /// opens_report.py, which keeps the latest row a code, labels the opens by the day the email went. The Function
    /// mints nothing and never writes link-tokens.csv; a lead with no code, or an email Gmail did not take (shadow), writes no row.
    /// </summary>
    /// <param name="result">The pass, in on.</param>
    /// <param name="sentAt">When the pass sent.</param>
    public static IReadOnlyList<(string Path, string Text)> TokenRows(CampaignPassResult result, DateTimeOffset sentAt)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string stamp = sentAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", inv) + "Z";
        string sent = sentAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", inv) + "Z";
        return result.Emails.Where(e => !string.IsNullOrWhiteSpace(e.Lead.RefCode) && result.Threads.ContainsKey(e.Lead.Id)).Select(e =>
            ($"{LinkTokenSpool}/{stamp}-campaign-{e.Lead.RefCode}.csv",
             "code,person,company,channel,role,thread,sent,lane\r\n" + TrackerCsvText.Line([
                 e.Lead.RefCode, e.Lead.ContactName, e.Lead.CompanyName, "Email", e.Lead.RoleTitle ?? "",
                 result.Threads[e.Lead.Id], sent, "campaign"]) + "\r\n")).ToList();
    }

    /// <summary>The report's text for one morning.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="result">The pass.</param>
    public static string Write(DateOnly day, CampaignPassResult result)
    {
        var text = new StringBuilder();
        text.Append("# Campaign shadow report ").Append(day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append("\n\n");
        text.Append("Written by the Function's 8 AM pass in shadow: nothing below was sent. ").Append(result.Line).Append("\n\n");
        text.Append("## Addresses found, by the kind of public page\n\n");
        text.Append(result.AddressesBySource.Count == 0 ? "- none yet\n" : string.Concat(result.AddressesBySource.OrderByDescending(a => a.Value).Select(a => $"- {a.Key}: {a.Value}\n")));
        text.Append("\n## Held, by the first rule each broke\n\n");
        text.Append(result.Held.Count == 0 ? "- none\n" : string.Concat(result.Held.OrderByDescending(h => h.Value).Select(h => $"- {h.Key}: {h.Value}\n")));
        text.Append("\n## The would-sends, whole\n");
        foreach (var (lead, email) in result.Emails)
        {
            text.Append("\n### ").Append(lead.CompanyName).Append(", track ").Append(lead.Track).Append(", to ").Append(lead.ContactName).Append(" (").Append(lead.ContactTitle).Append(")\n\n");
            text.Append("- To: ").Append(lead.ContactEmail).Append(", read from ").Append(lead.EmailSource).Append('\n');
            text.Append("- Subject: ").Append(email.Subject).Append("\n\n");
            text.Append(email.Text).Append('\n');
        }
        return text.ToString();
    }
}
