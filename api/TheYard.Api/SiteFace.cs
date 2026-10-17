namespace TheYard.Api;

/// <summary>
/// The site's one face for the pages the API draws itself (the About page and the diagram
/// pages): a style line that declares IBM Plex Sans from the file the build hashed under
/// wwwroot/assets, found once, so those pages wear the face the rest of the site wears without
/// asking another host for it. Empty where no built site sits beside the process (the test host,
/// dotnet run against the dev server), where the fallback face in the page's own rule stands.
/// </summary>
public static class SiteFace
{
    private static readonly object Gate = new();
    private static string? foundFor;
    private static string found = string.Empty;

    /// <summary>
    /// The style element for the assets folder, or an empty string when it holds no face. Read
    /// from the disk once per folder and kept, since the file's name changes only with a build.
    /// </summary>
    public static string StyleFor(string assetsRoot)
    {
        lock (Gate)
        {
            if (!string.Equals(foundFor, assetsRoot, StringComparison.Ordinal))
            {
                found = Find(assetsRoot);
                foundFor = assetsRoot;
            }
            return found;
        }
    }

    // #region find
    /// <summary>
    /// The one woff2 the build wrote for the face, by the name src/styles/fonts.css gives it with
    /// the build's hash after it; the first by name when a folder somehow holds two. The rule is
    /// the one fonts.css declares, weights 400 to 700 in one file, swap while it lands.
    /// </summary>
    public static string Find(string assetsRoot)
    {
        if (!Directory.Exists(assetsRoot))
        {
            return string.Empty;
        }
        string? file = Directory.EnumerateFiles(assetsRoot, "ibm-plex-sans-latin*.woff2")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        return file is null
            ? string.Empty
            : "<style>@font-face { font-family: 'IBM Plex Sans'; font-style: normal; font-weight: 400 700; font-display: swap; src: url('/assets/"
                + file + "') format('woff2'); }</style>";
    }
    // #endregion find
}
