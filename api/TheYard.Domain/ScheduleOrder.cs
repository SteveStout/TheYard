using TheYard.Data;

namespace TheYard.Domain;

/// <summary>
/// The catalogue in ending-soonest order, worked out once for a day's anchor
/// rather than once per request (ADR: The search index, the addendum on the
/// schedule order).
///
/// <para>Every vehicle's window depends only on its id and the anchor, so for
/// one anchor the windows never change; what changes as the clock moves is
/// which band each vehicle sits in, live, upcoming or ended. So this holds the
/// catalogue twice, sorted once by end and once by start, and at any instant
/// reads the order off them: the live ones are those that end after now and
/// started by now, closest end first; then the upcoming ones, soonest start
/// first; then the ended ones, most recent end first. A tie keeps the
/// catalogue's own order, which is what the full sort in
/// <see cref="VehicleOrdering"/> does, so the two give the same page.</para>
///
/// <para>Nothing here is stored anywhere: it is the schedule derived again,
/// held in memory for the day it was derived for, and replaced when the anchor
/// moves.</para>
/// </summary>
public sealed class ScheduleOrder
{
    private readonly long[] _starts;
    private readonly long[] _ends;
    private readonly int[] _byEnd;
    private readonly int[] _byStart;

    /// <summary>Derive every vehicle's window for <paramref name="anchorMs"/> and sort the catalogue by end and by start.</summary>
    public ScheduleOrder(IReadOnlyList<Vehicle> vehicles, long anchorMs)
    {
        ArgumentNullException.ThrowIfNull(vehicles);
        AnchorMs = anchorMs;
        int count = vehicles.Count;
        _starts = new long[count];
        _ends = new long[count];
        for (int index = 0; index < count; index++)
        {
            AuctionWindow window = AuctionSchedule.Window(vehicles[index].Id, anchorMs);
            _starts[index] = window.StartsAtMs;
            _ends[index] = window.EndsAtMs;
        }

        _byEnd = SortedBy(_ends);
        _byStart = SortedBy(_starts);
    }

    /// <summary>The anchor the windows were derived for; a clock with another anchor needs another order.</summary>
    public long AnchorMs { get; }

    /// <summary>How many vehicles the order covers.</summary>
    public int Count => _ends.Length;

    /// <summary>
    /// Hands <paramref name="visit"/> the catalogue's positions in ending-soonest
    /// order at <paramref name="nowMs"/>: live by closest end, then upcoming by
    /// soonest start, then ended by most recent end, ties in catalogue order.
    /// The walk stops as soon as <paramref name="visit"/> answers false, so a
    /// caller that needs one page reads no further than the page. A callback
    /// rather than an iterator, because the iterator the compiler writes reads
    /// the thread it runs on from the environment, and this ring reaches
    /// nothing outside itself (OnionTests).
    /// </summary>
    public void Visit(long nowMs, Func<int, bool> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        // Live: ends after now and started by now, closest end first.
        int firstUnended = FirstAfter(_byEnd, _ends, nowMs);
        for (int position = firstUnended; position < _byEnd.Length; position++)
        {
            int index = _byEnd[position];
            if (_starts[index] <= nowMs && !visit(index))
            {
                return;
            }
        }

        // Upcoming: starts after now, soonest first.
        for (int position = FirstAfter(_byStart, _starts, nowMs); position < _byStart.Length; position++)
        {
            if (!visit(_byStart[position]))
            {
                return;
            }
        }

        // Ended: ended by now, most recent first. A run of equal ends is read
        // forwards, so a tie keeps the catalogue's order as the full sort does.
        int last = firstUnended - 1;
        while (last >= 0)
        {
            int first = last;
            while (first > 0 && _ends[_byEnd[first - 1]] == _ends[_byEnd[last]])
            {
                first--;
            }

            for (int position = first; position <= last; position++)
            {
                if (!visit(_byEnd[position]))
                {
                    return;
                }
            }

            last = first - 1;
        }
    }

    /// <summary>Positions sorted by <paramref name="keys"/>, ties by position, so the order is stable.</summary>
    private static int[] SortedBy(long[] keys)
    {
        int[] order = new int[keys.Length];
        for (int index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }

        Array.Sort(order, (left, right) =>
        {
            int byKey = keys[left].CompareTo(keys[right]);
            return byKey != 0 ? byKey : left.CompareTo(right);
        });
        return order;
    }

    /// <summary>The first place in <paramref name="order"/> whose key is later than <paramref name="nowMs"/>.</summary>
    private static int FirstAfter(int[] order, long[] keys, long nowMs)
    {
        int low = 0;
        int high = order.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (keys[order[middle]] <= nowMs)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
