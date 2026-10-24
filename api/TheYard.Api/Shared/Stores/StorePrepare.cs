// The five asks a store gets at startup before it is written off. Its own file because it
// is one small retry policy, read and tested on its own, that both stores share.

namespace TheYard.Api.Shared.Stores;

// #region five-tries
/// <summary>
/// A store is asked five times at startup before it is written off, so a
/// refusal that lasts a few seconds does not cost the store for the life of
/// the process (ADR: The relational store). The waits between asks double
/// from two seconds, about thirty seconds in all, which fits inside
/// the deploy's five minutes with the container's own start-up still to pay
/// for. The state that comes back says how many asks it took, so the log line
/// and the Admin tab's startup card carry the count; the error is logged only
/// after the fifth refusal, by the caller.
/// </summary>
public static class StorePrepare
{
    /// <summary>How many times a store is asked before it is written off.</summary>
    public const int Tries = 5;

    /// <summary>The wait after the first refusal; each wait after it is twice the one before.</summary>
    public static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The setting that shortens the first wait: a whole number of milliseconds, read by the
    /// host at startup. Absent, the first wait is two seconds. A test that proves what the site
    /// does when its store never answers sets it to zero, because the five asks are the proof
    /// and the thirty seconds of waiting between them were not: with the waits in, that one test
    /// held every test run on both stores to no less than half a minute.
    /// </summary>
    public const string FirstWaitSetting = "Store:FirstWaitMs";

    /// <summary>The first wait the configuration asks for, or the default when it names none.</summary>
    public static TimeSpan FirstWaitFrom(IConfiguration configuration)
    {
        string? configured = configuration[FirstWaitSetting];
        return int.TryParse(configured, out int ms) && ms >= 0 ? TimeSpan.FromMilliseconds(ms) : FirstWait;
    }

    /// <summary>
    /// Asks the store up to five times with real waits between the asks, the
    /// first one as long as <paramref name="firstWait"/>.
    /// </summary>
    public static Task<DatabaseState> WithTriesAsync(Func<Task<DatabaseState>> prepare, TimeSpan firstWait) =>
        WithTriesAsync(prepare, Task.Delay, firstWait);

    /// <summary>
    /// Asks the store up to five times, waiting through <paramref name="wait"/>
    /// between asks so a test can run it without a real clock. Returns the first
    /// ready state, with the ask count in its note when it took more than one,
    /// or the last refusal with the count of refusals in its note. The waits
    /// double from <paramref name="firstWait"/>, two seconds unless the caller
    /// read a shorter one from the configuration.
    /// </summary>
    public static async Task<DatabaseState> WithTriesAsync(
        Func<Task<DatabaseState>> prepare,
        Func<TimeSpan, Task> wait,
        TimeSpan? firstWait = null)
    {
        DatabaseState state = new(false, "never asked");
        var between = firstWait ?? FirstWait;
        for (int attempt = 1; attempt <= Tries; attempt++)
        {
            try
            {
                state = await prepare();
            }
            catch (Exception ex)
            {
                // Prepare answers a state rather than throwing; this is the belt to that brace.
                state = new DatabaseState(false, ex.GetType().Name, ex);
            }

            if (state.Ready)
            {
                return attempt == 1 ? state : Renamed(state, $"{state.Note}, on ask {attempt} of {Tries}");
            }

            if (attempt < Tries)
            {
                await wait(between);
                between *= 2;
            }
        }

        return Renamed(state, $"{state.Note}, refused {Tries} times");
    }

    /// <summary>The same state with the count in its note; the note is read-only by design, so this is the constructor again.</summary>
    private static DatabaseState Renamed(DatabaseState state, string note) => new(state.Ready, note, state.Failure)
    {
        SchemaMs = state.SchemaMs,
        SeedMs = state.SeedMs,
        SeedRequestUnits = state.SeedRequestUnits,
    };
}
// #endregion five-tries
