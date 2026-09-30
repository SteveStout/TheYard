namespace TestProject.Domain;

/// <summary>
/// The home directory and the rules that keep every request inside it. Every path the
/// API accepts is relative to this root. This class is the only place that turns such
/// a path into an absolute one, and the only place that decides a path is refused, so
/// the safety rule lives in one spot. It never reads the filesystem; it works on
/// strings only, so the tests can run it against any root on any operating system.
/// The project follows onion architecture: business rules sit in the innermost layer,
/// Domain, and depend on nothing outside it, while the disk and HTTP sit in the outer
/// layers. This class is that business logic pulled out on its own, which is why it can
/// be tested with no disk, no web server and no database
/// (more in docs/ADR-003-the-line-a-path-cannot-cross.md).
/// </summary>
public sealed class HomePath
{
    // #region guard
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>The absolute path of the home directory, with no trailing separator.</summary>
    public string Root { get; }

    /// <summary>
    /// How paths are compared: without case on Windows, where the filesystem ignores
    /// case, and exactly everywhere else.
    /// </summary>
    private readonly StringComparison _comparison;

    /// <summary>
    /// Creates the home from an absolute directory path, normalised and with any trailing
    /// separator removed. The root is not checked further, because it comes from
    /// configuration and never from a request.
    /// </summary>
    /// <param name="root">The absolute path of the directory to use as home.</param>
    public HomePath(string root)
    {
        if (!System.IO.Path.IsPathRooted(root))
        {
            throw new ArgumentException("The home directory must be an absolute path.", nameof(root));
        }
        Root = System.IO.Path.GetFullPath(root).TrimEnd(Separators);
        _comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    /// <summary>
    /// Turns a path from a request into an absolute path under the root, or throws
    /// <see cref="PathRefusedException"/>. It first refuses three things by looking at the
    /// string alone: a rooted path (a drive letter or a leading slash), a "." or ".."
    /// segment anywhere, and a segment containing a character the operating system does
    /// not allow in a name. It then joins the segments onto the root, normalises the
    /// result, and checks it still lies inside the root. That final check is the safety
    /// net for any trick the first three checks do not anticipate. It is business logic in
    /// the innermost layer of the onion architecture, so it depends on nothing and its tests
    /// run with no disk.
    /// </summary>
    /// <param name="relative">The path the request sent; null or "" means home itself.</param>
    public string Resolve(string? relative)
    {
        string trimmed = (relative ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return Root;
        }
        if (System.IO.Path.IsPathRooted(trimmed) || trimmed.StartsWith('/') || trimmed.StartsWith('\\'))
        {
            throw new PathRefusedException("A path must be relative to the home directory.");
        }
        string[] segments = trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (segment is "." or "..")
            {
                throw new PathRefusedException("A path cannot contain '.' or '..' segments.");
            }
            if (segment.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new PathRefusedException($"'{segment}' is not a valid name.");
            }
        }
        string joined = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, System.IO.Path.Combine(segments)));
        if (!IsInside(joined))
        {
            throw new PathRefusedException("A path must stay inside the home directory.");
        }
        return joined;
    }

    /// <summary>
    /// Returns true when an absolute path is the root itself or lies under it. The root is
    /// matched with a separator after it, so a sibling such as "/home2" does not count as
    /// inside "/home".
    /// </summary>
    /// <param name="absolute">An absolute, normalised path.</param>
    public bool IsInside(string absolute)
    {
        if (string.Equals(absolute, Root, _comparison))
        {
            return true;
        }
        return absolute.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, _comparison)
            || absolute.StartsWith(Root + System.IO.Path.AltDirectorySeparatorChar, _comparison);
    }
    // #endregion guard

    /// <summary>
    /// Converts an absolute path under the root into the form the API sends: relative to
    /// home, with forward slashes, and "" for the root itself. Throws
    /// <see cref="PathRefusedException"/> for a path outside the root.
    /// </summary>
    /// <param name="absolute">An absolute path that <see cref="IsInside"/> accepts.</param>
    public string Relative(string absolute)
    {
        if (!IsInside(absolute))
        {
            throw new PathRefusedException("A path must stay inside the home directory.");
        }
        string rest = absolute.Length <= Root.Length ? string.Empty : absolute[(Root.Length + 1)..];
        return rest.Replace('\\', '/').TrimEnd('/');
    }

    /// <summary>
    /// Returns the folder above a relative path: "" when the path is directly under home,
    /// and null when the path is home itself.
    /// </summary>
    /// <param name="relative">A path relative to home, with forward slashes.</param>
    public static string? ParentOf(string relative)
    {
        if (relative.Length == 0)
        {
            return null;
        }
        int slash = relative.LastIndexOf('/');
        return slash < 0 ? string.Empty : relative[..slash];
    }

    /// <summary>Returns the last segment of a relative path, or "" for home itself.</summary>
    /// <param name="relative">A path relative to home, with forward slashes.</param>
    public static string NameOf(string relative)
    {
        int slash = relative.LastIndexOf('/');
        return slash < 0 ? relative : relative[(slash + 1)..];
    }

    /// <summary>
    /// Checks a name for a new file or folder and returns it with spaces at the ends
    /// removed. The name must be a single segment: not empty, not "." or "..", with no
    /// slash or backslash and no character the operating system forbids. Otherwise it
    /// throws <see cref="PathRefusedException"/>.
    /// </summary>
    /// <param name="name">The name the request sent.</param>
    public static string ValidName(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed is "." or ".." || trimmed.IndexOfAny(Separators) >= 0
            || trimmed.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new PathRefusedException($"'{trimmed}' is not a valid name.");
        }
        return trimmed;
    }
}

/// <summary>
/// Thrown when a request names a path outside the home directory or a name that is not
/// allowed. The API answers with status 400 and the exception message as the detail.
/// </summary>
public sealed class PathRefusedException(string detail) : Exception(detail);
