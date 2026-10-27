using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>One .NET posting that makes a company a .NET shop: the required stack, or another engineering posting.</summary>
public sealed record SpoolEvidence(string Kind, string Title, string Url, DateTimeOffset SeenAt);

/// <summary>A company as the lead run writes it.</summary>
public sealed record SpoolCompany(
    string Key,
    string? ParentKey,
    IReadOnlyList<string> Names,
    string RemoteStance,
    string? BoardKind,
    string? BoardToken,
    DateOnly? LastDotNetPostingAt,
    string? LastDotNetPostingTitle,
    IReadOnlyList<SpoolEvidence> Evidence);

/// <summary>The five tagged links make_ref.py printed for the lead's code.</summary>
public sealed record SpoolLinks(string Resume, string Site, string GitHub, string LinkedIn, string About);

/// <summary>A lead as the lead run writes it: every template variable, where the address came from, and the model's one step.</summary>
public sealed record SpoolLead
{
    public required string CompanyKey { get; init; }
    public required string Track { get; init; }
    public required string Company { get; init; }
    public string? RoleTitle { get; init; }
    public string? PostingUrl { get; init; }
    public required string WhatTheyDo { get; init; }
    public required string FirstName { get; init; }
    public required string ContactName { get; init; }
    public required string ContactTitle { get; init; }
    public string? ContactEmail { get; init; }
    public string? EmailSource { get; init; }
    public DateTimeOffset? AppliedAt { get; init; }
    public int? AppliedCount { get; init; }
    public DateTimeOffset? LinkedInInviteSentAt { get; init; }
    public DateTimeOffset? LinkedInAcceptedAt { get; init; }
    public string? ProofKey { get; init; }
    public string? FollowUpPostSlug { get; init; }
    public int? GhostScore { get; init; }
    public int? BandTop { get; init; }
    public int StrengthStep { get; init; }
    public string? StrengthWhy { get; init; }
    public string? RefCode { get; init; }
    public SpoolLinks? Links { get; init; }
}

/// <summary>One lead run's file in campaign/leads-inbox/: the run's name, its companies and its leads.</summary>
public sealed record LeadSpoolFile(string Run, IReadOnlyList<SpoolCompany> Companies, IReadOnlyList<SpoolLead> Leads);

