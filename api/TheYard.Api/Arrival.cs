// How a request arrived through the proxies in front of this process: the one address that shows it, and the shape the
// pipeline keeps the raw forwarded headers in before the middleware consumes them. Its own file because the pipeline and
// the Admin endpoint both name it (ADR: The order of the request pipeline).

namespace TheYard.Api;

/// <summary>The arrival read: its address, and the raw forwarded headers kept for it on the way in.</summary>
public static class Arrival
{
    /// <summary>The keyed read that shows how the caller's own request arrived.</summary>
    public const string Path = "/api/admin/arrival";

    /// <summary>Where the pipeline keeps <see cref="Raw"/> on the request, for this address only.</summary>
    public const string RawKey = "TheYard.Arrival.Raw";

    /// <summary>The two forwarded headers exactly as they reached this process, before the middleware took its entries.</summary>
    /// <param name="ForwardedFor">X-Forwarded-For as it arrived: every address every proxy added, leftmost first.</param>
    /// <param name="ForwardedProto">X-Forwarded-Proto as it arrived.</param>
    public sealed record Raw(string ForwardedFor, string ForwardedProto);
}
