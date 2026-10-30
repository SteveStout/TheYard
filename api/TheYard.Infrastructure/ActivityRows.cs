namespace TheYard.Infrastructure;

// #region activity-rows
// The two activity tables (ADR: Site activity, and the line an address does
// not cross). Both are counters: a batch of hits arrives as a delta and the
// row moves by it. Neither has a column an address, an account or an email
// could go in, which is the privacy rule expressed as a schema.

/// <summary>One store's requests in one UTC hour. Keyed on the pair, so an hour is one row per store.</summary>
public sealed class ActivityHourRow
{
    /// <summary>"sql" or "cosmos", the key the toggle uses.</summary>
    public required string Store { get; set; }

    /// <summary>The hour, UTC, minutes and seconds zero.</summary>
    public required DateTime Hour { get; set; }

    /// <summary>How many requests this store served in this hour. The counter itself: a batch of hits arrives as a delta and this moves by it, so the hour is answered without keeping one row per request.</summary>
    public required int Requests { get; set; }

    /// <summary>How many of those requests looked like a bot. Counted beside the total rather than taken out of it, so the tab can show traffic with and without them from the one row.</summary>
    public required int Bots { get; set; }

    /// <summary>The paths served most in this hour with their counts, as a JSON object, top twenty.</summary>
    public required string Paths { get; set; }
}

/// <summary>
/// One visitor token on one store on one UTC day. The token is a keyed hash
/// that rotates with the day, so the same address is one row within a day
/// and cannot be joined across days; the network is the address cut to its
/// first three octets, which is what the operator sees.
/// </summary>
public sealed class ActivityVisitorRow
{
    /// <summary>"sql" or "cosmos". Kept because one process serves both stores and a visitor of one is not a visitor of the other.</summary>
    public required string Store { get; set; }

    /// <summary>The UTC day as yyyy-MM-dd, which is also what the token is salted with.</summary>
    public required string Day { get; set; }

    /// <summary>The day's keyed hash of the visitor, and part of the key. Kept instead of anything that identifies a person: it counts distinct visitors within a day and cannot be joined to the next day's, which is the privacy rule written as a column.</summary>
    public required string Visitor { get; set; }

    /// <summary>The address cut to its first three octets. Kept because it is as much as the operator needs to tell one neighbourhood of traffic from another, and as little as the line an address does not cross allows.</summary>
    public required string Network { get; set; }

    /// <summary>When this visitor first arrived that day, UTC. Kept because the row is a running total, so without it there would be no record of when the day's traffic from this visitor began.</summary>
    public required DateTime FirstSeen { get; set; }

    /// <summary>When this visitor was last seen that day, UTC. Kept beside the first so the pair says how long the visit lasted, which no count of requests gives.</summary>
    public required DateTime LastSeen { get; set; }

    /// <summary>How many requests this visitor made that day. The counter the row exists for: a delta arrives and this moves by it.</summary>
    public required int Requests { get; set; }

    /// <summary>How many of those looked like a bot. Counted beside the total for the same reason as on the hour: the tab shows both, from one row.</summary>
    public required int Bots { get; set; }

    /// <summary>The paths this visitor asked for most that day with their counts, as a JSON object, top twenty. Kept as one column because it is read and written whole and never queried into, and capped at twenty because the tail of a visitor's paths is a record of a person's browsing rather than a reading of the site.</summary>
    public required string Paths { get; set; }

    /// <summary>The hosts that linked here on a page load, as a JSON object, top twenty; null on a row written before.</summary>
    public string? Sources { get; set; }
}
// #endregion activity-rows
