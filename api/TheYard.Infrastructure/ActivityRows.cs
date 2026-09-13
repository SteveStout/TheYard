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

    public required int Requests { get; set; }

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
    public required string Store { get; set; }

    /// <summary>The UTC day as yyyy-MM-dd, which is also what the token is salted with.</summary>
    public required string Day { get; set; }

    public required string Visitor { get; set; }

    public required string Network { get; set; }

    public required DateTime FirstSeen { get; set; }

    public required DateTime LastSeen { get; set; }

    public required int Requests { get; set; }

    public required int Bots { get; set; }

    public required string Paths { get; set; }
}
// #endregion activity-rows
