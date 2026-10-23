// The Communication Services metrics the mail card keeps, by Azure's names as the resource's own
// metric definitions list them: the send requests, and the delivery outcomes split by status.
namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The mail card's metrics.</summary>
public static class MailMetrics
{
    /// <summary>
    /// Azure's names: the resource's API requests, of which the sends are the SendMail operation,
    /// and its delivery status updates, split by the message's status.
    /// </summary>
    public const string Requests = "ApiRequests", Outcomes = "DeliveryStatusUpdate", Status = "MessageStatus";

    public static readonly IReadOnlyList<AzureMetric> All =
    [
        new(Requests, "count", "sends", Filter: "Operation eq 'SendMail'"),
        new(Outcomes, "count", "delivered", Filter: $"{Status} eq 'Delivered'"),
        new(Outcomes, "count", "bounced", Filter: $"{Status} eq 'Bounced'"),
        new(Outcomes, "count", "suppressed", Filter: $"{Status} eq 'Suppressed'"),
        new(Outcomes, "count", "quarantined", Filter: $"{Status} eq 'Quarantined'"),
        new(Outcomes, "count", "failed", Filter: $"{Status} eq 'Failed'"),
    ];
}
