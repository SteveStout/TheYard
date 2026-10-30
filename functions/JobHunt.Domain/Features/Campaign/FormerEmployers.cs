namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// The five places the applicant has worked, which the campaign never writes to. They are listed here rather
/// than left to the do-not-apply list because that list holds a company and a date it may be approached again,
/// which is a different question: a former employer is not a company to cold-email at all, ever, so it needs
/// no date and gets none. The names are the resume's headings together with the variants the tracker uses
/// ("Former Conway Investment Solutions coworker", "Storee Construction (network)"), because the match has to
/// catch the name as either document writes it. The domains are each company's own well-known address or one
/// read off its public business profile, which is the only source a campaign may use. The do-not-apply list is
/// still checked on top by the send rule, so this list narrows and never widens.
/// </summary>
public static class FormerEmployers
{
    /// <summary>
    /// Each former employer: the resume's name, the names it also goes by, and its mail domains. The domains
    /// are here as well as the names because a lead carries an address and often no company name the match
    /// could read.
    /// </summary>
    public static readonly IReadOnlyList<FormerEmployer> All =
    [
        new("Storee Construction", ["Storee"], ["storee.com"]),
        new("Conway Investment Research", ["Conway Investment Solutions", "Conway"], ["conwayis.com"]),
        new("Cass Information Systems", ["Cass"], ["cassinfo.com"]),
        new("Hunter Engineering", ["Hunter Engineering Company"], ["hunter.com"]),
        new("State of Missouri", ["Missouri State Government"], ["mo.gov"]),
    ];

    /// <summary>
    /// The former employer a company or an address belongs to, or null. A name matches the way Rule 50 matches
    /// (ApplicationCap.Same, so "Cass Information Systems Inc" is Cass), and a domain matches by its key, so
    /// dor.mo.gov and mail.storee.com are the employer too.
    /// </summary>
    public static FormerEmployer? Of(string? company, string? domain)
    {
        string? key = string.IsNullOrWhiteSpace(domain) ? null : CompanyKey.Of(domain);
        return All.FirstOrDefault(employer =>
            (!string.IsNullOrWhiteSpace(company) && employer.Names.Any(name => ApplicationCap.Same(name, company)))
            || (key is not null && employer.Domains.Contains(key, StringComparer.Ordinal)));
    }
}

/// <summary>One former employer: its resume name first in <see cref="Names"/>, then its variants.</summary>
public sealed record FormerEmployer(string Name, IReadOnlyList<string> Variants, IReadOnlyList<string> Domains)
{
    /// <summary>The resume name and every variant.</summary>
    public IReadOnlyList<string> Names { get; } = [Name, .. Variants];
}
