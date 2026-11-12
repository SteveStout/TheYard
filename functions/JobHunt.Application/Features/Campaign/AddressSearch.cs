using System.Globalization;
using System.Text;
using JobHunt.Application.Shared.Walls;
using JobHunt.Domain.Shared.Tracker;
using JobHunt.Domain.Shared.Walls;

namespace JobHunt.Application.Features.Campaign;

/// <summary>A lead with a named manager and no address yet, as the finder pass reads it.</summary>
public sealed record WaitingLead(long Id, string CompanyKey, string ContactName);

/// <summary>The leads still waiting on an address, and a finder's answer written back to the row.</summary>
public interface IAddressBook
{
    /// <summary>Open leads that name a manager and carry no address, strongest first.</summary>
    Task<IReadOnlyList<WaitingLead>> WaitingAsync(CancellationToken ct);

    /// <summary>Writes the address, the page it was read from and the MX answer onto the lead.</summary>
    Task SetAddressAsync(long leadId, string address, string sourceUrl, bool? mx, DateTimeOffset at, CancellationToken ct);

    /// <summary>How many of these leads now carry an address: the finder's found count over the month, read from the table.</summary>
    Task<int> WithAddressAsync(IReadOnlyCollection<long> leadIds, CancellationToken ct);
}

/// <summary>
/// The finder pass (ADR-118): after the lead run's files are imported, each lead that names a manager and has no
/// address is asked of the configured finder, up to a monthly cap counted in a file on the share. An answer is
/// kept only when it holds to the same rules a public-page address does: on the company's own registrable
/// domain, not a personal mailbox, a https page it was read from, and MX records found. Anything else is dropped
/// and the lead keeps waiting. The pass is built only when a finder is configured, so with none it never runs.
/// </summary>
public sealed class AddressSearch(IAddressBook book, IAddressFinder finder, IMxCheck mx, IMergeStore store, TimeProvider time, int monthCap)
{
    /// <summary>The month's call count for a finder, one number in a file.</summary>
    public static string CountPath(string finderName, DateTimeOffset local) =>
        $"campaign/finder/{finderName}-{local.ToString("yyyy-MM", CultureInfo.InvariantCulture)}.txt";

    /// <summary>The leads a finder was asked about this month, one id a line, so a lead it had no answer for is not asked again.</summary>
    public static string AskedPath(string finderName, DateTimeOffset local) =>
        $"campaign/finder/{finderName}-{local.ToString("yyyy-MM", CultureInfo.InvariantCulture)}-asked.txt";

    /// <summary>The floor (zeroresult1112): this many asked in the month with none found stops the asking.</summary>
    public const int FloorAsked = 10;

    /// <summary>How long the asking stops once the floor is met, and after a refusal (WalledFinder.Stop).</summary>
    public static readonly TimeSpan FloorStop = TimeSpan.FromHours(24);

