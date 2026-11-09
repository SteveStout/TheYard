using System.Globalization;

namespace JobHunt.Server.Domain.Shared.Registry;

/// <summary>The LIVE and close rows this process drops in append-inbox for state.md, in the shape every lane writes.</summary>
public static class RegistryRows
{
    /// <summary>The lane's name on spool files and in rows.</summary>
    public const string Lane = RegistryTail.OwnLane;

    /// <summary>The spool file's path, relative to the share.</summary>
    /// <param name="at">The instant.</param>
    /// <param name="kind">registry or close.</param>
    public static string Path(DateTimeOffset at, string kind) => $"inbox/append-inbox/state.md/{RunSlot.Stamp(at)}-{Lane}-{kind}.md";

    /// <summary>The LIVE row.</summary>
    /// <param name="at">The instant.</param>
    /// <param name="dry">True for a dry run.</param>
    /// <param name="lanes">The lanes this run will work, in words.</param>
    public static string Live(DateTimeOffset at, bool dry, string lanes) =>
        $"## LANE: {RunId(at)} ({Lane} program, systemd timer, WickedAwsome Chrome). REGISTERED {Clock(at)}. DRY_RUN {dry.ToString().ToLowerInvariant()}. Lanes: {lanes}. Drives LinkedIn in Chrome through claude -p; no other LinkedIn driver while this row is open. | IN PROGRESS |\n";

    /// <summary>The close row.</summary>
    /// <param name="at">The instant.</param>
    /// <param name="started">When the run began.</param>
    /// <param name="card">The card's lines.</param>
    public static string Closed(DateTimeOffset at, DateTimeOffset started, string card) =>
        $"## LANE: {RunId(started)} CLOSED {Clock(at)}. {card.ReplaceLineEndings(" ")} | CLOSED |\n";

    private static string RunId(DateTimeOffset at) =>
        $"{Lane}{RunSlot.Local(at).ToString("MMdd-HHmm", CultureInfo.InvariantCulture)}";

    private static string Clock(DateTimeOffset at) => RunSlot.Local(at).ToString("HH:mm 'Central' ddd M/d", CultureInfo.InvariantCulture);
}
