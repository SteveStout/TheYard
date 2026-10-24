using TheYard.Infrastructure;

namespace TheYard.Api.Shared.MachineReadings;

// #region hourly-readings
/// <summary>
/// Two readings that change by the day, not the minute, taken once an hour and
/// shown on the Azure card (ADR: The Admin tab, as a product, the addendum on
/// certificate and storage): when the certificate the site answers with runs
/// out, read from one TLS handshake with its own public address, and how full
/// the relational store is. Neither is asked on a request. A container with no
/// public address (a developer's machine, the test host) reads no certificate,
/// and one with no Azure SQL Database reads no storage; the card says so.
/// </summary>
public sealed class HourlyReadings(Uri? site, Func<CancellationToken, Task<StorageReading?>> storage, TimeProvider clock) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private object? _view;

    /// <summary>The last readings as the card shows them, or null before the first pass.</summary>
    public object? View => Volatile.Read(ref _view);

    /// <summary>The certificate's last day and the days left from <paramref name="now"/>, whole days, rounded down.</summary>
    public static int DaysLeft(DateTimeOffset notAfter, DateTimeOffset now) =>
        (int)Math.Floor((notAfter - now).TotalDays);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PassAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass: both readings, each on its own, so one that fails leaves the other.</summary>
    public async Task PassAsync(CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        DateTimeOffset? notAfter = site is null ? null : await OutboundHttp.CertificateNotAfterAsync(site, cancellation);
        var stored = await storage(cancellation);
        Volatile.Write(ref _view, new
        {
            read_at = now,
            certificate = notAfter is { } until
                ? new { host = site!.Host, not_after = until, days_left = DaysLeft(until, now) }
                : null,
            sql_storage = stored is { MaxMb: > 0 } s
                ? new { used_mb = Math.Round(s.UsedMb, 1), max_mb = Math.Round(s.MaxMb, 1), share = Math.Round(s.UsedMb / s.MaxMb, 4) }
                : null,
        });
    }
}
// #endregion hourly-readings
