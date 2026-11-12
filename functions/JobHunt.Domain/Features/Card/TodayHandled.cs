using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>
/// The section A items that need nothing from him, which the card folds into one line, "Handled, no action: N
/// (names)": a blacklist hit, a sales InMail or other note that names no role, a carried item the lane recommends no
/// reply to with no new message since the last card, and any carried item with no new message once it has been on
/// two cards. The reasons are the reply lane's own words in the queue (NIGHTLY-LANE.md writes a blacklist hit's held
/// reason to start "BLACKLIST HIT" and says "carried, no new message" of a carried thread); the card adds only the
/// count of cards, from TodayHistory.
/// </summary>
public static partial class TodayHandled
{
    /// <summary>The cards a carried item with no new message is shown on before it is handled.</summary>
    public const int CarriedCards = 2;

    /// <summary>Whether the item needs nothing from him, so it goes to the handled line.</summary>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="item">A section A item.</param>
    public static bool Is(TodayInputs inputs, TodayItem item)
    {
        string text = string.Join(" ", item.Notes.Prepend(item.Held));
        if (item.Held.StartsWith("BLACKLIST HIT", StringComparison.Ordinal) || text.Contains("BLACKLIST HIT", StringComparison.Ordinal))
        {
            return true;
        }
        if (item.Held.StartsWith("not a role", StringComparison.OrdinalIgnoreCase) || item.Held.Contains("sales InMail", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!item.Held.Contains("carried", StringComparison.OrdinalIgnoreCase) || !NoNew().IsMatch(item.Held))
        {
            return false;
        }
        int cards = DateOnly.FromDateTime(inputs.At).DayNumber - TodayHistory.First(inputs, item).DayNumber + 1;
        return item.Held.Contains("no reply", StringComparison.OrdinalIgnoreCase) || cards > CarriedCards;
    }

    /// <summary>The one line, or nothing when no item is handled.</summary>
    /// <param name="body">The card so far.</param>
    /// <param name="handled">The handled items, in the card's order.</param>
    public static void Line(StringBuilder body, IReadOnlyList<TodayItem> handled)
    {
        if (handled.Count > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"- Handled, no action: {handled.Count} ({string.Join(", ", handled.Select(h => h.Person.Length > 0 ? h.Person : h.Who))}).\n");
        }
    }

    [GeneratedRegex(@"no new (message|InMail|notice)", RegexOptions.IgnoreCase)]
    private static partial Regex NoNew();
}
