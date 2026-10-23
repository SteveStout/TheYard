using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using JobHunt.Functions.Ats;

namespace JobHunt.Functions.Leads;

/// <summary>
/// One job board's alert mail: which sender it comes from, and how its jobs are read out of it. The leads
/// harvest asks every reader in <see cref="BoardAlerts.All"/>, so a new board is one class and one line there
/// (PROMPT-JOBHUNT-OFF-THE-LAPTOP rung 2: every board's alert is harvested before it is archived).
/// </summary>
public interface IBoardAlert
{
    /// <summary>The board's name, for the log and the leads file.</summary>
    string Board { get; }

    /// <summary>The Gmail search clause that finds this board's alerts still in the inbox.</summary>
    string Query { get; }

    /// <summary>True when a From address is this board's alert sender, so a thread with anybody else in it is left alone.</summary>
    bool Sends(string address);

    /// <summary>The jobs in one alert, and how many blocks did not read as a job; a block is skipped and counted, never guessed.</summary>
    (IReadOnlyList<Lead> Leads, int Unread) Read(InboxMessage message);
}

/// <summary>Every board the harvest reads, in the order it reads them.</summary>
public static partial class BoardAlerts
{
    /// <summary>The readers: LinkedIn's digest, LinkedIn's single-job nudges, Ladders, and SuccessFactors (jobs2web) agents.</summary>
    public static readonly IReadOnlyList<IBoardAlert> All = [new LinkedInDigest(), new LinkedInNudge(), new LaddersAlert(), new Jobs2WebAlert()];

    /// <summary>The highest figure in a band such as "$160K - $210K*", in thousands, or null when no band is printed.</summary>
    public static int? TopOfBand(string? band)
    {
        var figures = Thousands().Matches(band ?? string.Empty).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        return figures.Count == 0 ? null : figures.Max();
    }

    [GeneratedRegex(@"\$(\d{2,3})K", RegexOptions.IgnoreCase)]
    private static partial Regex Thousands();
}

/// <summary>LinkedIn's daily digest, read by <see cref="LinkedInAlert"/> as before.</summary>
public sealed class LinkedInDigest : IBoardAlert
{
    /// <inheritdoc/>
    public string Board => "LinkedIn digest";

    /// <inheritdoc/>
    public string Query => $"from:{LinkedInAlert.Sender}";

    /// <inheritdoc/>
    public bool Sends(string address) => address == LinkedInAlert.Sender;

    /// <inheritdoc/>
    public (IReadOnlyList<Lead> Leads, int Unread) Read(InboxMessage message) =>
        LinkedInAlert.Read(message.Lines.Length > 0 ? message.Lines : message.Text, message.Id);
}

/// <summary>
/// LinkedIn's single-job nudges from jobs-noreply@linkedin.com, by subject: "&lt;Company&gt; is hiring for a
/// &lt;Role&gt; role" and "Steven, apply now to '&lt;Title&gt; at &lt;Company&gt;'". The job is the first job link in
/// the body, cut to its id. The same sender's "Your application was viewed by" and "your application was sent
/// to" mails are not alerts: they are not read here, so they stay for the ATS sweep and the triage run.
/// </summary>
public sealed partial class LinkedInNudge : IBoardAlert
{
    /// <summary>The nudges' sender.</summary>
    public const string Sender = "jobs-noreply@linkedin.com";

    /// <inheritdoc/>
    public string Board => "LinkedIn nudge";

    /// <inheritdoc/>
    public string Query => $"from:{Sender} (subject:\"is hiring for\" OR subject:\"apply now to\")";

    /// <inheritdoc/>
    public bool Sends(string address) => address == Sender;

    /// <inheritdoc/>
    public (IReadOnlyList<Lead> Leads, int Unread) Read(InboxMessage message)
    {
        string subject = Quotes().Replace(message.Subject ?? string.Empty, "'");
        var job = JobLink().Match(message.Lines + "\n" + message.Text + "\n" + message.Html);
        string? company = null, title = null;
        if (ApplyNow().Match(subject) is { Success: true } apply)
        {
            (title, company) = (apply.Groups["title"].Value.Trim(), apply.Groups["company"].Value.Trim());
        }
        else if (Hiring().Match(subject) is { Success: true } hiring)
        {
            (title, company) = (hiring.Groups["role"].Value.Trim() + " role", hiring.Groups["company"].Value.Trim());
        }
        if (company is null || title is null || !job.Success)
        {
            return ([], 1);
        }
        // A nudge names no location in its subject; the body's own words decide remote, read the way the filter reads it.
        string location = RemoteWord().IsMatch(message.Text) || RemoteWord().IsMatch(title) ? "Remote (named in the nudge)" : "not named in the nudge";
        return ([new Lead(company, title, location, $"https://www.linkedin.com/jobs/view/{job.Groups[1].Value}/", message.Id)], 0);
    }

    [GeneratedRegex(@"[‘’“”""]")]
    private static partial Regex Quotes();

