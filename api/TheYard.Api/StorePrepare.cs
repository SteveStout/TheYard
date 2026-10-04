// The five asks a store gets at startup before it is written off. Its own file because it
// is one small retry policy, read and tested on its own, that both stores share.
using TheYard.Application;

namespace TheYard.Api;

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

    /// <summary>Asks the store up to five times with real waits between the asks.</summary>
    public static Task<DatabaseState> WithTriesAsync(Func<Task<DatabaseState>> prepare) =>
        WithTriesAsync(prepare, Task.Delay);

    /// <summary>
    /// Asks the store up to five times, waiting through <paramref name="wait"/>
    /// between asks so a test can run it without a real clock. Returns the first
    /// ready state, with the ask count in its note when it took more than one,
    /// or the last refusal with the count of refusals in its note.
    /// </summary>
    public static async Task<DatabaseState> WithTriesAsync(
        Func<Task<DatabaseState>> prepare,
        Func<TimeSpan, Task> wait)
    {
        DatabaseState state = new(false, "never asked");
        var between = FirstWait;
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
