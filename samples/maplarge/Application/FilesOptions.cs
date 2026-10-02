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
    /// The folder the app shows. Set it on the Files:Home line in appsettings.json, or with the
    /// FILES__HOME environment variable on a server, which wins over the file. A full path
    /// (C:\files or /srv/files) is used as written; a short one (my-files) is taken from the
    /// folder the app runs from. Left empty, the app shows the <c>sample-home</c> practice
    /// folder that ships with the project, so a fresh copy has something to browse.
    /// Keeping the folder out of the code is what lets one build run on a laptop, a test
    /// machine and a server, each pointed at its own folder with no code change.
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