    [GeneratedRegex(@"apply now to '(?<title>.+) at (?<company>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex ApplyNow();

    [GeneratedRegex(@"^(?<company>.+?) is hiring for an? (?<role>.+?) role$", RegexOptions.IgnoreCase)]
    private static partial Regex Hiring();

    [GeneratedRegex(@"linkedin\.com/(?:comm/)?jobs/view/(\d+)")]
    private static partial Regex JobLink();

    [GeneratedRegex(@"\bremote\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteWord();
}

/// <summary>
/// Ladders' alerts from jobs@my.theladders.com. Each job reads "&lt;Title&gt; $160K - $210K* | &lt;Company&gt; |
/// &lt;Location&gt; Apply Now" once the markup is gone; a company Ladders names in two parts ("DecisionPoint |
/// Cortek") keeps both. Ladders' links are tracking redirects with no job id, so the link written is a search
/// on Ladders for the title and the company, which opens the job without carrying anybody's tracking.
/// </summary>
public sealed partial class LaddersAlert : IBoardAlert
{
    /// <summary>The alerts' sender.</summary>
    public const string Sender = "jobs@my.theladders.com";

    /// <inheritdoc/>
    public string Board => "Ladders";

    /// <inheritdoc/>
    public string Query => $"from:{Sender}";

    /// <inheritdoc/>
    public bool Sends(string address) => address == Sender;

    /// <inheritdoc/>
    public (IReadOnlyList<Lead> Leads, int Unread) Read(InboxMessage message)
    {
        string text = Space().Replace(Links().Replace(WebUtility.HtmlDecode(message.Text), " "), " ");
        var leads = new List<Lead>();
        int unread = 0;
        foreach (Match job in Job().Matches(text))
        {
            string title = Preamble().Replace(job.Groups["title"].Value, string.Empty).Trim(' ', '|');
            var parts = job.Groups["rest"].Value.Split('|').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (title.Length < 3 || parts.Count < 2)
            {
                unread++;
                continue;
            }
            string company = string.Join(" / ", parts.Take(parts.Count - 1));
            string search = Uri.EscapeDataString($"{title} {company}");
            leads.Add(new Lead(company, title, parts[^1], $"https://www.theladders.com/jobs/searchresults-jobs?keywords={search}", message.Id)
            {
                Band = $"${job.Groups["lo"].Value}K - ${job.Groups["hi"].Value}K",
            });
        }
        return (leads, leads.Count == 0 ? Math.Max(unread, 1) : unread);
    }

    [GeneratedRegex(@"\[\]\([^)]*\)|\(https?://[^)\s]*\)|<https?://[^>\s]*>|https?://\S+")]
    private static partial Regex Links();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    [GeneratedRegex(@"(?<title>[^$|]{3,200}?)\s*\$(?<lo>\d{2,3})K\s*-\s*\$(?<hi>\d{2,3})K\*?\s*\|(?<rest>[^$]*?)\bApply Now\b")]
    private static partial Regex Job();

    [GeneratedRegex(@"^.*\b(?:Apply Now|Hours|first and apply today!)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Preamble();
}

/// <summary>
/// A SuccessFactors career-site agent (jobs2web), from &lt;company&gt;-jobnotification@noreply.jobs2web.com: "The
/// following jobs matched your search agent at &lt;Company&gt;", then one link a job whose text is "&lt;Title&gt; -
/// &lt;City&gt;, &lt;State&gt;, &lt;Country&gt;, &lt;Zip&gt;". Read from the HTML part, because the mail has no plain part;
/// the link is cut at its query string, so the tracking parameters never reach a file.
/// </summary>
public sealed partial class Jobs2WebAlert : IBoardAlert
{
    /// <summary>The domain every agent sends from.</summary>
    public const string Domain = "noreply.jobs2web.com";

    /// <inheritdoc/>
    public string Board => "SuccessFactors agent";

    /// <inheritdoc/>
    public string Query => $"from:{Domain}";

    /// <inheritdoc/>
    public bool Sends(string address) => Mail.Domain(address) == Domain;

    /// <inheritdoc/>
    public (IReadOnlyList<Lead> Leads, int Unread) Read(InboxMessage message)
    {
        string html = message.Html;
        string company = WebUtility.HtmlDecode(Company().Match(html.Length > 0 ? html : message.Text).Groups[1].Value).Trim();
        var leads = new List<Lead>();
        int unread = 0;
        foreach (Match anchor in JobAnchor().Matches(html))
        {
            string text = WebUtility.HtmlDecode(Tags().Replace(anchor.Groups["text"].Value, string.Empty)).Trim();
            int dash = text.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dash <= 0 || company.Length == 0)
            {
                unread++;
                continue;
            }
            string href = WebUtility.HtmlDecode(anchor.Groups["href"].Value);
            int query = href.IndexOf('?', StringComparison.Ordinal);
            leads.Add(new Lead(company, text[..dash].Trim(), text[(dash + 3)..].Trim(), query > 0 ? href[..query] : href, message.Id));
        }
        return (leads, leads.Count == 0 ? Math.Max(unread, 1) : unread);
    }

    [GeneratedRegex(@"matched your search agent at (.+?) and can be found", RegexOptions.Singleline)]
    private static partial Regex Company();

    [GeneratedRegex(@"<a\s+class=""agentjoblink""\s+href=""(?<href>[^""]+)""[^>]*>(?<text>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JobAnchor();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();
}
