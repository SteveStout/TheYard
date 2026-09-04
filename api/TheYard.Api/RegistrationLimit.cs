namespace TheYard.Api;

// #region registration-limit
/// <summary>
/// How many accounts this site will let strangers create in an hour, counted
/// across the whole site rather than per visitor.
///
/// <para>Registration is the only write an anonymous caller can make that
/// persists. Everything else a stranger can reach either reads, or writes into
/// a ring buffer with a fixed number of slots, or needs an account first. So
/// this endpoint is the whole of what an unauthenticated caller can add to the
/// database, and until now there was no ceiling on it: a loop could fill
/// AspNetUsers, and each request through it costs a deliberately expensive
/// password hash, so the cheaper attack is not the rows at all, it is one
/// container's CPU.</para>
///
/// <para>The security page argues that a rate limiter here would not be the
/// control that mattered, and the argument is about partitioning: behind the
/// edge the origin sees one address for every visitor, and because the origin
/// is directly reachable an attacker can bypass the edge and forge whatever
/// address they like, so a per-address limit is a global cap wearing a
/// disguise. That argument is correct and it says nothing about this, because
/// this limit does not partition by anything. There is nothing to forge.</para>
///
/// <para>What it costs is honest and worth stating: while somebody is spending
/// the hour's allowance, a real visitor cannot register either. That is the
/// trade, and it is the right way round for a demo. Browsing, signing in and
/// bidding all keep working, an existing account is untouched, and the ceiling
/// is set far above what this site has ever seen in a day, so the only person
/// who meets it is somebody trying to.</para>
/// </summary>
public sealed class RegistrationLimit(int perHour, Func<DateTimeOffset> now)
{
    /// <summary>
    /// Two an hour would be safer and would be a different product. This site
    /// has never seen more than a handful of registrations in a day, most of
    /// them its own tests, so a hundred and twenty leaves three orders of
    /// magnitude of headroom over real use and still bounds a day of abuse to
    /// something a serverless database does not notice.
    /// </summary>
    public const int DefaultPerHour = 120;

    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>
    /// When each of the last <see cref="PerHour"/> registrations happened,
    /// oldest first. A sliding window rather than a bucket that resets on the
    /// hour: a bucket lets an attacker take the whole allowance twice in two
    /// minutes by arriving either side of the reset.
    /// </summary>
    private readonly Queue<DateTimeOffset> _taken = new();

    private readonly Lock _gate = new();

    public int PerHour { get; } = perHour;

    /// <summary>
    /// Take one of the hour's registrations, or say there are none left. A
    /// refusal costs nothing and is not recorded: a caller who is turned away
    /// has not consumed anything, so a flood of refusals cannot extend the
    /// window it is being refused by.
    /// </summary>
    public bool TryTake()
    {
        DateTimeOffset moment = now();
        lock (_gate)
        {
            while (_taken.Count > 0 && moment - _taken.Peek() >= Window)
            {
                _taken.Dequeue();
            }

            if (_taken.Count >= PerHour)
            {
                return false;
            }

            _taken.Enqueue(moment);
            return true;
        }
    }
}
// #endregion registration-limit
