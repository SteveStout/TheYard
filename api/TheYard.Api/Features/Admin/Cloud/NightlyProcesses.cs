// The nightly card's two added slots (ADR-102 and ADR-109, the addenda on the six processes): the Function's
// paragraph and its seven process sections, put onto the card the frame built. Three reads, each kept two
// minutes so a minute's polls on both names cost a workspace query at most once: the runs reader's thirty-day
// fold, the switches from the Function's health route (the app's settings, names and plain words only, which
// the site's Reader role cannot list itself), and the summaries from the code, read once.
using System.Globalization;
using System.Text.Json;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

// #region nightly-processes
/// <summary>Puts the description and the processes onto the nightly card.</summary>
/// <param name="runs">The runs reader.</param>
/// <param name="host">The Function's host name, read from its record when the recorder has not; null when it cannot be read.</param>
/// <param name="descriptions">The summaries from the code.</param>
/// <param name="version">The version this build is, which the description's source names.</param>
/// <param name="clock">The clock the strip and the next runs are counted on.</param>
public sealed class NightlyProcesses(IProcessRunsReader runs, Func<CancellationToken, Task<string?>> host, ProcessDescriptions descriptions, string version, TimeProvider clock)
{
    private static Outbound Http => OutboundHttp.Client(TimeSpan.FromSeconds(10));

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, ProcessFold Fold, IReadOnlyDictionary<string, string>? Switches)? _held;

    /// <summary>How long a fold and the switches are kept before they are read again.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(2);

    /// <summary>The card with its description and its seven sections; the card as it was if anything throws.</summary>
    /// <param name="card">The nightly card as the frame built it.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<CloudReport> EnrichAsync(CloudReport card, CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        ProcessFold fold;
        IReadOnlyDictionary<string, string>? switches;
        try
        {
            (fold, switches) = await ReadAsync(now, cancellation);
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            // Kept like a read, so a failing workspace or health route is asked again in two minutes, not on every poll.
            (fold, switches) = (ProcessFold.Unread($"not read ({ex.GetType().Name})"), null);
            _held = (now, fold, switches);
        }
        return card with
        {
            Description = new CloudDescription(
                descriptions.Function(),
                $"the Function's own summary comments, read from the repository at the version deployed ({version}) · the switches from the app's settings, read live through the Function's health route"),
            Processes = ProcessSections.Build(fold, switches, descriptions.For, runs.ProcessHref, now),
        };
    }

    private async Task<(ProcessFold Fold, IReadOnlyDictionary<string, string>? Switches)> ReadAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            if (_held is { } held && now - held.At < Keep)
            {
                return (held.Fold, held.Switches);
            }
            // From the start of the strip's first Central day, so the fold holds the thirty days the strip draws and no part of a thirty-first.
            var fold = await runs.FoldAsync(now - NightlySlots.StartOfCentralDay(now.AddDays(1 - ProcessSections.StripDays)), cancellation);
            var switches = await host(cancellation) is { Length: > 0 } name ? await SwitchesAsync(name, cancellation) : null;
            _held = (now, fold, switches);
            return (fold, switches);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The switches the health route names, the processes' own names only, each a plain word; null when it did not answer.</summary>
    /// <param name="name">The Function's host name.</param>
    /// <param name="cancellation">Stops the read.</param>
    public static async Task<IReadOnlyDictionary<string, string>?> SwitchesAsync(string name, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, MergerReading.Address(name));
            request.Headers.UserAgent.ParseAdd("TheYard-SelfRead/1 (nightly card, switches)");
            using var response = await Http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            return Switches(answer.RootElement);
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested && ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>The answer's "switches", kept only under the seven processes' switch names and only as a plain word of up to twelve letters.</summary>
    /// <param name="answer">The health route's JSON.</param>
    public static IReadOnlyDictionary<string, string>? Switches(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("switches", out var named) || named.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var known = FunctionProcesses.All.SelectMany(process => process.Switches).ToHashSet(StringComparer.Ordinal);
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in named.EnumerateObject())
        {
            if (known.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 and <= 12 } word && word.All(char.IsAsciiLetter))
            {
                kept[property.Name] = word.ToLower(CultureInfo.InvariantCulture);
            }
        }
        return kept;
    }

    /// <summary>The Function's host name off its record, for a site whose recorder has not read it; null when it cannot be read.</summary>
    /// <param name="azure">Resource Manager.</param>
    /// <param name="functionPath">The Function app's resource id.</param>
    /// <param name="cancellation">Stops the read.</param>
    public static async Task<string?> HostAsync(AzureMetrics azure, string functionPath, CancellationToken cancellation)
    {
        if (!azure.Configured)
        {
            return null;
        }
        try
        {
            using var record = (await azure.RecordAsync(functionPath, "2024-04-01", cancellation)).Record;
            return AzureMetrics.Text(record.RootElement, "properties.defaultHostName");
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested && ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }
}
// #endregion nightly-processes
