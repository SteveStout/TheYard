using System.Security.Cryptography;
using JobHunt.Domain.Features.Ats;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The sweep keeps why it left each message and reads every one again when the rules change (the nightly-jobs fix, R9):
/// J:\ats-sweep\judged.tsv on 27 November held 45 lines in four columns, one shared tracker hash and no reason, so every
/// pass logged "45 left before, not read again" and nobody could tell why. Its rows 3, 6 and 11 are copied here (the
/// greenhouse no-reply, Michaela Silhasek's Buildertrend thread, Jennifer George's Magna thread). The first pass under new
/// rules is dry and writes each reason; the next one acts, log first (RULE 38: no row, no archive). A share born and
/// thrown away with the test, an inbox in memory (RULE 79).
/// </summary>
public sealed class AtsRejudgeTests
{
    private static readonly DateTimeOffset Afternoon = new(2026, 10, 22, 19, 30, 0, TimeSpan.Zero);

    // The receipt's own text is not on J: (only its id and sender are), so the receipt is the repository's own test text.
    private const string ReceiptText = "Thank you for applying to Acme. We have received your application for Senior Engineer and our team is reviewing it.";

    private static readonly string[] Applied = ["10", "NA", "Acme", "Greenhouse", "10/21/2026", "", "SUBMITTED by Claude: Senior Engineer (Greenhouse 123), remote US"];

    private static readonly InboxMessage[] Mail =
    [
        new("1a19da219c47d1b4", "1a19da219c47d1b4", "no-reply@us.greenhouse-mail.io", "Thank you for applying to Acme", Afternoon, ReceiptText),
        new("1a0f46f7a9c570b9", "1a0f46f7a9c570b9", "michaela silhasek <buildertrend@myworkday.com>", "Buildertrend", Afternoon, "Next steps"),
        new("x2", "1a0f46f7a9c570b9", "michaela silhasek <michaela.silhasek@buildertrend.com>", "Re: Buildertrend", Afternoon, "Are you free Tuesday?"),
        new("1a0b5829e88831b3", "1a06d1f6166491aa", "magna legal services <mail@hire.lever.co>", "Magna", Afternoon, "Update"),
        new("x3", "1a06d1f6166491aa", "jennifer george <jgeorge@magnals.com>", "Re: Magna", Afternoon, "Thanks Steven"),
    ];

    private static string[] Ids => ["1a19da219c47d1b4", "1a0f46f7a9c570b9", "1a0b5829e88831b3"];

    [Fact]
    public async Task Old_lines_are_read_again_in_a_dry_pass_with_their_reasons_then_the_next_pass_acts()
    {
        using var scratch = new Scratch();
        scratch.Master(TrackerCsvText.Line(Applied) + "\r\n");
        string hash = Convert.ToHexStringLower(SHA256.HashData(scratch.Bytes(MergePaths.Master)));
        scratch.Write(AtsSweep.Judged,
            $"1a19da219c47d1b4\t1a19da219c47d1b4\tno-reply@us.greenhouse-mail.io\t{hash}\r\n"
            + $"1a0f46f7a9c570b9\t1a0f46f7a9c570b9\tmichaela silhasek <buildertrend@myworkday.com>,michaela silhasek <michaela.silhasek@buildertrend.com>\t{hash}\r\n"
            + $"1a0b5829e88831b3\t1a06d1f6166491aa\tjennifer george <jgeorge@magnals.com>,magna legal services <mail@hire.lever.co>\t{hash}\r\n");
        var inbox = new Inbox(Mail);

        string dry = await new AtsSweep(scratch.Store, inbox, new FixedTime(Afternoon)).RunAsync(true, CancellationToken.None);
        Assert.StartsWith($"SHADOW (rules {AtsRules.Version}: every message read again, a dry pass, reasons in ats-sweep/rejudge-2026-10-22.tsv) found 3:", dry, StringComparison.Ordinal);
        Assert.Equal(Ids, inbox.Read);
        Assert.Empty(inbox.Archived);
        string reasons = scratch.Text(AtsSweep.Rejudge(new DateOnly(2026, 10, 22)));
        Assert.Contains("rests on application Count 10", reasons, StringComparison.Ordinal);
        string judged = scratch.Text(AtsSweep.Judged);
        Assert.Contains($"1a0f46f7a9c570b9\t1a0f46f7a9c570b9\tmichaela silhasek <buildertrend@myworkday.com>,michaela silhasek <michaela.silhasek@buildertrend.com>\t{hash}\t{AtsRules.Version}\ta person is in the thread (michaela.silhasek@buildertrend.com)", judged, StringComparison.Ordinal);
        Assert.Contains($"\t{AtsRules.Version}\ta person is in the thread (jgeorge@magnals.com)", judged, StringComparison.Ordinal);

        var next = new Inbox(Mail);
        string live = await new AtsSweep(scratch.Store, next, new FixedTime(Afternoon)).RunAsync(true, CancellationToken.None);
        Assert.StartsWith("ON found 3: left 2, archive only 1", live, StringComparison.Ordinal);
        Assert.Equal(["1a19da219c47d1b4"], next.Archived);
        Assert.Equal(["1a19da219c47d1b4"], next.Read);
        Assert.Contains("(2 left before, not read again)", live, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_row_no_archive()
    {
        using var scratch = new Scratch();
        scratch.Master(TrackerCsvText.Line(Applied) + "\r\n");
        var old = new InboxMessage("m9", "t9", "no-reply@us.greenhouse-mail.io", "Thank you for applying to Acme", new DateTimeOffset(2025, 12, 1, 15, 0, 0, TimeSpan.Zero), ReceiptText);
        Assert.Equal((AtsAction.ArchiveOnly, null), (AtsRules.Decide(old, [old.From], []).Action, AtsRules.Decide(old, [old.From], []).RestsOn));
        var inbox = new Inbox(old);
        await new AtsSweep(scratch.Store, inbox, new FixedTime(Afternoon)).RunAsync(true, CancellationToken.None);
        Assert.Empty(inbox.Archived);
        Assert.Contains("left in the inbox: no row, no archive", scratch.Text(AtsSweep.LastPlan), StringComparison.Ordinal);
    }

    /// <summary>An inbox in memory: reads and archives recorded.</summary>
    private sealed class Inbox(params InboxMessage[] messages) : IInbox
    {
        public List<string> Archived { get; } = [];

        public List<string> Read { get; } = [];

        public Task<IReadOnlyList<string>> FindAsync(MailSearch search, int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. messages.Where(m => !m.Id.StartsWith('x')).Select(m => m.Id).Take(max)]);

        public Task<InboxMessage> ReadAsync(string id, CancellationToken ct)
        {
            Read.Add(id);
            return Task.FromResult(messages.Single(m => m.Id == id));
        }

        public Task<IReadOnlyList<string>> ThreadSendersAsync(string threadId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. messages.Where(m => m.ThreadId == threadId).Select(m => m.From)]);

        public Task<bool> ArchiveAsync(string id, CancellationToken ct)
        {
            Archived.Add(id);
            return Task.FromResult(true);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
