using System.Text.RegularExpressions;

namespace JobHunt.Domain.Shared.Postings;

/// <summary>
/// The one title test every apply path reads: the 2 o'clock pick (JobsGates), the pounce (PounceRules, through the
/// pick's verdict) and the 6 AM apply builder on the machine lanes (WorklistBuilder, linked from this file). A title
/// is an engineering seat only when it holds an engineering role word (Engineer, Engineering, Developer, Architect,
/// Software, Platform, SRE, DevOps, Tech Lead, CTO, a VP, Head or Director of Engineering) and no word of another
/// function (People, HR, Talent, Recruiter, Business Partner, Sales, Marketing, Finance, Legal, a product seat,
/// Business Development, Process Engineering). The function words are read first, so the "Engineering" in "Process
/// Engineering" or "Talent Partner, Engineering" never carries the title. "Technology", "AI" and "Product" alone are
/// never role words. Why: on 11/19 the pounce applied to Delinea's "Director, People Business Partner - Product,
/// Technology &amp; AI" (tracker Count 3803), and on 11/25 the pick passed business development and product seats to
/// the 6 AM apply lane.
/// </summary>
public static partial class RoleTitle
{
    /// <summary>The reason's opening words, the same on every path.</summary>
    public const string NotEngineering = "not an engineering role: ";

    /// <summary>The reason a title is not an engineering seat, naming the word that decided it, or null when it is one.</summary>
    /// <param name="title">The title as written.</param>
    public static string? Failure(string? title)
    {
        string t = title ?? string.Empty;
        if (Function().Match(t) is { Success: true } function)
        {
            return NotEngineering + function.Value;
        }
        return Role().IsMatch(t) ? null : NotEngineering + "no engineering role word";
    }

    /// <summary>True when the title is an engineering seat.</summary>
    /// <param name="title">The title as written.</param>
    public static bool IsEngineering(string? title) => Failure(title) is null;

    // "People" stays a role's subject when it names the reports ("Engineering Manager (People Leader)"); a product seat is
    // a manager, owner, designer or marketer of it, or its VP, head or director, never a "Product Engineer" or a
    // "Director, Product Engineering".
    [GeneratedRegex(@"\bpeople(?!\s+(manager|leader|leadership|management)\b)(\s+business\s+partner|\s+partner|\s+operations|\s+ops|\s+team)?\b|\bhuman\s+resources\b|\bHR\b(\s+business\s+partner)?|\btalent\b(\s+(partner|acquisition))?|\brecruit(er|ers|ing|ment)\b|\bbusiness\s+partner\b|\bbusiness\s+development\b|\bprocess\s+engineer(ing)?\b|\bsales\b|\baccount\s+executive\b|\bproduct\s+(marketing|manager|management|owner|designer|design|analyst)\b|\b(chief\s+product\s+officer|CPO)\b|\b(S?VP|EVP|vice\s+president|head|director|chief)\s*,?\s+(of\s+)?product\b(?!\s+(engineering|engineer|software|development))|\bmarketing\b|\bfinance\b|\blegal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Function();

    [GeneratedRegex(@"\b(engineer|engineers|engineering|developer|developers|architect|software|platform|SRE|site\s+reliability|devops|tech\s+lead|technical\s+lead|CTO|chief\s+technology\s+officer|application\s+development)\b|\.net\b|c#", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Role();
}
