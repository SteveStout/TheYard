namespace TestProject.Application;

/// <summary>
/// The settings the browser reads, bound from the <c>Files</c> section of
/// appsettings.json or the <c>FILES__*</c> environment variables (ADR-003).
/// </summary>
public sealed class FilesOptions
{
    /// <summary>The section name in configuration.</summary>
    public const string Section = "Files";

    /// <summary>
    /// The home directory. Absolute, or relative to the content root; empty means
    /// the <c>sample-home</c> folder that ships beside the project, so a clean
    /// clone runs with something to browse.
    /// </summary>
    public string Home { get; set; } = string.Empty;

    /// <summary>The largest single upload the API accepts, in bytes. 100 MB unless configured.</summary>
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>How many matches a search returns when the request does not say.</summary>
    public int SearchLimit { get; set; } = 200;

    /// <summary>The most matches a request may ask for.</summary>
    public int SearchCeiling { get; set; } = 1000;
}
