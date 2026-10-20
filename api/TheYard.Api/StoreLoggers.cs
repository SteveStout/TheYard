// The host's loggers, which exist only once the host does, for a store attached after startup.
namespace TheYard.Api;

/// <summary>
/// The host's loggers, which exist only once the host does. A context factory made before then
/// (at startup) is attached by StoreAnnouncement; one made later (the second chance) reads this.
/// </summary>
public sealed class StoreLoggers
{
    /// <summary>The host's logger factory, or null before the host has started.</summary>
    public ILoggerFactory? Factory { get; set; }
}
