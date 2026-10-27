using System.Globalization;
using System.Text;

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

    /// <summary>Asks the finder for each waiting lead under the cap; answers the part's line, counts only.</summary>
    public async Task<string> RunAsync(CancellationToken ct)
    {
        var local = CentralClock.Local(time.GetUtcNow());
        string path = CountPath(finder.Name, local);
        int used = await UsedAsync(path, ct);
        int asked = 0, found = 0, dropped = 0;
        foreach (var lead in await book.WaitingAsync(ct))
        {
            if (used + asked >= monthCap)
            {
                break;
            }
            asked++;
            var answer = await finder.FindAsync(lead.ContactName, lead.CompanyKey, ct);
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
        if (asked > 0)
        {
            await store.EnsureFolderAsync("campaign/finder", ct);
            await store.WriteAsync(path, Encoding.UTF8.GetBytes((used + asked).ToString(CultureInfo.InvariantCulture)), ct);
        }
        return $"{finder.Name}: {asked} asked, {found} found, {dropped} dropped, {used + asked} of {monthCap} this month";
    }

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

    private async Task<int> UsedAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct) && int.TryParse(Encoding.UTF8.GetString(await store.ReadAsync(path, ct)).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
}
