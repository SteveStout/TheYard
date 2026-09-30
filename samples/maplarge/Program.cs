using TestProject.Composition;

namespace TestProject;

/// <summary>
/// The entry point. It builds the web host and starts it. Each step is a single call, and the
/// comment beside the call names the file that holds the details. The setup lives in those
/// files so that this class stays a short, readable list of what the app is made of.
/// </summary>
public sealed class Program
{
    /// <summary>Builds the web app from its parts, in order, and runs it until shutdown.</summary>
    /// <param name="args">Command line arguments, passed to the host builder.</param>
    public static void Main(string[] args)
    {
        // #region composition
        var builder = WebApplication.CreateBuilder(args);

        builder.AddTheShedFiles();                    // Composition/FilesRegistration.cs
        builder.AddTheShedDocumentationAndVersion();  // Composition/DocumentationAndVersionRegistration.cs
        builder.AddTheShedApi();                      // Composition/ApiRegistration.cs

        var app = builder.Build();
        app.UseTheShedRequestPipeline();              // Composition/RequestPipeline.cs
        app.MapControllers();                         // Controllers/: Files, Docs, Health
        // #endregion composition

        app.Run();
    }
}
