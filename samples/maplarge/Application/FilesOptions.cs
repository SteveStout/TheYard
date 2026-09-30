namespace TestProject.Application;

/// <summary>
/// The settings for the file browser. They are read from the <c>Files</c> section of
/// appsettings.json, and any of them can be overridden by a <c>FILES__*</c> environment
/// variable (for example <c>FILES__HOME</c>).
/// </summary>
public sealed class FilesOptions
{
    /// <summary>The name of the configuration section these settings are read from.</summary>
    public const string Section = "Files";

    /// <summary>
    /// The home directory the browser shows. It can be absolute, or relative to the
    /// folder the app runs from. Empty means the <c>sample-home</c> folder that ships
    /// with the project, so a fresh copy of the code runs with something to browse.
    /// </summary>
    public string Home { get; set; } = string.Empty;

    /// <summary>
    /// The largest single file the API accepts as an upload, in bytes. Default 100 MB.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>How many matches a search returns when the request does not give a limit.</summary>
    public int SearchLimit { get; set; } = 200;

    /// <summary>
    /// The highest limit a search may ask for; a larger request is lowered to this.
    /// </summary>
    public int SearchCeiling { get; set; } = 1000;
}
