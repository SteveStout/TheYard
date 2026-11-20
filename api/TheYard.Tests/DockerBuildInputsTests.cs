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
        // The recommendations file: the column draws one blank place per entry in it, read at build time.
        "data",
        // The release facts the landing and the gate badge are drawn with, read by vite.config.ts at build time:
        // 1.0.5.82 shipped without them in the image and both deploys failed at the build (ADR-132).
        "docs/CHANGELOG.md",
        "data/coderabbit-findings.json",
        // The badge's link to the gate run and the landing's last stopped release, read the same way (G5's data).
        "data/test-results.json",
        "data/gate-reds.json",
    ];

    [Fact]
    public void Everything_the_frontend_build_reads_is_copied_into_the_image()
    {
        string root = Repo.Root();
        string dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));

        // Only the stage that runs `npm run build`. A COPY in the API stage
        // does not put a file where Vite can see it.
        int start = dockerfile.IndexOf("FROM mirror.gcr.io/library/node:", StringComparison.Ordinal);
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

    /// <summary>
    /// What the API reads from the repository when it serves a page has to be in the runtime image too: the
    /// Quality page names the job hunt Function's test classes from their folder (QualityCounts.cs), and without
    /// it every Function rule would be counted from the site's same-named tests on the live site alone.
    /// </summary>
    [Fact]
    public void The_runtime_image_carries_the_folders_the_quality_page_reads()
    {
        string dockerfile = File.ReadAllText(Path.Combine(Repo.Root(), "Dockerfile"));
        string runtime = dockerfile[dockerfile.IndexOf(" AS runtime", StringComparison.Ordinal)..];

        Assert.Matches(new Regex(@"^COPY\s+(?:[^\s]+\s+)*functions/jobhunt\.Tests\s", RegexOptions.Multiline), runtime);
    }

    /// <summary>
    /// The frontend stage copies the inputs above and nothing else, and `npm run build` type-checks every
    /// file under src, tests included. A file under src that imports from anywhere else builds on a
    /// developer's machine and in CI, which hold the whole repository, and fails in the image: on
    /// 8 October a test reading data/vehicles.json stopped Deploy at Build and push. So every relative
    /// import under src has to land inside the inputs the stage copies.
    /// </summary>
    [Fact]
    public void Nothing_under_src_imports_a_file_the_image_build_does_not_copy()
    {
        string root = Repo.Root();
        var specifier = new Regex(@"(?:\bfrom\s+|\bimport\s*\(\s*|\bimport\s+)'(\.{1,2}/[^'?]+)");
        var outside = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal)))
        {
            foreach (Match match in specifier.Matches(File.ReadAllText(file)))
            {
                string target = Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, match.Groups[1].Value)));
                string first = target.Split(Path.DirectorySeparatorChar)[0];
                if (!FrontendInputs.Contains(first, StringComparer.Ordinal))
                {
                    outside.Add($"{Path.GetRelativePath(root, file)} imports {match.Groups[1].Value}");
                }
            }
        }

        Assert.True(outside.Count == 0, "the image's frontend stage does not copy these: " + string.Join(", ", outside));
    }

    [Fact]
    public void The_files_a_crawler_fetches_from_the_root_come_from_the_public_folder()
    {
        // Stated as a test because the reason is not obvious from either file
        // on its own: these are served from the root of the domain, and
        // the only thing that puts a file at the root of the built site is
        // Vite's publicDir.
        string root = Path.Combine(Repo.Root(), "public");
        foreach (string name in new[] { "robots.txt", "sitemap.xml", "og.png", $"{ServedAddresses.IndexNowKey}.txt" })
        {
            Assert.True(File.Exists(Path.Combine(root, name)), $"public/{name} is what serves /{name}");
        }
        // The two the blog writes from its store are the API's routes, and a file in the web root would win the route
        // and go stale with the first post saved after the build (ADR: Search engines are told about every post).
        foreach (string name in new[] { "llms.txt", "sitemap-blog.xml" })
        {
            Assert.False(File.Exists(Path.Combine(root, name)), $"public/{name} would hide the API's /{name}, which lists every post");
        }
    }

    [Fact]
    public void The_image_build_stamps_the_sitemap_and_writes_the_verification_tag_only_when_it_is_given()
    {
        string root = Repo.Root();
        string dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));
        string deploy = File.ReadAllText(Path.Combine(root, ".github", "workflows", "deploy.yml"));

        // The build's day replaces the sitemap's placeholder, and a placeholder left behind stops the build.
        Assert.Contains("s/__BUILD_DATE__/$(date -u +%Y-%m-%d)/", dockerfile, StringComparison.Ordinal);
        Assert.Contains("if grep -q \"__BUILD_DATE__\" dist/sitemap.xml; then", dockerfile, StringComparison.Ordinal);
        // #region verification-tag
        // The tag is written in place of index.html's comment, only when the variable is set, and only if it is a token.
        Assert.Contains("ARG GOOGLE_SITE_VERIFICATION=", dockerfile, StringComparison.Ordinal);
        Assert.Contains("if [ -n \"${GOOGLE_SITE_VERIFICATION}\" ]", dockerfile, StringComparison.Ordinal);
        Assert.Contains("*[!A-Za-z0-9_-]*)", dockerfile, StringComparison.Ordinal);
        Assert.Contains("s|<!-- google-site-verification -->|", dockerfile, StringComparison.Ordinal);
        // A repository variable, not a secret: the tag is public by design. Every build of the image passes it,
        // the cache export included, so the cached layers match the pushed ones.
        Assert.Equal(2, Regex.Matches(deploy, @"GOOGLE_SITE_VERIFICATION=\$\{\{ vars\.GOOGLE_SITE_VERIFICATION \}\}").Count);
        Assert.DoesNotContain("secrets.GOOGLE_SITE_VERIFICATION", deploy, StringComparison.Ordinal);
        // #endregion verification-tag
    }
}
