using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The Dockerfile lists what the frontend is made of, and so does the
/// repository. This holds the two lists to each other (ADR: The second
/// manifest).
///
/// <para>The image is built from an explicit set of COPY lines rather than the
/// whole tree, which is right: it keeps the layer cache useful and keeps the
/// build honest about its inputs. The cost is a second manifest that nothing
/// checks. Adding <c>public/</c> to the project shipped an image without it,
/// and every gate stayed green, because locally the folder is simply there. It
/// was only visible on the live site, as three 404s, after a deploy.</para>
/// </summary>
public class DockerBuildInputsTests
{
    // #region inputs
    /// <summary>
    /// What Vite reads from the repository root when it builds. Anything here
    /// that exists has to reach the image, or the built site is missing it.
    /// </summary>
    private static readonly string[] FrontendInputs =
    [
        "index.html",
        "vite.config.ts",
        "tsconfig.json",
        "tsconfig.app.json",
        "tsconfig.node.json",
        "src",
        // Vite's publicDir: copied verbatim to the root of the built site.
        "public",
    ];

    [Fact]
    public void Everything_the_frontend_build_reads_is_copied_into_the_image()
    {
        string root = Repo.Root();
        string dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));

        // Only the stage that runs `npm run build`. A COPY in the API stage
        // does not put a file where Vite can see it.
        int start = dockerfile.IndexOf("FROM node:", StringComparison.Ordinal);
        int end = dockerfile.IndexOf("RUN npm run build", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "the Dockerfile should have a node stage that runs the frontend build");
        string stage = dockerfile[start..end];

        var missing = FrontendInputs
            .Where(input => Directory.Exists(Path.Combine(root, input)) || File.Exists(Path.Combine(root, input)))
            .Where(input => !Regex.IsMatch(stage, $@"^COPY\s+(?:[^\s]+\s+)*{Regex.Escape(input)}[\s/]", RegexOptions.Multiline))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "these exist in the repository and the frontend build stage never copies them, so the built site will not have them: "
            + string.Join(", ", missing));
    }
    // #endregion inputs

    [Fact]
    public void The_files_a_crawler_fetches_from_the_root_come_from_the_public_folder()
    {
        // Stated as a test because the reason is not obvious from either file
        // on its own: these three are served from the root of the domain, and
        // the only thing that puts a file at the root of the built site is
        // Vite's publicDir.
        string root = Path.Combine(Repo.Root(), "public");
        foreach (string name in new[] { "robots.txt", "sitemap.xml", "og.png" })
        {
            Assert.True(File.Exists(Path.Combine(root, name)), $"public/{name} is what serves /{name}");
        }
    }
}
