namespace TheYard.Rules;

// #region length-rule
/// <summary>
/// One rule, one body, compiled into both test projects by a link (ADR-075, the addendum of 1.0.4.70): no
/// production source file runs past three hundred lines. A longer file is usually doing more than one job, so it is
/// split by job. Each suite hands it the files it holds.
/// </summary>
public static class LengthRule
{
    /// <summary>The longest a source file may be before it is split by job.</summary>
    public const int Longest = 300;

    /// <summary>Every file over <see cref="Longest"/> lines, as a line naming it and its length.</summary>
    /// <param name="files">The files.</param>
    /// <param name="repoRoot">The repository's root, for the paths the lines print.</param>
    public static List<string> TooLong(IEnumerable<string> files, string repoRoot) =>
        [.. files
            .Select(path => (path, lines: File.ReadAllLines(path).Length))
            .Where(file => file.lines > Longest)
            .Select(file => $"{Path.GetRelativePath(repoRoot, file.path).Replace('\\', '/')} is {file.lines} lines: split it by job")];
}
// #endregion length-rule
