using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using TestProject.Application;
using TestProject.Domain;
using TestProject.Infrastructure;

namespace TestProject.Composition;

/// <summary>
/// Registers the file browser's services: the home directory and its limits, the file store
/// interface with the class that reads and writes real files, and the FileBrowser that holds
/// the rules. It also sets the server's upload size limits to match the configured maximum.
/// </summary>
public static class FilesRegistration
{
    /// <summary>
    /// Adds the home directory, the file store, the FileBrowser and the upload size limits to the
    /// host.
    /// </summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheShedFiles(this WebApplicationBuilder builder)
    {
        // #region files
        // Reads the "Files" section of appsettings.json into FilesOptions. An environment variable
        // named FILES__ plus the setting (FILES__HOME, for example) replaces the value from the
        // file, which is how a server chooses its folder without editing the code. The options and
        // the HomePath built from them are registered once, so every class sees the same folder.
        builder.Services.Configure<FilesOptions>(builder.Configuration.GetSection(FilesOptions.Section));
        builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<FilesOptions>>().Value);
        builder.Services.AddSingleton(provider => HomeFor(provider.GetRequiredService<FilesOptions>(), builder.Environment.ContentRootPath));

        // IFileStore is the interface the rules use to touch the disk, and PhysicalFileStore is
        // the class that reads and writes real files. FileBrowser holds the rules and depends only
        // on the interface, so tests can swap in a different store.
        //
        // This sample keeps files on the server's own disk to stay simple. For production, store
        // them in Azure Blob Storage or another cloud file storage service, the way TheYard keeps
        // its data in managed Azure services rather than on a server. A container's disk is wiped
        // when the app restarts or redeploys and is not shared between instances, so uploads here
        // do not last. The move is one new class that implements IFileStore and this one line.
        builder.Services.AddSingleton<IFileStore, PhysicalFileStore>();
        builder.Services.AddSingleton<FileBrowser>();

        // FileBrowser.UploadAsync checks the exact per-file limit and names the file in its error.
        // The web server and the form parser get that limit plus one megabyte, to allow for the
        // multipart form's own headers and boundaries. A slightly large request therefore still
        // reaches the per-file check and gets a clear message, while a far too large request is
        // cut off by the server before it is read.
        long maxUpload = builder.Configuration.GetSection(FilesOptions.Section).GetValue("MaxUploadBytes", new FilesOptions().MaxUploadBytes);
        long slack = 1024 * 1024;
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = maxUpload + slack);
        builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = maxUpload + slack);
        // #endregion files
    }

    /// <summary>The practice folder that ships with the project, shown when no folder is set.</summary>
    private const string PracticeFolder = "sample-home";

    /// <summary>
    /// Picks the folder the app shows and hands it to HomePath, the guard that keeps every
    /// request inside it. The folder comes from Files:Home in appsettings.json, or from the
    /// FILES__HOME environment variable, which wins. Nothing set means the sample-home practice
    /// folder.
    /// </summary>
    /// <param name="options">The Files section of configuration.</param>
    /// <param name="contentRoot">The folder the project runs from.</param>
    public static HomePath HomeFor(FilesOptions options, string contentRoot)
    {
        // Which folder was chosen? Nothing chosen means the practice folder.
        string chosen = string.IsNullOrWhiteSpace(options.Home) ? PracticeFolder : options.Home;

        // A full path (C:\files or /srv/files) is used as written. A short one (my-files) is
        // taken from the folder the project runs from.
        string folder = Path.IsPathRooted(chosen) ? chosen : Path.Combine(contentRoot, chosen);

        // Make the folder if it is not there yet, so a fresh copy runs with no setup.
        Directory.CreateDirectory(folder);

        // From here on, HomePath is the only code that turns a request into a real path on disk,
        // and it refuses any path that would leave this folder.
        return new HomePath(folder);
    }
}
