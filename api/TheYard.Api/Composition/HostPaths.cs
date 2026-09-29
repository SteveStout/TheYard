namespace TheYard.Api;

/// <summary>
/// Where this process finds the files it serves, found once at startup by walking
/// up from the content root, so `dotnet run`, the test host and the image all
/// resolve them from one line.
/// </summary>
/// <param name="DataPath">The dataset, data/vehicles.json.</param>
/// <param name="ReadmePath">The README, whose folder is the repository root.</param>
/// <param name="TestResultsPath">Every test result from the gate that shipped this build, beside the dataset
/// (ADR: The five-minute gate, the addendum on every check running once).</param>
/// <param name="ResumePath">The resume the About section serves.</param>
/// <param name="ManifestPath">The photo manifest.</param>
/// <param name="ImagesRoot">The photographs, served under /api/images.</param>
/// <param name="RepoRoot">The folder README.md sits in, in the image and in a checkout: live code samples
/// (ADR-014) read whitelisted source files under it.</param>
public sealed record HostPaths(
    string DataPath,
    string ReadmePath,
    string TestResultsPath,
    string ResumePath,
    string ManifestPath,
    string ImagesRoot,
    string RepoRoot)
{
    /// <summary>Finds every path from the content root the host was started in.</summary>
    public static HostPaths Find(string contentRoot)
    {
        string dataPath = FindUpward(contentRoot, Path.Combine("data", "vehicles.json"));
        string readmePath = FindUpward(contentRoot, "README.md");
        return new HostPaths(
            dataPath,
            readmePath,
            Path.Combine(Path.GetDirectoryName(dataPath)!, "test-results.json"),
            Path.Combine(contentRoot, "wwwroot", "docs", "resume.pdf"),
            Path.Combine(contentRoot, "photo-manifest.json"),
            Path.Combine(contentRoot, "wwwroot", "images"),
            Path.GetDirectoryName(readmePath)!);
    }

    #region find-upward
    // Started from three different folders (dotnet run, the test host's bin
    // directory, /app in the image), so nothing may assume a fixed depth. Walking
    // up until the file appears works from all three, and the throw names what
    // was missing instead of failing later as a null.
    private static string FindUpward(string startDirectory, string relativePath)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException($"Could not locate {relativePath} in or above {startDirectory}");
    }
    #endregion find-upward
}
