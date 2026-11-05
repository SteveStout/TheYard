namespace JobHunt.Domain.Shared.Pay;

/// <summary>
/// The pay lines every job-hunt rule reads, one constant each (systemRules.md RULE 72, the pay check before any
/// effort): a band whose top, or the top wage filing for the title, reaches <see cref="Pass"/> passes; one that
/// reaches <see cref="Flag"/> and not Pass is flagged, worth a range question first; one under Flag is skipped with
/// its number. RULE 72 outranks the older $155,000 floor and $170,000 line of targeting-criteria.md, which is why
/// the night's picker and the campaign's Strength both read these and nothing else.
/// </summary>
public static class PayFloor
{
    /// <summary>A band topping here or above passes.</summary>
    public const int Pass = 180_000;

    /// <summary>A band topping here, under Pass, is flagged; under it, skipped.</summary>
    public const int Flag = 170_000;

    /// <summary>PASS, FLAG or SKIP for a band's top in whole dollars a year, or NONE when no band was published.</summary>
    public static string Judge(int? top) => top switch
    {
        null => "NONE",
        >= Pass => "PASS",
        >= Flag => "FLAG",
        _ => "SKIP",
    };
}
