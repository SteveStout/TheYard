using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using TestProject.Application;
using TestProject.Domain;
using TestProject.Infrastructure;

namespace TestProject.Composition;

/// <summary>
/// The file browser itself: the home directory and its limits, the one port and its one adapter,
/// and the use cases over them (ADR-002, ADR-003).
/// </summary>
public static class FilesRegistration
{
    /// <summary>Registers the home, the store, the use cases and the upload limits.</summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheShedFiles(this WebApplicationBuilder builder)
    {
        // #region files
        // The home directory and the limits, from the Files section or FILES__* variables (ADR-003).
        builder.Services.Configure<FilesOptions>(builder.Configuration.GetSection(FilesOptions.Section));
        builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<FilesOptions>>().Value);
        builder.Services.AddSingleton(provider => HomeFor(provider.GetRequiredService<FilesOptions>(), builder.Environment.ContentRootPath));

        // The onion: the port, its one adapter, and the use cases over them (ADR-002).
        builder.Services.AddSingleton<IFileStore, PhysicalFileStore>();
        builder.Services.AddSingleton<FileBrowser>();

        // The exact limit is checked per file in FileBrowser.UploadAsync; the server and the form parser
        // get the same number plus a megabyte for the multipart framing, so a request that is merely
        // large reaches the check that can name the file, and one that is absurd is cut off earlier.
        long maxUpload = builder.Configuration.GetSection(FilesOptions.Section).GetValue("MaxUploadBytes", new FilesOptions().MaxUploadBytes);
        long slack = 1024 * 1024;
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = maxUpload + slack);
        builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = maxUpload + slack);
        // #endregion files
    }

    /// <summary>
    /// The home from configuration: absolute as given, relative to the content
    /// root otherwise, and the sample-home that ships with the project when
    /// nothing is configured, created if it is missing so a clean clone runs.
    /// </summary>
    /// <param name="options">The Files section.</param>
    /// <param name="contentRoot">Where the project runs from.</param>
    public static HomePath HomeFor(FilesOptions options, string contentRoot)
    {
        string configured = string.IsNullOrWhiteSpace(options.Home) ? "sample-home" : options.Home;
        string root = Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot, configured);
        Directory.CreateDirectory(root);
        return new HomePath(root);
    }
}
