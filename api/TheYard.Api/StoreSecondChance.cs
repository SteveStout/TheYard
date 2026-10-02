// The store tried again after startup, for a backend whose store refused all five asks.
// Its own file because it is a background service with its own loop and its own test, and
// it runs long after the rest of the store wiring has finished.
using TheYard.Infrastructure;

namespace TheYard.Api;

// #region second-chance
/// <summary>
/// A store that did not come up at startup is tried again (ADR: The relational
/// store, the addendum on the second chance). Without this, a store that was
/// briefly unavailable while the container started would be lost for the life
/// of the process: the catalogue served from files all day, and sign-in and
/// bids on that site going nowhere. A backend whose store refused is asked
/// again every <see cref="Every"/> for <see cref="Window"/>, and when the
/// store answers the backend is attached to it, warm. A store that is genuinely gone is
/// still the fallback the relational store record describes, and this stops
/// asking after the window so a missing database is not a query a minute for
/// ever.
/// </summary>
public sealed class StoreSecondChance(
    IReadOnlyList<StoreSecondChance.Plan> plans,
    ILogger<StoreSecondChance> logger) : BackgroundService
{
    /// <summary>One backend that did not come up: how to try the store again, and what to do when it answers.</summary>
    /// <param name="Backend">The backend whose store did not come up.</param>
    /// <param name="Prepare">Asks the store again and returns its state.</param>
    /// <param name="Attach">Attaches the backend to the store once it has answered.</param>
    public sealed record Plan(Backend Backend, Func<Task<DatabaseState>> Prepare, Func<DatabaseState, Task> Attach);

    /// <summary>How long to wait between asks.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(30);

    /// <summary>How long to keep asking before the store is left on the files until the next roll.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>Starts one loop per backend that did not come up, and returns without waiting for them.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var plan in plans.Where(plan => !plan.Backend.Ready))
        {
            _ = TryAsync(plan, Every, Window, Task.Delay, logger, stoppingToken);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// The loop, on its own so a test can run it with a fake clock: prepare,
    /// and on the first state that is ready, attach and stop; otherwise wait
    /// and ask again until the window closes. Returns how many times the store
    /// was asked. The prepare never throws (it answers a state), but the
    /// attach can, and an attach that throws is logged and the loop goes on,
    /// because a store that answered once will answer again.
    /// </summary>
    public static async Task<int> TryAsync(
        Plan plan,
        TimeSpan every,
        TimeSpan window,
        Func<TimeSpan, CancellationToken, Task> wait,
        ILogger logger,
        CancellationToken cancellation)
    {
        int attempts = 0;
        var started = DateTimeOffset.UtcNow;
        var deadline = started + window;
        while (!cancellation.IsCancellationRequested && !plan.Backend.Ready)
        {
            try
            {
                await wait(every, cancellation);
            }
            catch (OperationCanceledException)
            {
                return attempts;
            }

            attempts++;
            DatabaseState state;
            try
            {
                state = await plan.Prepare();
            }
            catch (Exception ex)
            {
                // Prepare answers a state rather than throwing; this is the belt to that brace.
                state = new DatabaseState(false, $"{plan.Backend.Name}: {ex.GetType().Name}", ex);
            }

            if (state.Ready)
            {
                try
                {
                    await plan.Attach(state);
                    logger.LogInformation(
                        "The {Store} store came up on the second chance, attempt {Attempt}: {Note}",
                        plan.Backend.Name, attempts, state.Note);
                    return attempts;
                }
                catch (Exception ex)
                {
                    // The type only; the message can carry a host name and this line reaches the public log section.
                    logger.LogError("Attaching the {Store} store failed with {Exception} on attempt {Attempt}; it will be asked again", plan.Backend.Name, ex.GetType().Name, attempts);
                }
            }
            else
            {
                logger.LogWarning(
                    "The {Store} store still refused on attempt {Attempt} of the second chance: {Note}",
                    plan.Backend.Name, attempts, state.Note);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                logger.LogError(
                    "The {Store} store did not come up in {Window}; the catalogue stays on the files until the next roll",
                    plan.Backend.Name, window);
                return attempts;
            }
        }
        return attempts;
    }
}
// #endregion second-chance
