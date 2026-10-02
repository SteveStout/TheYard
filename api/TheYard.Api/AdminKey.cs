// The operator's key, the one thing standing between a stranger and the
// visitor rows. Its own file because it guards more than activity (the kept
// logs and the operator's reset link ask it too), and a reader looking for
// what the key admits should find it by name; Activity.cs lists the other parts.
using System.Security.Cryptography;
using System.Text;

namespace TheYard.Api;

// #region report
/// <summary>
/// The key the visitor rows sit behind. Configuration, filled at roll time
/// like the signing key; unset means the endpoint does not exist, which is
/// the default and the safe one. Compared in constant time, and only ever
/// against the whole value.
/// </summary>
public sealed class AdminKey(string? configured)
{
    /// <summary>The configured key as bytes, or null when it is unset or still an unsubstituted placeholder such as __ADMIN_KEY__ (anything starting with two underscores).</summary>
    private readonly byte[]? _key = string.IsNullOrWhiteSpace(configured) || configured.StartsWith("__", StringComparison.Ordinal)
        ? null
        : Encoding.UTF8.GetBytes(configured.Trim());

    /// <summary>Whether a key is configured at all; without one the keyed endpoints do not exist.</summary>
    public bool Configured => _key is not null;

    /// <summary>Whether the presented value is the whole key, compared in constant time so the time taken says nothing about how close a guess was.</summary>
    public bool Admits(string? presented)
    {
        if (_key is null || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(presented);
        return bytes.Length == _key.Length && CryptographicOperations.FixedTimeEquals(bytes, _key);
    }
}
// #endregion report
