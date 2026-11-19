using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>The send copy a resume lock names, by file name, with the PDF's SHA-256 the lock records.</summary>
/// <param name="Lock">The lock file's name.</param>
/// <param name="Name">The send copy's file name, at the share's root.</param>
/// <param name="Sha">The SHA-256 the lock records for it, lower case.</param>
public sealed record LockedCopy(string Lock, string Name, string Sha);

/// <summary>
/// The fallback when a tailored build stops (RULE 76's resume fix): the newest RESUME-LOCK-&lt;date&gt;.md names the
/// send copy by name and records its SHA-256, and an application whose own build failed goes out with those bytes
/// instead of waiting another night. A copy whose bytes do not hash to the lock's line is never attached.
/// </summary>
public static partial class ResumeLock
{
    /// <summary>The words a fallback row carries in place of a headline.</summary>
    public const string Marker = "locked send copy";

    /// <summary>The newest lock among the share root's file names, or null.</summary>
    /// <param name="names">File names at the share's root.</param>
    public static string? Newest(IEnumerable<string> names) =>
        names.Where(name => LockName().IsMatch(name)).OrderDescending(StringComparer.Ordinal).FirstOrDefault();

    /// <summary>The send copy and its hash as the lock writes them, or null when either line is missing.</summary>
    /// <param name="lockName">The lock file's name.</param>
    /// <param name="text">The lock file's text.</param>
    public static LockedCopy? Read(string lockName, string text)
    {
        Match name = SendCopyLine().Match(text), sha = ShaLine().Match(text);
        return name.Success && sha.Success ? new LockedCopy(lockName, name.Groups[1].Value, sha.Groups[1].Value.ToLowerInvariant()) : null;
    }

    /// <summary>The build that goes out instead of a failed tailored one: the lock's copy when its bytes match, else a louder failure.</summary>
    /// <param name="failure">Why the tailored build stopped.</param>
    /// <param name="copy">The lock's send copy, or null when no lock could be read.</param>
    /// <param name="path">The send copy's real path.</param>
    /// <param name="actualSha">The SHA-256 of the bytes at that path, or null when the file is missing.</param>
    public static ResumeBuild Fallback(string failure, LockedCopy? copy, string path, string? actualSha)
    {
        if (copy is null)
        {
            return new ResumeBuild(string.Empty, string.Empty, string.Empty, $"{failure}; and no RESUME-LOCK file names a send copy to fall back to");
        }
        if (actualSha is null)
        {
            return new ResumeBuild(string.Empty, string.Empty, string.Empty, $"{failure}; and {copy.Name}, the send copy {copy.Lock} names, is missing");
        }
        if (!string.Equals(actualSha, copy.Sha, StringComparison.OrdinalIgnoreCase))
        {
            return new ResumeBuild(string.Empty, string.Empty, string.Empty, $"{failure}; and LOCK MISMATCH: {copy.Name} hashes {actualSha}, {copy.Lock} records {copy.Sha}, nothing attached");
        }
        return new ResumeBuild(path, $"{Marker} (tailored build failed: {failure})", copy.Sha);
    }

    [GeneratedRegex(@"^RESUME-LOCK-\d{4}-\d{2}-\d{2}\.md$")]
    private static partial Regex LockName();

    [GeneratedRegex(@"^\s*(\S+\.pdf)\s+THE SEND COPY", RegexOptions.Multiline)]
    private static partial Regex SendCopyLine();

    [GeneratedRegex(@"^SHA-256:.*\bpdf\s+([0-9a-fA-F]{64})\b", RegexOptions.Multiline)]
    private static partial Regex ShaLine();
}
