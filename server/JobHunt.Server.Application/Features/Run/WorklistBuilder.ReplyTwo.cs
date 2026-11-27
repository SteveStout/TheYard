namespace JobHunt.Server.Application.Features.Run;

/// <summary>The messages worklist's reading of a format 2 queue (ReplyQueueTwo).</summary>
public sealed partial class WorklistBuilder
{
    /// <summary>
    /// Sections B and C of a format 2 queue: an item with a draft becomes a candidate when its draft greets the person it
    /// names, an item the reply lane held is dropped with the lane's own reason, and a line the grammar does not read is
    /// named. Sections A and E are Steve's (warm replies and rejection reads) and never become a send.
    /// </summary>
    private async Task<Worklist> MessagesTwoAsync(string newest, string queue, DayReads reads, List<Candidate> candidates, int room, bool dry, CancellationToken ct)
    {
        var dropped = new List<Dropped>();
        var retired = await share.ExistsAsync(WritePolicy.RetiredTable, ct) ? LeftOut.Retired(await share.ReadAsync(WritePolicy.RetiredTable, ct)) : [];
        var retiring = new List<string>();
        int earlier = 0;
        dropped.AddRange(ReplyQueueTwo.Unread(queue).Select(line => new Dropped($"{newest} section {line}", "holds text this program could not read as items; read it by hand")));
        foreach (QueueLine line in ReplyQueueTwo.Lines(queue).Where(line => line.Section is "B" or "C"))
        {
            string name = line.Person.Length > 0 ? line.Person : line.Company;
            if (line.Held.Length > 0 && LeftOut.Permanent(line.Held))
            {
                if (retired.Contains(LeftOut.Key(name)))
                {
                    earlier++;
                }
                else if (!retiring.Any(r => r.StartsWith(LeftOut.Key(name) + "\t", StringComparison.Ordinal)))
                {
                    dropped.Add(new Dropped(name, $"retired, settled for good: held by the reply lane: {line.Held}"));
                    retiring.Add(LeftOut.Line(name, line.Held, reads.Day));
                }
                continue;
            }
            if (line.Held.Length > 0)
            {
                dropped.Add(new Dropped(name, $"held by the reply lane: {line.Held}"));
                continue;
            }
            if (!await share.ExistsAsync(line.Draft, ct))
            {
                dropped.Add(new Dropped(name, $"the draft file is missing: {line.Draft}"));
                continue;
            }
            string body = ReplyQueue.Body(await share.ReadAsync(line.Draft, ct));
            string first = ReplyQueue.Greeted(body);
            if (line.Person.Length > 0 && !line.Person.StartsWith(first + " ", StringComparison.OrdinalIgnoreCase))
            {
                dropped.Add(new Dropped(name, $"the draft greets {first}"));
                continue;
            }
            string company = line.Company.Length > 0 ? line.Company : reads.Gate.CompanyOf(line.Person);
            if (ReplyQueue.WhoIs(line.Person, first, company, reads.Rows) is not { } person)
            {
                dropped.Add(new Dropped(name, $"the draft greets {first}, and no one tracker person at {company} by that name gives a full name to check Rule 25 by"));
                continue;
            }
            candidates.Add(new Candidate(person, company, WorkAction.Message, body, Profile(reads, person, company), $"linkedin-queue.md {line.Section}, {line.Reference}, {line.Draft}"));
        }
        if (earlier > 0)
        {
            dropped.Add(new Dropped($"{earlier} people", $"retired on an earlier morning, each reason settled for good and kept in {WritePolicy.RetiredTable} beside the tracker row it cites"));
        }
        if (retiring.Count > 0 && !dry)
        {
            await share.AppendAsync(WritePolicy.RetiredTable, string.Concat(retiring), ct);
        }
        Worklist list = await FinishAsync(candidates, reads, candidate => Held(reads, candidate) ?? reads.Gate.Message(candidate.Person, candidate.Company), room, dry, ct);
        return list with { Dropped = [.. dropped, .. list.Dropped] };
    }
}
