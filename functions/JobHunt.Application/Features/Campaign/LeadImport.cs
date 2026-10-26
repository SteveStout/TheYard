namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// The lead run's files into the database (ADR-118, Decision 5). The lead run on GitHub Actions has no identity
/// the database knows, so it writes a file to campaign/leads-inbox/ on the share and the Function, the one writer
/// of the campaign's rows, reads it here: each company and lead held to LeadSpool's rules, each new address asked
/// for its MX records, Strength and its inputs hash computed by code, and the file moved to merged/ or rejected/
/// with a line saying what was written and what was refused. One file at a time, oldest first. The part's line
/// counts every refusal (a file, a company or a lead) and each company saved once.
/// </summary>
public sealed class LeadImport(IMergeStore store, ICompanyBook companies, ILeadStore leads, IMxCheck mx, TimeProvider time)
{
    /// <summary>The import's log on the share, one line a file.</summary>
    public const string Log = "campaign/leads-inbox/import.log";

    /// <summary>Imports every waiting file and answers the part's line: counts only.</summary>
    public async Task<string> RunAsync(CancellationToken ct)
    {
        var files = (await store.ListFilesAsync(LeadSpool.Inbox, ct))
            .Where(f => f.Name.EndsWith(".json", StringComparison.Ordinal)).OrderBy(f => f.LastWritten).ToList();
        if (files.Count == 0)
        {
            return "0 lead files";
        }
        // A company named in more than one file is saved once a file but counted once a pass.
        var companiesSaved = new HashSet<string>(StringComparer.Ordinal);
        int leadsSaved = 0, blocked = 0, refused = 0;
        foreach (var entry in files)
        {
            string path = $"{LeadSpool.Inbox}/{entry.Name}";
            var now = time.GetUtcNow();
            var (file, error) = LeadSpool.Parse(TrackerCsvText.Decode(await store.ReadAsync(path, ct)));
            if (file is null)
            {
                await MoveAsync(path, entry.Name, "rejected", $"{entry.Name}: {error}", ct);
                refused++;
                continue;
            }
            var notes = new List<string>();
            var keys = new Dictionary<string, (int Id, IReadOnlyList<string> Names, SpoolCompany? Spool)>(StringComparer.Ordinal);
            foreach (var company in file.Companies)
            {
                if (LeadSpool.Refuse(company) is { } why)
                {
                    notes.Add($"company {company.Key}: {why}");
                    refused++;
                    continue;
                }
                keys[company.Key] = (await companies.SaveAsync(company, file.Run, now, ct), company.Names, company);
                companiesSaved.Add(company.Key);
            }
            foreach (string key in file.Leads.Select(l => l.CompanyKey).Distinct().Where(k => !keys.ContainsKey(k)))
            {
                if (await companies.FindAsync(key, ct) is { } known)
                {
                    keys[key] = (known.Id, known.Names, null);
                }
            }
            var companyKeys = keys.Keys.ToHashSet(StringComparer.Ordinal);
            foreach (var lead in file.Leads)
            {
                if (LeadSpool.Refuse(lead, companyKeys) is { } why)
                {
                    notes.Add($"lead at {lead.CompanyKey}: {why}");
                    refused++;
                    continue;
                }
                var (id, names, spool) = keys[lead.CompanyKey];
                bool? hasMx = lead.ContactEmail is { } email ? await mx.HasMxAsync(CompanyKey.Of(email), ct) : null;
                var stored = spool is not null ? null : await companies.FindAsync(lead.CompanyKey, ct);
                var inputs = new StrengthInputs(
                    Enum.TryParse<RemoteStance>(spool?.RemoteStance ?? stored?.RemoteStance, out var stance) ? stance : RemoteStance.Unknown,
                    spool?.Evidence.Count ?? stored?.DotNetEvidence ?? 0,
                    lead.Track == "A",
                    lead.BandTop,
                    lead.GhostScore,
                    DateOnly.FromDateTime(CentralClock.Local(now)),
                    spool?.LastDotNetPostingAt ?? stored?.LastDotNetPostingAt);
                string? block = LeadSpool.Blocked(lead, names);
                blocked += block is null ? 0 : 1;
                await leads.SaveAsync(id, lead, hasMx, Strength.Score(inputs, inputs.FirstSeen, lead.StrengthStep), Strength.Hash(inputs), block, file.Run, now, ct);
                leadsSaved++;
            }
            string line = $"{entry.Name}: run {file.Run}, {file.Companies.Count} companies, {file.Leads.Count} leads" + (notes.Count == 0 ? "" : " | " + string.Join("; ", notes));
            await MoveAsync(path, entry.Name, "merged", line, ct);
        }
        return $"{files.Count} lead files, {companiesSaved.Count} companies saved, {leadsSaved} leads written, {blocked} blocked, {refused} refused";
    }

    private async Task MoveAsync(string path, string name, string folder, string line, CancellationToken ct)
    {
        await store.EnsureFolderAsync($"{LeadSpool.Inbox}/{folder}", ct);
        string stamp = CentralClock.Local(time.GetUtcNow()).ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        await store.MoveAsync(path, $"{LeadSpool.Inbox}/{folder}/{stamp}-{name}", ct);
        await store.AppendAsync(Log, TrackerCsvText.Encode($"{CentralClock.Local(time.GetUtcNow()):yyyy-MM-dd HH:mm:ss} {folder}: {line}\r\n"), ct);
    }
}

/// <summary>
/// The daily board scan (ADR-118, Decision 5; the review, F6): once a day, first in the 8 o'clock hour, one request
/// per company with a public board, keyless JSON, and the newest posting whose title names .NET written back to the
/// company with the time it looked. Track B ranks by that date. About 2 % of the Function's free grant a month at
/// two hundred companies; hourly would be 72 %, so it is daily.
/// </summary>
public sealed class BoardScan(ICompanyBook companies, IBoardReader boards, TimeProvider time)
{
    /// <summary>Scans every board once and answers the part's line: counts only.</summary>
    public async Task<string> RunAsync(CancellationToken ct)
    {
        var all = await companies.BoardsAsync(ct);
        int answered = 0, dotNet = 0, postings = 0;
        foreach (var company in all)
        {
            var read = await boards.PostingsAsync(company.Kind, company.Token, ct);
            if (read is null)
            {
                continue;
            }
            answered++;
            postings += read.Count;
            var newest = read.Where(p => IsDotNet(p.Title)).OrderByDescending(p => p.Posted ?? DateOnly.MinValue).FirstOrDefault();
            dotNet += read.Count(p => IsDotNet(p.Title));
            await companies.MarkScannedAsync(company.Id, newest?.Posted ?? (newest is null ? null : DateOnly.FromDateTime(CentralClock.Local(time.GetUtcNow()))), newest?.Title, time.GetUtcNow(), ct);
        }
        return $"{all.Count} boards, {answered} answered, {postings} postings, {dotNet} .NET";
    }

    /// <summary>Whether a posting's title names .NET or C#: the scan's count and the recency date read titles only.</summary>
    public static bool IsDotNet(string title) =>
        title.Contains(".NET", StringComparison.OrdinalIgnoreCase) || title.Contains("C#", StringComparison.Ordinal) || title.Contains("dotnet", StringComparison.OrdinalIgnoreCase);
}
