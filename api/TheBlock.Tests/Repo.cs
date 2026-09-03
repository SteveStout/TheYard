namespace TheBlock.Tests;

/// <summary>
/// Where the repository is, from inside a test binary. The suite runs from
/// `api/TheBlock.Tests/bin/...`, so the dataset is found by walking up to the
/// directory that has both a README and a data folder rather than by counting
/// how many `..` that happens to be today.
/// </summary>
internal static class Repo
{
    public static string DataFile(string name) => Path.Combine(Root(), "data", name);

    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "data")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            $"no repository root above {AppContext.BaseDirectory}: looked for a README.md beside a data directory");
    }
}
