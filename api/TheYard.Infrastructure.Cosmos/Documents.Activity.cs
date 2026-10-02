// The two document shapes of the activity container: one store's requests in one UTC hour, and
// one visitor token on one store on one day, both partitioned on the day. One file per
// container, so what a container holds is one short file to open.
namespace TheYard.Infrastructure.Cosmos;

// #region documents
// #region activity-documents
/// <summary>
/// One store's requests in one UTC hour, partitioned on the day. Moved in
/// place by partial updates, so the counters add under two writers
/// (ADR: Site activity, and the line an address does not cross).
/// </summary>
public sealed class ActivityHourDocument
{
    /// <summary>The value of <see cref="Kind"/> on every hour document, which is how a query tells hours from visitors.</summary>
    public const string KindName = "hour";

    /// <summary>hour:{store}:{yyyy-MM-ddTHH}, so the same hour on the same store is always the same document.</summary>
    public string Id { get; set; } = "";

    /// <summary>Which of the two shapes this is: always <see cref="KindName"/> (<c>kind</c>).</summary>
    public string Kind { get; set; } = KindName;

    /// <summary>The UTC day the hour falls in, and the partition key (<c>day</c>).</summary>
    public string Day { get; set; } = "";

    /// <summary>The store that answered the requests (<c>store</c>).</summary>
    public string Store { get; set; } = "";

    /// <summary>The start of the hour, as a round-trip UTC timestamp (<c>hour</c>).</summary>
    public string Hour { get; set; } = "";

    /// <summary>How many requests the store answered in the hour (<c>requests</c>).</summary>
    public int Requests { get; set; }

    /// <summary>How many of those requests came from bots (<c>bots</c>).</summary>
    public int Bots { get; set; }

    /// <summary>Request counts by path for the hour (<c>paths</c>).</summary>
    public Dictionary<string, int> Paths { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The id of the hour document for a store and an hour.</summary>
    public static string IdFor(string store, DateTimeOffset hour) =>
        $"hour:{store}:{hour.ToUniversalTime():yyyy-MM-dd'T'HH}";
}

/// <summary>
/// One visitor token on one store on one UTC day, partitioned on the day. The
/// token is a keyed hash that rotates with the day; the network is the address
/// cut to three octets; nothing here can name a person.
/// </summary>
public sealed class ActivityVisitorDocument
{
    /// <summary>The value of <see cref="Kind"/> on every visitor document, which is how a query tells visitors from hours.</summary>
    public const string KindName = "visitor";

    /// <summary>visitor:{store}:{token}. Unique within the day partition, which is what the token is scoped to.</summary>
    public string Id { get; set; } = "";

    /// <summary>Which of the two shapes this is: always <see cref="KindName"/> (<c>kind</c>).</summary>
    public string Kind { get; set; } = KindName;

    /// <summary>The UTC day the token belongs to, and the partition key (<c>day</c>).</summary>
    public string Day { get; set; } = "";

    /// <summary>The store the visitor reached (<c>store</c>).</summary>
    public string Store { get; set; } = "";

    /// <summary>The day's keyed hash of the visitor, never the visitor (<c>visitor</c>).</summary>
    public string Visitor { get; set; } = "";

    /// <summary>The caller's address cut to three octets (<c>network</c>).</summary>
    public string Network { get; set; } = "";

    /// <summary>The visitor's first request of the day on this store (<c>first_seen</c>).</summary>
    public DateTimeOffset FirstSeen { get; set; }

    /// <summary>The visitor's latest request of the day on this store (<c>last_seen</c>).</summary>
    public DateTimeOffset LastSeen { get; set; }

    /// <summary>How many requests the visitor made that day (<c>requests</c>).</summary>
    public int Requests { get; set; }

    /// <summary>How many of those requests were a bot's (<c>bots</c>).</summary>
    public int Bots { get; set; }

    /// <summary>Request counts by path for the visitor's day (<c>paths</c>).</summary>
    public Dictionary<string, int> Paths { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The hosts that linked here on a page load, with their counts; absent on a document written before sources were kept, which reads as none (<c>sources</c>).</summary>
    public Dictionary<string, int> Sources { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The id of the visitor document for a store and a day's token.</summary>
    public static string IdFor(string store, string visitor) => $"visitor:{store}:{visitor}";
}
// #endregion activity-documents
// #endregion documents
