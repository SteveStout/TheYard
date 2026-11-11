using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// Track A from the tracker (ADR-118, the addendum "Track A sends first; the finder behind the port"): in the 8 o'clock
/// hour, before the send, every application in Job_Hunt_Tracker.csv (read with the tracker's own CSV reader) from the
/// last seven days, at a company with no Track A lead in that window, becomes one Track A lead when the jobs lane named
/// a person for it on a public page: AppliedAt and AppliedCount from the tracker row, the person, the role, the tagged
/// code and links from nightly/track-a-contacts-&lt;date&gt;.tsv, and an address only when a public page showed it. With
/// no address the row waits for the finder behind the port (HunterFinder with HUNTER_KEY, else none) and the send holds
/// it as "Track A: no named address"; a shared mailbox is never written. One lead a company a window, whatever the
/// number of passes. It never sends.
/// </summary>
public sealed partial class TrackAFeed(IMergeStore store, ITrackABook book, IMxCheck mx, TimeProvider time)
{
    /// <summary>The window an application is followed in.</summary>
    public const int WindowDays = 7;

    /// <summary>The hold for a Track A lead with no named person's address.</summary>
    public const string NoNamedAddress = "Track A: no named address";

    /// <summary>The feed's log, one line a pass.</summary>
    public const string Log = "campaign/track-a.log";

    /// <summary>The run every Track A lead is written under.</summary>
    public const string Run = "track-a";

    /// <summary>
    /// The run a Track A lead is written under: "track-a", or "track-a/" and the lane for a row the leaders lane wrote
    /// ("leaders", or "leaders-" and its day), which the lead keeps as CreatedBy so the send picks Template A v3 for it.
    /// Any other cell, or none, is the jobs lane's row.
    /// </summary>
    public static string RunFor(string? lane) => lane is not null && LeadersLane().IsMatch(lane) ? Run + "/" + lane : Run;

    /// <summary>Whether a lead's CreatedBy names a row the leaders lane wrote.</summary>
    public static bool IsLeadersRun(string? createdBy) =>
        createdBy is not null && createdBy.StartsWith(Run + "/", StringComparison.Ordinal) && LeadersLane().IsMatch(createdBy[(Run.Length + 1)..]);

    /// <summary>Whether an address is a shared mailbox and not a person's.</summary>
    public static bool IsGeneralMailbox(string? address) => TrackAContacts.IsGeneralMailbox(address);

    /// <summary>Runs one pass and answers its line: counts only.</summary>
    public async Task<string> RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(CentralClock.Local(now));
        var master = TrackerCsvText.Parse(TrackerCsvText.Decode(await store.ReadAsync(MergePaths.Master, ct)));
        var applications = master.Where(row => row.Length >= 7 && TrackerRows.IsApplication(row) && Day(row[4]) is { } day && today.DayNumber - day.DayNumber is >= 0 and < WindowDays)
            .Select(row => (Company: row[2].Trim(), Count: row[0].Trim(), Day: Day(row[4])!.Value)).OrderBy(a => a.Day).ToList();
        var contacts = new List<TrackAContact>();
        int unread = 0;
        for (int back = WindowDays - 1; back >= 0; back--)
        {
            string path = TrackAContacts.File(today.AddDays(-back));
            if (await store.ExistsAsync(path, ct))
            {
                var (rows, missed) = TrackAContacts.Parse(TrackerCsvText.Decode(await store.ReadAsync(path, ct)));
                contacts.AddRange(rows);
                unread += missed;
            }
        }
        var known = (await book.TrackAKeysAsync(now.AddDays(-WindowDays), ct)).ToHashSet(StringComparer.Ordinal);
        int written = 0, noPerson = 0, noAddress = 0, already = 0;
        foreach (var application in applications)
        {
            var contact = contacts.LastOrDefault(c => ApplicationCap.Same(c.Company, application.Company));
            if (contact is null)
            {
                noPerson++;
                continue;
            }
            if (!known.Add(contact.CompanyKey))
            {
                already++;
                continue;
            }
            int companyId = await book.CompanyIdAsync(new SpoolCompany(contact.CompanyKey, null, [contact.Company], "Unknown", null, null, null, null, []), Run, now, ct);
            bool named = contact.ContactEmail is { } address && !IsGeneralMailbox(address) && CompanyKey.Of(address) == contact.CompanyKey
                && contact.EmailSource?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;
            noAddress += named ? 0 : 1;
            bool? hasMx = named ? await mx.HasMxAsync(contact.CompanyKey, ct) : null;
            var applied = new DateTimeOffset(application.Day.ToDateTime(new TimeOnly(6, 0)), CentralClock.Local(now) - now.UtcDateTime);
            var lead = new SpoolLead
            {
                CompanyKey = contact.CompanyKey, Track = "A", Company = contact.Company, RoleTitle = contact.RoleTitle, PostingUrl = contact.PostingUrl,
                WhatTheyDo = "", FirstName = contact.FirstName, ContactName = contact.ContactName, ContactTitle = contact.ContactTitle,
                ContactEmail = named ? contact.ContactEmail : null, EmailSource = named ? contact.EmailSource : null,
                AppliedAt = applied, AppliedCount = int.TryParse(application.Count, NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : null,
                ProofKey = contact.ProofKey, RefCode = contact.RefCode, Links = contact.Links,
            };
            var inputs = new StrengthInputs(RemoteStance.Unknown, 0, true, null, null, today, null);
            await book.SaveLeadAsync(companyId, lead, hasMx, Strength.Score(inputs, today), Strength.Hash(inputs), RunFor(contact.Lane), now, ct);
            written++;
        }
        string line = $"track A: {applications.Count} applications in {WindowDays} days, {contacts.Count} named contacts ({unread} unread), {written} rows written ({noAddress} waiting on a named address), {already} already had one, {noPerson} with no named person";
        await store.EnsureFolderAsync("campaign", ct);
        await store.AppendAsync(Log, TrackerCsvText.Encode($"{CentralClock.Local(now):yyyy-MM-dd HH:mm:ss} {line}\r\n"), ct);
        return line;
    }

    [GeneratedRegex(@"^leaders(-[a-z0-9]+)*$")]
    private static partial Regex LeadersLane();

    private static DateOnly? Day(string text) =>
        DateOnly.TryParseExact(text.Trim(), ["M/d/yyyy", "yyyy-MM-dd", "MM/dd/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