/// <summary>
/// The lead run's rows, read and held to the rules before any reaches the database (ADR-118, Decision 5). The
/// model writes a file; code decides what it may say. A lead that breaks a shape rule (no company, a track that
/// is not A or B, Track A with no application, no contact, a blank first name on a lead that is not a declared
/// company mailbox, a company mailbox with no address, an address with no https source, a link without the lead's code,
/// a proof key not on the list) is refused with the reason and goes nowhere. A lead whose WhatTheyDo clause breaks
/// WhatTheyDoRule is kept with Blocked set, so the next run sees why. Personal addresses are refused here too:
/// nothing the campaign may not send to is written.
/// </summary>
public static partial class LeadSpool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The folder the lead run writes to; merged/ and rejected/ sit inside it.</summary>
    public const string Inbox = "campaign/leads-inbox";

    /// <summary>The contact title the lead run writes for a company's own published address, the one lead that may have no first name.</summary>
    public const string CompanyMailbox = "Company mailbox";

    /// <summary>Whether the contact title declares a company mailbox; a missing name never makes one (ADR-118, 1.0.4.106).</summary>
    public static bool IsCompanyMailbox(string? contactTitle) =>
        string.Equals(contactTitle?.Trim(), CompanyMailbox, StringComparison.OrdinalIgnoreCase);

    /// <summary>The file, or null with the reason it could not be read.</summary>
    public static (LeadSpoolFile? File, string? Error) Parse(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<LeadSpoolFile>(json, Json);
            return file is null || string.IsNullOrWhiteSpace(file.Run) ? (null, "no run name") : (file with { Companies = file.Companies ?? [], Leads = file.Leads ?? [] }, null);
        }
        catch (JsonException ex)
        {
            return (null, "not the lead file's shape: " + ex.Message.Split('.')[0]);
        }
    }

    /// <summary>Null when the company may be written, or the reason it may not.</summary>
    public static string? Refuse(SpoolCompany company)
    {
        if (CompanyKey.Of(company.Key) != company.Key)
        {
            return $"key {company.Key} is not a registrable domain";
        }
        if (company.Names is not { Count: > 0 } || company.Names.Any(string.IsNullOrWhiteSpace))
        {
            return "no name";
        }
        if (!Enum.TryParse<RemoteStance>(company.RemoteStance, out _))
        {
            return $"remote stance {company.RemoteStance} is not Unknown, Remote, Hybrid or Onsite";
        }
        if (company.BoardKind is { } kind && kind is not ("greenhouse" or "lever" or "ashby"))
        {
            return $"board {kind} is not greenhouse, lever or ashby";
        }
        if (company.BoardKind is not null != company.BoardToken is not null)
        {
            return "a board kind and its token come together";
        }
        return company.Evidence.FirstOrDefault(e => e.Kind is not ("required" or "other") || !Https(e.Url)) is { } bad ? $"evidence {bad.Url} has no https address or kind" : null;
    }

    /// <summary>Null when the lead may be written, or the reason it is refused.</summary>
    public static string? Refuse(SpoolLead lead, IReadOnlySet<string> companyKeys)
    {
        if (!companyKeys.Contains(lead.CompanyKey))
        {
            return $"company {lead.CompanyKey} is not in the file or the book";
        }
        if (lead.Track is not ("A" or "B"))
        {
            return $"track {lead.Track} is not A or B";
        }
        if (lead.Track == "A" && (lead.AppliedAt is null || string.IsNullOrWhiteSpace(lead.RoleTitle)))
        {
            return "Track A without the application's time and the role";
        }
        if (string.IsNullOrWhiteSpace(lead.ContactName) || string.IsNullOrWhiteSpace(lead.ContactTitle))
        {
            return "no contact named";
        }
        if (string.IsNullOrWhiteSpace(lead.FirstName) && !IsCompanyMailbox(lead.ContactTitle))
        {
            return "no manager named";
        }
        if (string.IsNullOrWhiteSpace(lead.FirstName) && lead.ContactEmail is null)
        {
            return "a company mailbox with no address";
        }
        if (lead.ContactEmail is { } email)
        {
            if (!Https(lead.EmailSource))
            {
                return "an address with no https page it was read from";
            }
            if (RecipientDomainRule.Personal.Contains(CompanyKey.Of(email)))
            {
                return "a personal address";
            }
        }
        if (lead.ProofKey is { } key && !ProofKey.Keys.Contains(key))
        {
            return $"proof key {key} is not one of {string.Join(", ", ProofKey.Keys)}";
        }
        if (lead.GhostScore is < 0 or > 5 || lead.StrengthStep is < -1 or > 1)
        {
            return "a ghost score outside 0 to 5 or a step outside -1 to 1";
        }
        if (lead.FollowUpPostSlug is { } slug && !Slug().IsMatch(slug))
        {
            return $"follow-up post {slug} is not a slug";
        }
        if (lead.RefCode is not { } code || !Code().IsMatch(code) || lead.Links is not { } links)
        {
            return "no tagged code and links from make_ref.py";
        }
        string[] all = [links.Resume, links.Site, links.GitHub, links.LinkedIn, links.About];
        return all.All(link => Https(link) && link.EndsWith("r=" + code, StringComparison.Ordinal)) ? null : "a link that does not carry the lead's code";
    }

    /// <summary>The Blocked reason a kept lead carries: its WhatTheyDo clause's broken rule, or null.</summary>
    public static string? Blocked(SpoolLead lead, IReadOnlyList<string> companyNames) =>
        WhatTheyDoRule.Check(lead.WhatTheyDo, [.. companyNames, lead.Company]);

    private static bool Https(string? url) => url is not null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.Any(char.IsWhiteSpace);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Slug();

    [GeneratedRegex("^[a-z0-9]{6}$")]
    private static partial Regex Code();
}
