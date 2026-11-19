using System.Globalization;
using System.Text.Json;
using JobHunt.Application.Features.Campaign;
using JobHunt.Application.Shared.Walls;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// Hunter's email finder behind the finder port (ADR-118), shipped dormant: it is built only when the app setting
/// HUNTER_KEY holds a key, and with none the pass never builds it and logs nothing about it. It asks only for a
/// lead whose public pages gave no address, keeps an answer only when Hunter scores it 70 or more and marks it valid,
/// and gives the first public page it cites as the address's source. A valid answer with no page is kept too, since
/// Hunter checked the mailbox itself, with Hunter's verifier as its source, as hunter_find.py writes it. The monthly
/// cap is the setting HUNTER_MONTHLY_CAP (1,500 on the Starter plan), the free plan's 50 without it; and the pass reads
/// the searches the plan has left before it asks (IFinderPlan; ADR-118, the addendum on the paid finder).
/// </summary>
public sealed class HunterFinder(IHttpClientFactory http, string key) : IAddressFinder, IFinderPlan
{
    /// <summary>The app setting that turns the finder on.</summary>
    public const string Setting = "HUNTER_KEY";

    /// <summary>The app setting that holds the month's cap of searches.</summary>
    public const string CapSetting = "HUNTER_MONTHLY_CAP";

    /// <summary>Searches a month with no setting: the free plan's credits.</summary>
    public const int FreePlanCap = 50;

    /// <summary>The source written for a valid address Hunter cited no page for.</summary>
    public const string VerifierPage = "https://hunter.io/email-verifier";

    /// <summary>The month's cap from its setting: a whole number of zero or more, else the free plan's 50.</summary>
    public static int MonthCapFrom(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int cap) ? cap : FreePlanCap;

    /// <summary>What Steve does when Hunter refuses, as the WALLS.md bullet says it.</summary>
    public const string Control = "sign in at hunter.io and read the account notice";

    /// <summary>What Steve does when the floor stops the asking (zeroresult1112).</summary>
    public const string FloorControl = "read one lookup by hand at hunter.io";

    /// <summary>The lowest score kept.</summary>
    public const int MinScore = 70;

    /// <summary>The finder when the setting holds a key, or null so nothing is built; "off" is off, as for every switch.</summary>
    public static HunterFinder? FromSetting(string? value, IHttpClientFactory http) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "off", StringComparison.OrdinalIgnoreCase) ? null : new HunterFinder(http, value.Trim());

    /// <inheritdoc />
    public string Name => "hunter";

    /// <summary>How long one call may wait.</summary>
    public TimeSpan Timeout { get; init; } = PublicBoardReader.RequestCap;

    /// <inheritdoc />
    public async Task<FoundAddress?> FindAsync(string fullName, string domain, CancellationToken ct)
    {
        string[] parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }
        string url = "https://api.hunter.io/v2/email-finder?domain=" + Uri.EscapeDataString(domain)
            + "&first_name=" + Uri.EscapeDataString(parts[0]) + "&last_name=" + Uri.EscapeDataString(parts[^1])
            + "&api_key=" + Uri.EscapeDataString(key);
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(Timeout);
        try
        {
            using var answer = await http.CreateClient().GetAsync(url, cap.Token);
            string body = await answer.Content.ReadAsStringAsync(cap.Token);
            if (answer.IsSuccessStatusCode)
            {
                return Read(body);
            }
            // A refusal is not "no address": every later call would meet the same answer until Steve clears it.
            return Refused((int)answer.StatusCode, body) is { } reason ? throw new FinderRefusedException(Name, reason, called: true) : null;
        }
        catch (Exception ex) when (PublicBoardReader.DidNotAnswer(ex, ct))
        {
            return null;
        }
    }

    /// <summary>
    /// The refusal in Hunter's answer, or null for any other failure: 401 and 403 (the key is not accepted) and
    /// 429 restricted_account (the account is held). A plain 429 too_many_requests is a rate limit, not a wall.
    /// </summary>
    public static string? Refused(int status, string body)
    {
        string? id = ErrorId(body);
        return status switch
        {
            401 or 403 => id is null ? $"HTTP {status}" : $"HTTP {status} {id}",
            429 when id == "restricted_account" => "HTTP 429 restricted_account",
            _ => null,
        };
    }

    private static string? ErrorId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Hunter's JSON to an address and the first public page it cites, or null.</summary>
    public static FoundAddress? Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        string email = data.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString()! : "";
        int score = data.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
        string status = data.TryGetProperty("verification", out var v) && v.ValueKind == JsonValueKind.Object
            && v.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString()! : "";
        string page = data.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array
            ? sources.EnumerateArray()
                .Select(x => x.TryGetProperty("uri", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString()! : "")
                .FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ?? ""
            : "";
        return email.Length > 0 && score >= MinScore && status == "valid" ? new FoundAddress(email, page.Length > 0 ? page : VerifierPage) : null;
    }

    /// <inheritdoc />
    public async Task<int?> SearchesLeftAsync(CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(Timeout);
        try
        {
            using var answer = await http.CreateClient().GetAsync("https://api.hunter.io/v2/account?api_key=" + Uri.EscapeDataString(key), cap.Token);
            return answer.IsSuccessStatusCode ? SearchesLeft(await answer.Content.ReadAsStringAsync(cap.Token)) : null;
        }
        catch (Exception ex) when (PublicBoardReader.DidNotAnswer(ex, ct))
        {
            return null;
        }
    }

    /// <summary>The searches left in Hunter's account answer (remaining, else available less used), or null.</summary>
    public static int? SearchesLeft(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("requests", out var requests) || requests.ValueKind != JsonValueKind.Object
                || !requests.TryGetProperty("searches", out var searches) || searches.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            int? Number(string name) => searches.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number ? (int)n.GetDouble() : null;
            return Number("remaining") ?? (Number("available") is int available && Number("used") is int used ? available - used : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
