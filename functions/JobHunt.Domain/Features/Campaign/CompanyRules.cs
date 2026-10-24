namespace JobHunt.Domain.Features.Campaign;

/// <summary>A company as the campaign keys it: its key, its parent's key when one is known, and its names.</summary>
public sealed record CampaignCompany(string Key, string? ParentKey, IReadOnlyList<string> Names);

/// <summary>One email already sent (or written in shadow), follow-ups included, on its Central day.</summary>
public sealed record PastSend(string Key, string? ParentKey, string Company, DateOnly Day);

/// <summary>A rule's answer: allowed, or held with the reason the card and the pass log name.</summary>
public sealed record CampaignVerdict(bool Allowed, string Reason)
{
    /// <summary>The one allowed answer.</summary>
    public static CampaignVerdict Pass { get; } = new(true, "allowed");

    /// <summary>A held answer with its reason.</summary>
    public static CampaignVerdict Hold(string reason) => new(false, reason);
}

/// <summary>
/// One email a company a rolling 30 days (the review, F10), follow-ups included. The same company is the same
/// key (registrable domain), the same parent key (CoverMyMeds under McKesson, acme.com and acme.io under one
/// parent), or the same name the way Rule 50 matches names (Upstart and Upstart Network). The window is 30 full
/// days: a send on day 0 holds days 0 to 30, and day 31 is the first allowed.
/// </summary>
public static class ThirtyDayRule
{
    /// <summary>The window in days, the send's own day not counted.</summary>
    public const int WindowDays = 30;

    /// <summary>Allowed, or held naming the send that holds it and the first day it opens.</summary>
    public static CampaignVerdict Check(CampaignCompany company, IReadOnlyList<PastSend> sends, DateOnly today)
    {
        var holding = sends
            .Where(send => today.DayNumber - send.Day.DayNumber is >= 0 and <= WindowDays && SameCompany(company, send))
            .OrderByDescending(send => send.Day)
            .FirstOrDefault();
        return holding is null
            ? CampaignVerdict.Pass
            : CampaignVerdict.Hold($"30-day rule: emailed {holding.Day:yyyy-MM-dd}, opens {holding.Day.AddDays(WindowDays + 1):yyyy-MM-dd}");
    }

    /// <summary>Whether a past send was to the same company by key, parent key or name.</summary>
    public static bool SameCompany(CampaignCompany company, PastSend send)
    {
        bool key = company.Key.Length > 0 && (company.Key == send.Key || company.Key == send.ParentKey);
        bool parent = company.ParentKey is { Length: > 0 } p && (p == send.Key || p == send.ParentKey);
        bool name = company.Names.Any(n => ApplicationCap.Same(n, send.Company));
        return key || parent || name;
    }
}

/// <summary>
/// The company rules in the order the send handler reads them (the review, F1 then F10): a former employer
/// first and named as that, then the do-not-apply list Rule 50 keeps, then the 30-day rule.
/// </summary>
public static class CompanyGate
{
    /// <summary>The first rule that holds the company, or allowed.</summary>
    public static CampaignVerdict Check(CampaignCompany company, IReadOnlyList<PastSend> sends, IReadOnlyList<DoNotApply> doNotApply, DateOnly today)
    {
        var former = company.Names.Select(n => FormerEmployers.Of(n, null)).FirstOrDefault(e => e is not null)
            ?? FormerEmployers.Of(null, company.Key)
            ?? (company.ParentKey is { Length: > 0 } parent ? FormerEmployers.Of(null, parent) : null);
        if (former is not null)
        {
            return CampaignVerdict.Hold($"former employer: {former.Name}");
        }
        var listed = doNotApply.FirstOrDefault(d => today < d.Until && d.Names.Any(n => company.Names.Any(c => ApplicationCap.Same(n, c))));
        if (listed is not null)
        {
            return CampaignVerdict.Hold($"do-not-apply list until {listed.Until:yyyy-MM-dd}");
        }
        return ThirtyDayRule.Check(company, sends, today);
    }
}
