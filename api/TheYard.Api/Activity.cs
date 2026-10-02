// Site activity: the graph at the top of the Admin tab and the table behind
// the operator's key (ADR: Site activity, and the line an address does not
// cross). Three pieces: how a request becomes a hit with nothing in it a
// person could be named by, the collector that takes hits off the request
// path and writes them in batches to one keeper (Azure Cosmos DB wherever it
// is configured; each row still names the store that served it), and the
// report the two endpoints serve.
//
// This file is the place to start, and holds the first piece. The parts, one type to a file:
//   VisitorTokens          Activity.cs              the address made into a token and a network
//   Hits                   Hits.cs                  what a request is recorded as, and what is not
//   ActivityCollector      ActivityCollector.cs     hits off the request path, written in batches
//   ActivityWindows        ActivityWindows.cs       the windows the graph offers
//   ActivityReport         ActivityReport.cs        the report both endpoints are built from
//   ActivityWho            ActivityWho.cs           who a visitor-day was: people, scanners, the site
//   AdminKey               AdminKey.cs              the key the visitor rows sit behind
//   ActivityReportCache    ActivityReportCache.cs   the public report, kept a short while per window
using System.Security.Cryptography;
using System.Text;
using TheYard.Application;

namespace TheYard.Api;

// #region visitor-token
/// <summary>
/// The address, made into something the page can group by and nobody can
/// reverse. A keyed hash of the day and the address: keyed with the signing
/// key, which every container shares and nobody outside has, so the same
/// address is the same token on both sites within a day and a different one
/// tomorrow; the day is in the hash rather than in a salt table, so there is
/// no table of salts to keep or to lose. The address itself is kept only as
/// its first three octets, which is enough to see a network, a country and an
/// obvious scanner range, and not enough to name a machine. A full address is
/// never stored, and a full address is never sent anywhere from here.
/// </summary>
public sealed class VisitorTokens(string key)
{
    /// <summary>The signing key as bytes, the key of every hash this makes.</summary>
    private readonly byte[] _key = Encoding.UTF8.GetBytes(key);

    /// <summary>Thirty-two hex characters: the first sixteen bytes of the keyed hash.</summary>
    public string TokenFor(string address, DateTimeOffset at)
    {
        byte[] hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ActivityFolding.DayOf(at) + "|" + address));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    /// <summary>203.0.113.7 becomes 203.0.113.x; an IPv6 address keeps its first three groups and ends in x; anything unreadable is x.</summary>
    public static string NetworkOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "x";
        }

        string[] octets = address.Split('.');
        if (octets.Length == 4 && octets.All(octet => int.TryParse(octet, out int value) && value is >= 0 and <= 255))
        {
            return $"{octets[0]}.{octets[1]}.{octets[2]}.x";
        }

        if (address.Contains(':', StringComparison.Ordinal))
        {
            string[] groups = address.Split(':');
            return string.Join(':', groups.Take(3)) + ":x";
        }

        return "x";
    }

    /// <summary>
    /// The visitor's address as the edge reports it. Netlify writes the
    /// connecting client's address into its own header and the standard one;
    /// a request that reaches the origin without either is a direct one and
    /// the connection says who. A header is what the sender says it is, which
    /// is fine for a count of visitors and would not be fine for anything that
    /// grants something.
    /// </summary>
    public static string AddressOf(HttpContext context)
    {
        string? netlify = context.Request.Headers["X-Nf-Client-Connection-Ip"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(netlify))
        {
            return WithoutPort(netlify.Trim());
        }

        string? forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return WithoutPort(forwarded.Split(',')[0].Trim());
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "";
    }

    /// <summary>
    /// The address without the port a forwarding hop may have written after
    /// it. App Service's own requests reach the container as 127.0.0.1 with a
    /// port, and with the port left on, it went into the token and the
    /// network: every one of those requests was a new visitor, hundreds a day,
    /// and the network showed the whole address. 203.0.113.7:443 is
    /// 203.0.113.7; [2001:db8::1]:443 is 2001:db8::1; an address with no port
    /// is itself.
    /// </summary>
    public static string WithoutPort(string address)
    {
        if (address.StartsWith('['))
        {
            int close = address.IndexOf(']', StringComparison.Ordinal);
            return close > 1 ? address[1..close] : address;
        }

        int colon = address.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && colon == address.LastIndexOf(':') && address[..colon].Count(c => c == '.') == 3
            ? address[..colon]
            : address;
    }
}
// #endregion visitor-token
