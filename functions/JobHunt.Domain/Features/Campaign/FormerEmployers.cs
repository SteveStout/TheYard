namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// The five places Steve has worked, which the campaign never writes to (the review of the email campaign,
/// F1: the do-not-apply list carries one company and a date, and none of these). The names are the resume's
/// headings and the variants the tracker uses ("Former Conway Investment Solutions coworker", "Storee
/// Construction (network)"); the domains were read off public profiles on 2026-10-24 (owler.com/company/storee,
/// privateequityinternational.com's Conway Investment Solutions profile) or are the companies' own well-known
/// addresses. There is no date: a former employer stays out for good, and the do-not-apply list is still checked
/// on top by the send rule.
/// </summary>
public static class FormerEmployers
{
    /// <summary>Each former employer: the resume's name, the names it also goes by, and its mail domains.</summary>
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
