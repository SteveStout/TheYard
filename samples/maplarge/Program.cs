using TestProject.Composition;

namespace TestProject;

/// <summary>
/// The host, as a table of contents (ADR-001, ADR-002). The starter's shape is kept: one class,
/// one Main. Each line names a part of the app in the order it is made, and the comment beside it
/// names the file that shows how, the way TheYard's Program.cs reads.
/// </summary>
public sealed class Program
{
    /// <summary>Builds and runs the app.</summary>
    /// <param name="args">Command line arguments, passed to the host builder.</param>
    public static void Main(string[] args)
    {
        // #region composition
        var builder = WebApplication.CreateBuilder(args);

        builder.AddTheShedFiles();          // Composition/FilesRegistration.cs
        builder.AddTheShedDocs();           // Composition/DocsRegistration.cs
        builder.AddTheShedApi();            // Composition/ApiRegistration.cs

        var app = builder.Build();
        app.UseTheShedRequestPipeline();    // Composition/RequestPipeline.cs
        app.MapControllers();               // Controllers/: Files, Docs, Health
        // #endregion composition

        app.Run();
    }
}
