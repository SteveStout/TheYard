using System.Security.Cryptography;
using System.Text;

namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// The desk beside a live apply run. The model has no shell, so it cannot run check_aiisms.py itself; instead it writes
/// each essay answer to essays/&lt;row&gt;-&lt;n&gt;.md in its run folder before typing it, and this desk, running in the
/// program while the model runs, writes &lt;row&gt;-&lt;n&gt;.verdict beside it once the file's last line reads END: clean
/// only when every sentence is a whole banked sentence in his voice (<see cref="BankedText"/>) and check_aiisms.py passes
/// the whole answer. A clean verdict carries the hash of the answer it passed, so an answer changed after its verdict, or
/// a verdict the desk never wrote, does not read as passed. The lane types an answer only on a clean verdict. A failure
/// here never reaches the run: an essay with no verdict is one the lane skips. Every other run goes straight through.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="prose">check_aiisms.py.</param>
/// <param name="inner">The CLI the model runs in.</param>
public sealed class EssayDesk(IShare share, IProseCheck prose, IClaudeCli inner) : IClaudeCli
{
    /// <summary>The run folder's essays folder.</summary>
    public const string Folder = "essays";

    /// <summary>The first word of the verdict a lane may type on.</summary>
    public const string Clean = "clean";

    /// <summary>The text the owner already approved, the only text an answer may come from.</summary>
    public static readonly IReadOnlyList<string> Banks = ["standard-answers.md", "EVIDENCE-BANK.md", "writing-samples.md"];

    private static readonly TimeSpan Every = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ProofLimit = TimeSpan.FromMinutes(1);

    /// <summary>The verdict that passes this answer, and only this answer.</summary>
    /// <param name="answer">The answer as the essay file holds it.</param>
    public static string Passed(string answer) =>
        $"{Clean} {Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(BankedText.Plain(answer))))}";

    /// <summary>Runs the model, answering its essays until it ends, then answers any it left.</summary>
    /// <param name="request">The run.</param>
    /// <param name="ct">Stops the run.</param>
    public async Task<ClaudeRun> RunAsync(ClaudeRequest request, CancellationToken ct)
    {
        if (request.Essays is not { } folder)
        {
            return await inner.RunAsync(request, ct);
        }
        IReadOnlySet<string> banked = await BankedAsync(ct);
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task watch = WatchAsync($"{folder}/{Folder}", banked, watching.Token);
        try
        {
            return await inner.RunAsync(request, ct);
        }
        finally
        {
            await watching.CancelAsync();
            await watch.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            await PassAsync($"{folder}/{Folder}", banked, true, CancellationToken.None);
        }
    }

    /// <summary>The verdict on one essay file's text: <see cref="Passed"/>, or refused with the reason.</summary>
    /// <param name="text">The essay file's text.</param>
    /// <param name="banked">The banks' whole sentences.</param>
    /// <param name="ct">Stops the check.</param>
    public async Task<string> JudgeAsync(string text, IReadOnlySet<string> banked, CancellationToken ct)
    {
        Essay essay = BankedText.Parse(text);
        if (BankedText.Sentences(essay.Answer).Count == 0)
        {
            return "refused: no answer under an ANSWER: line; skip the row, reason essay";
        }
        var unbanked = BankedText.Unbanked(essay.Answer, banked);
        if (unbanked.Count > 0)
        {
            return $"refused: {unbanked.Count} sentences are not whole sentences of {string.Join(", ", Banks)} in his own voice, first: \"{unbanked[0]}\"";
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(ProofLimit);
        return await prose.CleanAsync(essay.Answer, limit.Token) ? Passed(essay.Answer) : "refused: check_aiisms.py found something in the answer";
    }

    private async Task<IReadOnlySet<string>> BankedAsync(CancellationToken ct)
    {
        var banked = new HashSet<string>(StringComparer.Ordinal);
        foreach (string bank in Banks)
        {
            try
            {
                banked.UnionWith(BankedText.BankSentences(await share.ReadAsync(bank, ct)));
            }
            catch (IOException)
            {
                // An unreadable bank holds nothing an answer can come from, so the answers it would have passed are refused.
            }
        }
        return banked;
    }

    private async Task WatchAsync(string essays, IReadOnlySet<string> banked, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await PassAsync(essays, banked, false, ct);
            await Task.Delay(Every, ct);
        }
    }

    private async Task PassAsync(string essays, IReadOnlySet<string> banked, bool last, CancellationToken ct)
    {
        try
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
                    await share.WriteAsync(verdict, await JudgeAsync(text, banked, ct) + "\n", ct);
                }
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException && (last || !ct.IsCancellationRequested))
        {
            // A pass that fails leaves the essay with no verdict, which the lane reads as a skip; the next pass tries again.
        }
    }
}
