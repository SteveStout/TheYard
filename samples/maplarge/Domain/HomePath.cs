namespace TestProject.Domain;

/// <summary>
/// The home directory, and the line a path cannot cross (ADR-003). Every path
/// the API accepts is relative to this root; this class is the only place that
/// turns one into an absolute path, and the only place that decides a path is
/// refused. Pure: it reads no filesystem, so the tests can run it against any
/// root string on any operating system.
/// </summary>
public sealed class HomePath
{
    // #region guard
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>The absolute root, with no trailing separator.</summary>
    public string Root { get; }

    /// <summary>Windows compares paths without case; everything else is exact.</summary>
    private readonly StringComparison _comparison;

    /// <summary>Builds the home from an absolute root. The root itself is trusted: it comes from configuration, never a request.</summary>
    /// <param name="root">The absolute directory that is home.</param>
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
    /// Turns a request path into an absolute path under the root, or throws
    /// <see cref="PathRefusedException"/>. Three refusals, checked as strings
    /// before any filesystem touch: a rooted path (a drive letter or a leading
    /// slash), a "." or ".." segment anywhere, and a segment carrying a character
    /// the operating system forbids in a name. Then the joined path is
    /// normalised and must still start inside the root; that last check is the
    /// one that catches whatever the first three did not think of.
    /// </summary>
    /// <param name="relative">The path as the request sent it; null or "" is home.</param>
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

    /// <summary>True when an absolute path is the root or lies under it.</summary>
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

    /// <summary>The wire form of an absolute path under the root: relative, forward slashes, "" for the root.</summary>
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

    /// <summary>The folder above a wire path, or null for the root.</summary>
    /// <param name="relative">A wire path.</param>
    public static string? ParentOf(string relative)
    {
        if (relative.Length == 0)
        {
            return null;
        }
        int slash = relative.LastIndexOf('/');
        return slash < 0 ? string.Empty : relative[..slash];
    }

    /// <summary>The last segment of a wire path, or "" for the root.</summary>
    /// <param name="relative">A wire path.</param>
    public static string NameOf(string relative)
    {
        int slash = relative.LastIndexOf('/');
        return slash < 0 ? relative : relative[(slash + 1)..];
    }

    /// <summary>
    /// A single name for a new file or folder: no separators, no "." or "..",
    /// no forbidden character. Throws <see cref="PathRefusedException"/> otherwise.
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

/// <summary>A request named a path the home refuses. The API answers 400 with the sentence.</summary>
public sealed class PathRefusedException(string detail) : Exception(detail);
