using System.Net.Http.Json;
using System.Text.Json;

namespace JobHunt.Functions.Sheet;

/// <summary>
/// Sheet1 of the tracker's Google Sheet through the Sheets REST API, with the same OAuth client and
/// refresh token the nightly lane holds (ADR-JOBHUNT-001 in the job-hunt folder), set on the Function
/// out of band and never kept in the repository. Two calls only: spreadsheets.values.get on A:G, and
/// spreadsheets.values.append with RAW input and INSERT_ROWS. No call here can change a cell that is
/// already written.
/// </summary>
public sealed class GoogleSheetGrid(IHttpClientFactory http, GoogleSheetSettings settings) : ISheetGrid
{
    private const string Range = "Sheet1!A:G";

    public async Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(CancellationToken ct)
    {
        using var client = await ClientAsync(ct);
        using var answer = await client.GetAsync($"https://sheets.googleapis.com/v4/spreadsheets/{settings.SheetId}/values/{Uri.EscapeDataString(Range)}?majorDimension=ROWS", ct);
        answer.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(ct));
        var rows = new List<IReadOnlyList<string>>();
        if (json.RootElement.TryGetProperty("values", out var values))
        {
            foreach (var row in values.EnumerateArray())
            {
                rows.Add([.. row.EnumerateArray().Select(cell => cell.ValueKind == JsonValueKind.String ? cell.GetString() ?? string.Empty : cell.ToString())]);
            }
        }
        return rows;
    }

    public async Task AppendAsync(IReadOnlyList<IReadOnlyList<string>> rows, CancellationToken ct)
    {
        using var client = await ClientAsync(ct);
        using var answer = await client.PostAsync(
            $"https://sheets.googleapis.com/v4/spreadsheets/{settings.SheetId}/values/{Uri.EscapeDataString(Range)}:append?valueInputOption=RAW&insertDataOption=INSERT_ROWS",
            JsonContent.Create(new { range = Range, majorDimension = "ROWS", values = rows }), ct);
        answer.EnsureSuccessStatusCode();
    }

    private Task<HttpClient> ClientAsync(CancellationToken ct) => settings.Google.ClientAsync(http, ct);
}

/// <summary>The Google credentials and the tracker Sheet's id, from settings set out of band.</summary>
/// <param name="Google">The OAuth client and refresh token.</param>
/// <param name="SheetId">TRACKER_SHEET_ID.</param>
public sealed record GoogleSheetSettings(GoogleCredentials Google, string SheetId)
{
    /// <summary>The settings as the Function holds them, or null while any is unset.</summary>
    public static GoogleSheetSettings? FromEnvironment()
    {
        string? sheet = Environment.GetEnvironmentVariable("TRACKER_SHEET_ID");
        return GoogleCredentials.FromEnvironment() is { } google && !string.IsNullOrEmpty(sheet) ? new GoogleSheetSettings(google, sheet) : null;
    }
}
