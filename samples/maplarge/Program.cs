using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using TestProject.Application;
using TestProject.Controllers;
using TestProject.Docs;
using TestProject.Domain;
using TestProject.Infrastructure;

namespace TestProject;

/// <summary>
/// The host, and nothing but wiring (ADR-001). The starter's shape is kept: one
/// class, one Main, controllers, static files. Every line here either registers
/// a service or orders the pipeline; a rule in this file would be a bug in
/// layering (ADR-002).
/// </summary>
public sealed class Program
{
    /// <summary>Builds and runs the app.</summary>
    /// <param name="args">Command line arguments, passed to the host builder.</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // #region services
        // The home directory and the limits, from the Files section or FILES__* variables (ADR-003).
        builder.Services.Configure<FilesOptions>(builder.Configuration.GetSection(FilesOptions.Section));
        builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<FilesOptions>>().Value);
        builder.Services.AddSingleton(provider => HomeFor(provider.GetRequiredService<FilesOptions>(), builder.Environment.ContentRootPath));

        // The onion: the port, its one adapter, and the use cases over them (ADR-002).
        builder.Services.AddSingleton<IFileStore, PhysicalFileStore>();
        builder.Services.AddSingleton<FileBrowser>();

        // The documents the app serves about itself, and the footer's version (ADR-012).
        builder.Services.AddSingleton(new DocsCatalog(builder.Environment.ContentRootPath));
        builder.Services.AddSingleton(VersionReader.Read(builder.Environment.ContentRootPath));

        // The wire is snake_case, and every failure is a problem document (ADR-004).
        builder.Services.AddControllers().AddJsonOptions(json =>
        {
            json.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            json.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        });
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<BrowserProblemHandler>();

        // The exact limit is checked per file in FileBrowser.UploadAsync; the server and the form parser
        // get the same number plus a megabyte for the multipart framing, so a request that is merely
        // large reaches the check that can name the file, and one that is absurd is cut off earlier.
        long maxUpload = builder.Configuration.GetSection(FilesOptions.Section).GetValue("MaxUploadBytes", new FilesOptions().MaxUploadBytes);
        long slack = 1024 * 1024;
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = maxUpload + slack);
        builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = maxUpload + slack);
        // #endregion services

        var app = builder.Build();

        // #region pipeline
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        // The starter redirected to HTTPS everywhere. Behind the edge that terminates TLS the app
        // only ever sees HTTP, so the redirect stays for the local run and steps aside in production.
        if (app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();
        // #endregion pipeline

        app.Run();
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
