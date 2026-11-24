using System.Globalization;

namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// The invite worklist. A queue block whose person the tracker already shows invited is closed in its own queue file
/// the morning it is skipped for that reason (one "- Item N ... DROPPED" line, which QueueBlocks reads as closed), so the
/// next morning offers someone new instead of the same names: a live run only, and only in a queue file the write policy
/// lets the program write (the feed's QUEUE-hotmgr and QUEUE-pounce files). Every gate is the same as before.
/// </summary>
public sealed partial class WorklistBuilder
{
    /// <summary>The invite worklist.</summary>
    /// <param name="reads">The morning's reads.</param>
    /// <param name="room">Invites left today.</param>
    /// <param name="dry">True for a dry run.</param>
    /// <param name="ct">Stops the build.</param>
    public async Task<Worklist> InvitesAsync(DayReads reads, int room, bool dry, CancellationToken ct)
    {
        var blocks = HotRank.Order(reads.Queues.Where(block => block.Action == WorkAction.Invite), reads.Rows, reads.Day);
        var heading = blocks.ToDictionary(block => block.Source, block => block.Person, StringComparer.Ordinal);
        var candidates = blocks
            .Select(block => new Candidate(block.Text.Contains(Placeholder, StringComparison.OrdinalIgnoreCase) ? string.Empty : block.Person, block.Company, WorkAction.Invite, block.Text, block.Url, block.Source, Budget: block.Budget));
        var invited = new List<string>();
        Worklist list = await FinishAsync(candidates, reads, candidate =>
        {
            string? reason = Held(reads, candidate) ?? reads.Gate.Invite(candidate.Person, candidate.Company) ?? HeadingGate(reads, candidate, heading);
            if (reason is "already invited" || reason?.EndsWith(": already invited", StringComparison.Ordinal) == true)
            {
                invited.Add(candidate.Source);
            }
            return reason;
        }, room, dry, ct);
        if (!dry)
        {
            await CloseInvitedAsync(blocks, invited, reads.Day, ct);
        }
        return list;
    }

    /// <summary>Appends one closing line a block to each queue file the program may write.</summary>
    private async Task CloseInvitedAsync(IReadOnlyList<QueueBlock> blocks, IReadOnlyList<string> sources, DateOnly day, CancellationToken ct)
    {
        foreach (var file in blocks.Where(block => sources.Contains(block.Source, StringComparer.Ordinal)).GroupBy(block => block.Source[..block.Source.LastIndexOf(':')], StringComparer.Ordinal))
        {
            string path = $"linkedin-proof/{file.Key}";
            if (!WritePolicy.Allowed(path) || !await share.ExistsAsync(path, ct))
            {
                continue;
            }
            string lines = string.Concat(file.Select(block => string.Create(CultureInfo.InvariantCulture,
                $"- Item {block.Number} ({(block.Person.Length > 0 ? block.Person + ", " : string.Empty)}{block.Company}): DROPPED, already invited (the tracker shows the invite), closed by the 6 AM program {day:yyyy-MM-dd} so it is not offered again\n")));
            await share.AppendAsync(path, "\n" + lines, ct);
        }
    }

    private static string? HeadingGate(DayReads reads, Candidate candidate, IReadOnlyDictionary<string, string> heading) =>
        candidate.Person.Length == 0 && heading.TryGetValue(candidate.Source, out string? named) && named.Length > 0 && reads.Gate.Invite(named, candidate.Company) is { } why
            ? $"the heading names {named}: {why}"
            : null;
}
