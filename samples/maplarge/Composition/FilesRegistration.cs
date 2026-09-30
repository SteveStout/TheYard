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
        // Reads the "Files" section of configuration (or FILES__* environment variables) into
        // FilesOptions, then registers the options object and the HomePath built from it as
        // singletons, so every class receives the same home directory.
        builder.Services.Configure<FilesOptions>(builder.Configuration.GetSection(FilesOptions.Section));
        builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<FilesOptions>>().Value);
        builder.Services.AddSingleton(provider => HomeFor(provider.GetRequiredService<FilesOptions>(), builder.Environment.ContentRootPath));

        // IFileStore is the interface the rules use to touch the disk, and PhysicalFileStore is
        // the class that reads and writes real files. FileBrowser holds the rules and depends only
        // on the interface, so tests can swap in a different store.
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

    /// <summary>
    /// Works out the home directory from configuration. An absolute path is used as given, and a
    /// relative path is taken from the content root. When nothing is configured, it uses the
    /// sample-home folder that ships with the project. The folder is created if it is missing,
    /// so a fresh copy of the project runs without any setup.
    /// </summary>
    /// <param name="options">The Files section of configuration.</param>
    /// <param name="contentRoot">The folder the project runs from.</param>
    public static HomePath HomeFor(FilesOptions options, string contentRoot)
    {
        string configured = string.IsNullOrWhiteSpace(options.Home) ? "sample-home" : options.Home;
        string root = Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot, configured);
        Directory.CreateDirectory(root);
        return new HomePath(root);
    }
}
