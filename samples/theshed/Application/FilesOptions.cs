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

    /// <summary>
    /// Whether upload, new folder, delete, move and copy are answered at all. Off unless a setting
    /// turns it on: the public site is a demonstration anybody can open, and a file browser that
    /// takes anonymous deletes is one a stranger can empty. Off, those routes answer 403 with a
    /// sentence saying why; browse, search and download are unaffected. Development turns it on in
    /// appsettings.Development.json, so a local run can try every route.
    /// </summary>
    public bool AllowWrites { get; set; }

    /// <summary>
    /// The most bytes the home may hold once a write lands. An upload or a copy that would take it
    /// past this is refused, so a loop of copies cannot fill the disk. Default 200 MB; 0 turns
    /// the check off.
    /// </summary>
    public long HomeQuotaBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>How many write requests one visitor may make in a minute before the next is refused with 429.</summary>
    public int WritesPerMinute { get; set; } = 30;
}
