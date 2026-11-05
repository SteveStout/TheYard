using System.Globalization;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// A domain's MX records over DNS over HTTPS (Google's public resolver, dns.google/resolve, keyless JSON): .NET
/// has no MX lookup of its own, and a package for one is a dependency for one question. Status 3 (no such name)
/// or an answer with no MX record is no mail; a resolver that does not answer is null, and the lead waits.
/// Not answering covers a network error, a wait past <see cref="Timeout"/> and a body that is not the JSON
/// expected, so one odd answer never fails the pass that asked.
/// </summary>
public sealed class DohMxCheck(IHttpClientFactory http) : IMxCheck
{
    /// <summary>The resolver's address.</summary>
    public const string Resolver = "https://dns.google/resolve";

    /// <summary>How long one question may wait; the tests set it shorter.</summary>
    public TimeSpan Timeout { get; init; } = PublicBoardReader.RequestCap;

    /// <inheritdoc />
    public async Task<bool?> HasMxAsync(string domain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(Timeout);
        try
        {
            using var answer = await http.CreateClient().GetAsync($"{Resolver}?name={Uri.EscapeDataString(domain)}&type=MX", cap.Token);
            return answer.IsSuccessStatusCode ? Read(await answer.Content.ReadAsStringAsync(cap.Token)) : null;
        }
        catch (Exception ex) when (PublicBoardReader.DidNotAnswer(ex, ct))
        {
            return null;
        }
    }

    /// <summary>The resolver's JSON to the answer: an MX record (type 15) is mail.</summary>
    public static bool? Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        int status = doc.RootElement.TryGetProperty("Status", out var s) ? s.GetInt32() : -1;
        if (status == 3)
        {
            return false;
        }
        if (status != 0)
        {
            return null;
        }
        return doc.RootElement.TryGetProperty("Answer", out var answers) && answers.EnumerateArray().Any(a => a.TryGetProperty("type", out var t) && t.GetInt32() == 15);
    }
}
