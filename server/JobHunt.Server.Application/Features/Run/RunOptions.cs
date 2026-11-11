namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// What the command line asked of a run. --slot and --role come together or not at all: a slot run works one caught
/// role, its hand-off a JSON file the pounce wrote under nightly/pounce/, and its slot is pounce- and that posting's key.
/// Without them the run is the morning's, slot 0600.
/// </summary>
/// <param name="Dry">True for --dry-run.</param>
/// <param name="Only">The one lane --lane named, or null for all of them.</param>
/// <param name="Slot">The slot its folders and card are named for.</param>
/// <param name="Role">The hand-off's path relative to the share, or null for the morning.</param>
public sealed record RunOptions(bool Dry, Lane? Only, string Slot = RunSlot.Hour, string? Role = null)
{
    private const string Handoffs = "nightly/pounce/";

    /// <summary>The options a command line asks for, or null when it holds anything this program does not know.</summary>
    /// <param name="arguments">The arguments, the verb first.</param>
    public static RunOptions? Parse(IReadOnlyList<string> arguments)
    {
        bool dry = false;
        Lane? only = null;
        string? slot = null, role = null;
        for (int i = 1; i < arguments.Count; i++)
        {
            string? next = i + 1 < arguments.Count ? arguments[i + 1] : null;
            if (arguments[i] == "--dry-run")
            {
                dry = true;
                continue;
            }
            if (arguments[i] == "--lane" && next is not null && RunSlot.Parse(next) is { } lane)
            {
                only = lane;
            }
            else if (arguments[i] == "--slot" && next is not null && slot is null)
            {
                slot = next;
            }
            else if (arguments[i] == "--role" && next is not null && role is null)
            {
                role = next;
            }
            else
            {
                return null;
            }
            i++;
        }
        if (slot is null && role is null)
        {
            return new RunOptions(dry, only);
        }
        bool fits = slot is not null && slot != RunSlot.Hour && RunSlot.IsSlot(slot)
            && role is not null && role.StartsWith(Handoffs, StringComparison.Ordinal) && role.EndsWith(".json", StringComparison.Ordinal)
            && !role.Contains("..", StringComparison.Ordinal) && !role[Handoffs.Length..].Contains('/');
        return fits ? new RunOptions(dry, only, slot!, role) : null;
    }
}

/// <summary>The checks that stand between the timer and a browser: the mount, the lock, the desktop and the clock.</summary>
/// <param name="Mount">The share's mount.</param>
/// <param name="Lock">The run lock.</param>
/// <param name="Desktop">The desktop.</param>
/// <param name="Time">The clock.</param>
public sealed record RunGuards(IMount Mount, IRunLock Lock, IDesktop Desktop, TimeProvider Time);

/// <summary>The three steps of a lane: what the morning reads, what it builds and what it runs.</summary>
/// <param name="Reader">The morning's reads.</param>
/// <param name="Builder">The worklists.</param>
/// <param name="Runner">The lanes.</param>
/// <param name="Recounter">The live invite count.</param>
/// <param name="Feed">The hot-manager feed, the morning's first lane.</param>
/// <param name="Network">The network log's LinkedIn half, after the messages lane.</param>
public sealed record LaneWork(DayReader Reader, WorklistBuilder Builder, LaneRunner Runner, InviteRecount Recounter, FeedLane Feed, NetworkLane Network);
