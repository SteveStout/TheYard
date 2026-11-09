namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// The desk beside a live apply run. The model has no shell, so it cannot run check_aiisms.py itself; instead it writes
/// each essay answer to essays/&lt;row&gt;-&lt;n&gt;.md in its run folder before typing it, and this desk, running in the
/// program while the model runs, writes &lt;row&gt;-&lt;n&gt;.verdict beside it once the file's last line reads END: clean only when every sentence is banked
/// text (<see cref="BankedText"/>) and check_aiisms.py passes the whole answer. The lane types an answer only on a clean
/// verdict. Every other run goes straight through.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="prose">check_aiisms.py.</param>
/// <param name="inner">The CLI the model runs in.</param>
public sealed class EssayDesk(IShare share, IProseCheck prose, IClaudeCli inner) : IClaudeCli
{
    /// <summary>The run folder's essays folder.</summary>
    public const string Folder = "essays";

    /// <summary>The verdict a lane may type on.</summary>
    public const string Clean = "clean";

    /// <summary>The text the owner already approved, the only text an answer may come from.</summary>
    public static readonly IReadOnlyList<string> Banks = ["standard-answers.md", "EVIDENCE-BANK.md", "writing-samples.md"];

    private static readonly TimeSpan Every = TimeSpan.FromSeconds(2);

    /// <summary>Runs the model, answering its essays until it ends, then answers any it left.</summary>
    /// <param name="request">The run.</param>
    /// <param name="ct">Stops the run.</param>
    public async Task<ClaudeRun> RunAsync(ClaudeRequest request, CancellationToken ct)
    {
        if (request.Essays is not { } folder)
        {
            return await inner.RunAsync(request, ct);
        }
        IReadOnlyList<string> banks = await BanksAsync(ct);
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task watch = WatchAsync($"{folder}/{Folder}", banks, watching.Token);
        try
        {
            return await inner.RunAsync(request, ct);
        }
        finally
        {
            await watching.CancelAsync();
            await watch.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            await AnswerAsync($"{folder}/{Folder}", banks, true, CancellationToken.None);
        }
    }

    /// <summary>The verdict on one essay file's text: <see cref="Clean"/>, or refused with the reason.</summary>
    /// <param name="text">The essay file's text.</param>
    /// <param name="banks">The banks, plain.</param>
    /// <param name="ct">Stops the check.</param>
    public async Task<string> JudgeAsync(string text, IReadOnlyList<string> banks, CancellationToken ct)
    {
        Essay essay = BankedText.Parse(text);
        if (essay.Answer.Length == 0)
        {
            return "refused: no answer under an ANSWER: line; skip the row, reason essay";
        }
        var unbanked = BankedText.Unbanked(essay.Answer, banks);
        if (unbanked.Count > 0)
        {
            return $"refused: {unbanked.Count} sentences are not word for word in {string.Join(", ", Banks)}, first: \"{unbanked[0]}\"";
        }
        return await prose.CleanAsync(essay.Answer, ct) ? Clean : "refused: check_aiisms.py found something in the answer";
    }

    private async Task<IReadOnlyList<string>> BanksAsync(CancellationToken ct)
    {
        var banks = new List<string>();
        foreach (string bank in Banks)
        {
            try
            {
                banks.Add(BankedText.Plain(await share.ReadAsync(bank, ct)));
            }
            catch (IOException)
            {
                // An unreadable bank holds nothing an answer can come from, so the answers it would have passed are refused.
            }
        }
        return banks;
    }

    private async Task WatchAsync(string essays, IReadOnlyList<string> banks, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await AnswerAsync(essays, banks, false, ct);
            await Task.Delay(Every, ct);
        }
    }

    private async Task AnswerAsync(string essays, IReadOnlyList<string> banks, bool last, CancellationToken ct)
    {
        foreach (ShareFile file in await share.ListAsync(essays, "*.md", ct))
        {
            string verdict = Path.ChangeExtension(file.Path, ".verdict");
            if (await share.ExistsAsync(verdict, ct))
            {
                continue;
            }
            string text = await share.ReadAsync(file.Path, ct);
            if (BankedText.Finished(text) || last)
            {
                await share.WriteAsync(verdict, await JudgeAsync(text, banks, ct) + "\n", ct);
            }
        }
    }
}