    /// <summary>
    /// Asks the finder for each waiting lead under the cap that it has not been asked about this month; answers the
    /// part's line, counts only. The count and the asked leads are written even when a call throws, so a failed pass
    /// never lets the next one spend past the cap. A call the finder refused is the exception (ADR-118 addendum,
    /// 11/12): Hunter does not charge it (50 of 50 searches left after 21 restricted calls on 11/12), so it is not
    /// counted and its lead is not locked out of the month.
    /// <para>
    /// The floor: once <see cref="FloorAsked"/> leads have been asked this month and none of them carries an address
    /// in the table (the month's asked list read against the leads table), the pass stops asking, puts one bullet on WALLS.md and asks nothing for 24 hours. After that one
    /// lead is asked a pass, and the first address found takes the bullet away. The tally adds no share writes: asked
    /// is the month's asked list the pass already keeps, found is read from the leads table.
    /// </para>
    /// </summary>
    public async Task<string> RunAsync(CancellationToken ct)
    {
        var utc = time.GetUtcNow();
        var local = CentralClock.Local(utc);
        string path = CountPath(finder.Name, local);
        string askedPath = AskedPath(finder.Name, local);
        int used = await UsedAsync(path, ct);
        var before = await AskedAsync(askedPath, ct);
        string walls = await WallsAsync(ct);
        DateTimeOffset? floorSince = WallList.Last(walls, FloorKey);
        if (floorSince is { } since && utc - since < FloorStop)
        {
            return $"{finder.Name}: 0 asked, 0 found, 0 dropped, {used} of {monthCap} this month, stopped: {FloorAsked} asked and none found, until {WalledFinder.Central(since + FloorStop)}";
        }
        int foundBefore = before.Count > 0 ? await book.WithAddressAsync(before, ct) : 0;
        int most = floorSince is null ? int.MaxValue : 1;
        var now = new List<long>();
        int found = 0, dropped = 0, refusedCalls = 0;
        string? stopped = null;
        try
        {
            foreach (var lead in await book.WaitingAsync(ct))
            {
                if (used + now.Count >= monthCap || now.Count >= most)
                {
                    break;
                }
                if (floorSince is null && before.Count + now.Count >= FloorAsked && foundBefore + found == 0)
                {
                    stopped = "floor";
                    break;
                }
                if (before.Contains(lead.Id))
                {
                    continue;
                }
                now.Add(lead.Id);
                FoundAddress? answer;
                try
                {
                    answer = await finder.FindAsync(lead.ContactName, lead.CompanyKey, ct);
                }
                catch (FinderRefusedException wall)
                {
                    // Not charged, so not counted, and the lead is asked again once the wall is gone.
                    now.Remove(lead.Id);
                    refusedCalls += wall.Called ? 1 : 0;
                    stopped = wall.Reason;
                    break;
                }
                if (answer is null)
                {
                    continue;
                }
                string? why = Refuse(answer, lead.CompanyKey);
                bool? hasMx = why is null ? await mx.HasMxAsync(lead.CompanyKey, ct) : null;
                if (why is not null || hasMx != true)
                {
                    dropped++;
                    continue;
                }
                await book.SetAddressAsync(lead.Id, answer.Address.Trim(), answer.SourceUrl, hasMx, time.GetUtcNow(), ct);
                found++;
            }
        }
        finally
        {
            if (now.Count > 0)
            {
                await store.EnsureFolderAsync("campaign/finder", CancellationToken.None);
                await store.WriteAsync(path, Encoding.UTF8.GetBytes((used + now.Count).ToString(CultureInfo.InvariantCulture)), CancellationToken.None);
                await store.WriteAsync(askedPath, Encoding.UTF8.GetBytes(string.Join('\n', before.Concat(now).Select(id => id.ToString(CultureInfo.InvariantCulture))) + "\n"), CancellationToken.None);
            }
        }
        if (found > 0 && floorSince is not null)
        {
            await WriteWallsAsync(WallList.Without(await WallsAsync(ct), FloorKey), ct);
        }
        else if (stopped == "floor" || (floorSince is not null && now.Count > 0 && found == 0))
        {
            await WriteWallsAsync(WallList.With(await WallsAsync(ct), FloorKey, FloorBullet(utc), utc), ct);
            stopped = "floor";
        }
        string line = $"{finder.Name}: {now.Count} asked, {found} found, {dropped} dropped, {used + now.Count} of {monthCap} this month";
        return stopped switch
        {
            null => line,
            "floor" => $"{line}, stopped: {FloorAsked} asked and none found, until {WalledFinder.Central(utc + FloorStop)}",
            _ => refusedCalls > 0 ? $"{line}, {refusedCalls} refused ({stopped})" : $"{line}, stopped: {stopped}",
        };
    }

    /// <summary>The one thing Steve does when the floor is met, as its WALLS.md bullet says it.</summary>
    public string FloorControl { get; init; } = "read one lookup by hand on the finder's own site";

    private string FloorKey => $"{finder.Name}-empty";

    private string FloorBullet(DateTimeOffset now) =>
        $"{finder.Name} found no address for the first {FloorAsked} leads asked this month: the asking stopped. "
        + $"Control: {FloorControl}. Since {WalledFinder.Central(now)}; "
        + "it stops for 24 hours at a time, asks one lead a pass after that, and the first address found removes this bullet.";

    private async Task<string> WallsAsync(CancellationToken ct) =>
        await store.ExistsAsync(WallList.Path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(WallList.Path, ct)) : "- none\n";

    private Task WriteWallsAsync(string text, CancellationToken ct) => store.WriteAsync(WallList.Path, TrackerCsvText.Encode(text), ct);

    /// <summary>Null when a finder's address may be written for the company, or the reason it may not.</summary>
    public static string? Refuse(FoundAddress answer, string companyKey)
    {
        string to = answer.Address.Trim();
        int at = to.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != to.LastIndexOf('@') || to.Any(char.IsWhiteSpace))
        {
            return "not an address";
        }
        string key = CompanyKey.Of(to);
        if (RecipientDomainRule.Personal.Contains(key))
        {
            return "a personal address";
        }
        if (!string.Equals(key, companyKey, StringComparison.Ordinal))
        {
            return "not on the company's domain";
        }
        return answer.SourceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !answer.SourceUrl.Any(char.IsWhiteSpace)
            ? null
            : "no https page it was read from";
    }

    private async Task<HashSet<long>> AskedAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct)
            ? [.. Encoding.UTF8.GetString(await store.ReadAsync(path, ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => long.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out long id) ? id : -1).Where(id => id >= 0)]
            : [];

    private async Task<int> UsedAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct) && int.TryParse(Encoding.UTF8.GetString(await store.ReadAsync(path, ct)).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
}
