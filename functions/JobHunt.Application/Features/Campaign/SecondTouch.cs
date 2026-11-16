namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// What a lead that already had its first email may get. Never the same first email again: the open leads stay open
/// after a send, so once the 30-day hold lapses the pass would otherwise mail them the identical note. A second,
/// different email may follow after 30 days to a person's own address, only once, and only when a second-touch
/// template has been approved; until then every emailed lead is held here with the reason.
/// </summary>
public static class SecondTouch
{
    /// <summary>The hold for a lead whose emails reached the most it may have.</summary>
    public const string NoThird = "followed up already: never a third email";

    /// <summary>The hold for a follow-up whose address is a shared box.</summary>
    public const string NoSharedBox = "follow-up: never to a shared mailbox";

    /// <summary>The hold for a follow-up while no second-touch template is approved.</summary>
    public const string NoTemplate = "follow-up: waits on an approved second email";

    /// <summary>Why the lead may not have the next email, or null when it has had none.</summary>
    /// <param name="lead">The lead with its count of real emails.</param>
    public static string? Hold(LeadRecord lead) => lead.Touches switch
    {
        <= 0 => null,
        >= 2 => NoThird,
        _ when TrackAFeed.IsGeneralMailbox(lead.ContactEmail) => NoSharedBox,
        _ => NoTemplate,
    };
}
