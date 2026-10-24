using System.Globalization;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// The word for when Steve applied, rendered at send time in Central (the review, F8): Template A's "so I
/// applied today" was false the morning after. The same day is "today", the day before "yesterday", two to six
/// days back "on Friday", and seven days back "last Saturday" (the weekday's name alone would read as today's).
/// Track A sends no later than seven days after the application: past that, or an application dated after the
/// send, there is no word and the lead is held. Rendering a date as a word is formatting, not content.
/// </summary>
public static class AppliedWhen
{
    /// <summary>The oldest application, in days, a Track A email may follow.</summary>
    public const int LastDay = 7;

    /// <summary>The word, or null when Track A may not send.</summary>
    public static string? Render(DateTimeOffset appliedAtUtc, DateTimeOffset sendAtUtc)
    {
        var applied = CentralClock.Local(appliedAtUtc);
        var send = CentralClock.Local(sendAtUtc);
        int days = DateOnly.FromDateTime(send).DayNumber - DateOnly.FromDateTime(applied).DayNumber;
        string weekday = applied.DayOfWeek.ToString();
        return days switch
        {
            0 => "today",
            1 => "yesterday",
            >= 2 and < LastDay => "on " + weekday,
            LastDay => "last " + weekday,
            _ => null,
        };
    }

    /// <summary>Allowed with the word, or held naming the days.</summary>
    public static CampaignVerdict Check(DateTimeOffset? appliedAtUtc, DateTimeOffset sendAtUtc)
    {
        if (appliedAtUtc is not { } applied)
        {
            return CampaignVerdict.Hold("Track A with no AppliedAt");
        }
        return Render(applied, sendAtUtc) is null
            ? CampaignVerdict.Hold($"applied {CentralClock.Local(applied).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, outside {LastDay} days")
            : CampaignVerdict.Pass;
    }
}
