using System.Text.Json;
using System.Text.RegularExpressions;
using TheYard.Api;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The editor configuration under <c>.vscode/</c>, held to the code it points at
/// (ADR: The editor is set up by the repository).
///
/// <para>A launch configuration names a DLL by path and a port by number, and a task
/// names an npm script by name. None of those is checked by anything when it is typed, and
/// each goes stale on its own: a project renamed, a script dropped from package.json, a
/// port moved in launchSettings.json. The failure is the worst kind, because it lands on
/// the newest developer, on the first day, at F5, and teaches them that the setup in the
/// repository is decorative. So the three files are read here, every path and port and
/// script resolved against the tree, and the page that documents them is held to name
/// every task.</para>
/// </summary>
public class WorkspaceTests
{
    // #region reading
    /// <summary>VS Code's files carry comments and trailing commas; the reader allows both.</summary>
    private static readonly JsonDocumentOptions Relaxed = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static JsonDocument Read(string root, params string[] parts) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine([root, .. parts])), Relaxed);

    /// <summary>The scripts a package.json declares, by name.</summary>
    private static HashSet<string> Scripts(string root, params string[] folder)
    {
        using var package = Read(root, [.. folder, "package.json"]);
        return package.RootElement.GetProperty("scripts").EnumerateObject()
            .Select(script => script.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The http port a project's launchSettings.json names, the one the editor must use too.</summary>
    private static int LaunchSettingsPort(string root, string project)
    {
        using var settings = Read(root, project, "Properties", "launchSettings.json");
        foreach (var profile in settings.RootElement.GetProperty("profiles").EnumerateObject())
        {
            string urls = profile.Value.GetProperty("applicationUrl").GetString() ?? "";
            var http = Regex.Match(urls, @"http://localhost:(\d+)");
            if (http.Success)
            {
                return int.Parse(http.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        throw new InvalidOperationException($"{project}/Properties/launchSettings.json names no http://localhost port");
    }

    private static int PortOf(string url) =>
        int.Parse(Regex.Match(url, @"localhost:(\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    // #endregion reading

    // #region tasks
    [Theory]
    [InlineData(".vscode")]
    [InlineData("samples/theshed/.vscode")]
    public void Every_task_runs_a_script_the_package_declares_or_a_project_in_the_tree(string folder)
    {
        string root = Repo.Root();
        string home = Path.GetDirectoryName(Path.Combine(root, folder))!;
        using var tasks = Read(root, folder, "tasks.json");
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var wrong = new List<string>();

        foreach (var task in tasks.RootElement.GetProperty("tasks").EnumerateArray())
        {
            string label = task.GetProperty("label").GetString()!;
            Assert.True(labels.Add(label), $"{folder}/tasks.json lists '{label}' twice");
            Assert.True(task.TryGetProperty("detail", out _), $"'{label}' has no detail, so the picker shows a bare name");

            if (!task.TryGetProperty("command", out var commandElement))
            {
                continue; // a compound of other tasks, checked below by its dependsOn
            }
            string command = commandElement.GetString()!;
            string cwd = task.TryGetProperty("options", out var options) && options.TryGetProperty("cwd", out var cwdElement)
                ? cwdElement.GetString()!.Replace("${workspaceFolder}", home, StringComparison.Ordinal)
                : home;
            Assert.True(Directory.Exists(cwd), $"'{label}' runs in {cwd}, which does not exist");

            var npm = Regex.Match(command, @"^npm (?:run )?(?<script>[\w:-]+)$");
            var dotnet = Regex.Match(command, @"^dotnet (?<verb>build|run|test)(?: (?<path>\S+))?");
            if (npm.Success)
            {
                string script = npm.Groups["script"].Value;
                var scripts = Scripts(cwd);
                if (!scripts.Contains(script))
                {
                    wrong.Add($"'{label}' runs npm script '{script}', which {Path.GetRelativePath(root, cwd)}/package.json does not declare");
                }
            }
            else if (dotnet.Success)
            {
                string path = dotnet.Groups["path"].Success ? Path.Combine(cwd, dotnet.Groups["path"].Value) : cwd;
                bool found = File.Exists(path)
                    || (Directory.Exists(path) && Directory.EnumerateFiles(path, "*.csproj").Any());
                if (!found)
                {
                    wrong.Add($"'{label}' runs {command}, and {path} is neither a project file nor a folder with one");
                }
            }
            else
            {
                wrong.Add($"'{label}' runs '{command}', which is neither an npm script nor a dotnet verb this test can resolve");
            }
        }

        foreach (var task in tasks.RootElement.GetProperty("tasks").EnumerateArray())
        {
            if (task.TryGetProperty("dependsOn", out var dependsOn))
            {
                foreach (var dependency in dependsOn.EnumerateArray())
                {
                    if (!labels.Contains(dependency.GetString()!))
                    {
                        wrong.Add($"'{task.GetProperty("label").GetString()}' depends on '{dependency.GetString()}', which is not a task");
                    }
                }
            }
        }

        Assert.True(labels.Count >= 5, $"{folder}/tasks.json holds {labels.Count} tasks, fewer than it had");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion tasks

    // #region launch
    [Theory]
    [InlineData(".vscode")]
    [InlineData("samples/theshed/.vscode")]
    public void Every_launch_configuration_runs_a_project_in_the_tree_on_the_port_its_own_settings_name(string folder)
    {
        string root = Repo.Root();
        string home = Path.GetDirectoryName(Path.Combine(root, folder))!;
        using var launch = Read(root, folder, "launch.json");
        using var tasks = Read(root, folder, "tasks.json");
        var taskLabels = tasks.RootElement.GetProperty("tasks").EnumerateArray()
            .Select(task => task.GetProperty("label").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var names = new List<string>();
        var wrong = new List<string>();

        foreach (var configuration in launch.RootElement.GetProperty("configurations").EnumerateArray())
        {
            string name = configuration.GetProperty("name").GetString()!;
            names.Add(name);
            if (configuration.TryGetProperty("preLaunchTask", out var pre) && !taskLabels.Contains(pre.GetString()!))
            {
                wrong.Add($"'{name}' runs the task '{pre.GetString()}' first, which {folder}/tasks.json does not define");
            }

            string type = configuration.GetProperty("type").GetString()!;
            if (type == "coreclr")
            {
                // The program is bin/Debug/<tfm>/<Assembly>.dll under the project folder;
                // the project file and its target framework are what the path must agree with.
                string program = configuration.GetProperty("program").GetString()!.Replace("${workspaceFolder}", home, StringComparison.Ordinal);
                var shape = Regex.Match(program.Replace('\\', '/'), @"^(?<project>.+)/bin/Debug/(?<tfm>net\d+\.\d+)/(?<assembly>[^/]+)\.dll$");
                Assert.True(shape.Success, $"'{name}' runs {program}, which is not a bin/Debug/<tfm>/<Assembly>.dll path");
                string projectFolder = shape.Groups["project"].Value;
                string? csproj = Directory.Exists(projectFolder)
                    ? Directory.EnumerateFiles(projectFolder, "*.csproj").FirstOrDefault()
                    : null;
                if (csproj is null)
                {
                    wrong.Add($"'{name}' runs a DLL under {projectFolder}, which holds no project");
                    continue;
                }
                string projectText = File.ReadAllText(csproj);
                string framework = Regex.Match(projectText, @"<TargetFramework>([^<]+)</TargetFramework>").Groups[1].Value;
                if (framework != shape.Groups["tfm"].Value)
                {
                    wrong.Add($"'{name}' expects {shape.Groups["tfm"].Value} and {Path.GetFileName(csproj)} targets {framework}");
                }
                string assembly = Regex.Match(projectText, @"<AssemblyName>([^<]+)</AssemblyName>") is { Success: true } named
                    ? named.Groups[1].Value
                    : Path.GetFileNameWithoutExtension(csproj);
                if (assembly != shape.Groups["assembly"].Value)
                {
                    wrong.Add($"'{name}' runs {shape.Groups["assembly"].Value}.dll and the project builds {assembly}.dll");
                }

                int configured = PortOf(configuration.GetProperty("env").GetProperty("ASPNETCORE_URLS").GetString()!);
                int settings = LaunchSettingsPort(root, Path.GetRelativePath(root, projectFolder));
                if (configured != settings)
                {
                    wrong.Add($"'{name}' listens on {configured} and the project's launchSettings.json says {settings}");
                }
            }
            else if (type == "chrome")
            {
                // The page's address is Vite's, the one the browser suite and the proxy agree on.
                string config = File.ReadAllText(Path.Combine(root, "playwright.config.ts"));
                int vite = PortOf(Regex.Match(config, @"baseURL: '(http://localhost:\d+)'").Groups[1].Value);
                int configured = PortOf(configuration.GetProperty("url").GetString()!);
                if (configured != vite)
                {
                    wrong.Add($"'{name}' opens port {configured} and Vite serves {vite}");
                }
            }
            else
            {
                wrong.Add($"'{name}' is of type '{type}', which this test does not know how to resolve");
            }
        }

        if (launch.RootElement.TryGetProperty("compounds", out var compounds))
        {
            foreach (var compound in compounds.EnumerateArray())
            {
                foreach (var member in compound.GetProperty("configurations").EnumerateArray())
                {
                    if (!names.Contains(member.GetString()!))
                    {
                        wrong.Add($"the compound '{compound.GetProperty("name").GetString()}' names '{member.GetString()}', which is not a configuration");
                    }
                }
            }
        }

        Assert.True(names.Count >= 1, $"{folder}/launch.json holds no configuration");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion launch

    // #region settings-and-extensions
    [Fact]
    public void The_default_solution_and_the_language_and_test_extensions_are_the_ones_the_page_promises()
    {
        string root = Repo.Root();
        using var settings = Read(root, ".vscode", "settings.json");
        string solution = settings.RootElement.GetProperty("dotnet.defaultSolution").GetString()!;
        Assert.True(File.Exists(Path.Combine(root, solution)), $".vscode/settings.json opens {solution}, which does not exist");

        using var shedSettings = Read(root, "samples", "theshed", ".vscode", "settings.json");
        string shedSolution = shedSettings.RootElement.GetProperty("dotnet.defaultSolution").GetString()!;
        Assert.True(File.Exists(Path.Combine(root, "samples", "theshed", shedSolution)), $"The Shed's settings.json opens {shedSolution}, which does not exist");

        using var extensions = Read(root, ".vscode", "extensions.json");
        var recommended = extensions.RootElement.GetProperty("recommendations").EnumerateArray()
            .Select(extension => extension.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string needed in new[] { "ms-dotnettools.csharp", "ms-dotnettools.csdevkit", "esbenp.prettier-vscode", "vitest.explorer", "ms-playwright.playwright", "editorconfig.editorconfig" })
        {
            Assert.Contains(needed, recommended);
        }
    }
    // #endregion settings-and-extensions

    // #region the-page
    /// <summary>
    /// The editor page names every task, through the live sample of tasks.json it carries and
    /// the prose around it, so a task added to the file shows on the page that explains them.
    /// </summary>
    [Fact]
    public void The_editor_page_shows_every_task_and_every_launch_configuration()
    {
        string root = Repo.Root();
        string page = LiveSamples.Expand(
            File.ReadAllText(Path.Combine(root, DocumentationCatalog.Files["vscode"])), root, "local");
        var missing = new List<string>();

        using var tasks = Read(root, ".vscode", "tasks.json");
        foreach (var task in tasks.RootElement.GetProperty("tasks").EnumerateArray())
        {
            string label = task.GetProperty("label").GetString()!;
            if (!page.Contains(label, StringComparison.Ordinal))
            {
                missing.Add($"the task '{label}'");
            }
        }
        using var launch = Read(root, ".vscode", "launch.json");
        foreach (var configuration in launch.RootElement.GetProperty("configurations").EnumerateArray())
        {
            string name = configuration.GetProperty("name").GetString()!;
            if (!page.Contains(name, StringComparison.Ordinal))
            {
                missing.Add($"the launch configuration '{name}'");
            }
        }

        Assert.True(missing.Count == 0, "the editor page does not show " + string.Join(", ", missing));
        Assert.DoesNotContain("Sample unavailable", page, StringComparison.Ordinal);
    }
    // #endregion the-page
}
