using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// One SQL database, a schema for each area (ADR: One SQL database, one schema per area). No template declares a
/// database, so a database name reaches Azure only through the parameters and settings a connection string is built
/// from: each template's parameter default, the deploy's SQL_DB, and the site's own Azure.Resources names. These
/// tests read those values rather than the literal strings around them, because every Initial Catalog in the
/// repository is a placeholder filled from one of them, and a scan of the literals would pass on any name.
/// </summary>
public sealed partial class OneSqlDatabaseTests
{
    /// <summary>The one database every connection string names.</summary>
    private const string TheDatabase = "sqldb-theyard-ss-basic";

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Repo.Root(), .. parts]));

    private static IEnumerable<string> Under(string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Repo.Root(), folder), pattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal);

    [GeneratedRegex(@"Initial Catalog=\$\{(?<name>\w+)\}")]
    private static partial Regex CatalogFromVariable();

    [GeneratedRegex(@"Initial Catalog=(?<name>[A-Za-z0-9_-]+)")]
    private static partial Regex CatalogLiteral();

    [GeneratedRegex(@"Microsoft\.Sql/servers/databases|resource\s+\w+\s+'databases@", RegexOptions.IgnoreCase)]
    private static partial Regex DatabaseDeclaration();

    [GeneratedRegex(@"\bsqldb-[a-z0-9-]+")]
    private static partial Regex DatabaseName();

    // #region templates
    /// <summary>
    /// A second database starts as a resource in a template, so no template declares one, whether as its own resource
    /// or as a child of a server. The one database was made by hand and is read, never declared.
    /// </summary>
    [Fact]
    public void No_template_declares_a_database()
    {
        var declared = Under("infra", "*.bicep")
            .Where(file => DatabaseDeclaration().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(Repo.Root(), file))
            .ToList();

        Assert.True(declared.Count == 0, "a template declares a SQL database: " + string.Join(", ", declared));
    }

    /// <summary>
    /// Every Initial Catalog in a template is a parameter, so the name that reaches Azure is that parameter's
    /// default. Each one names the one database, and at least the site's and the job hunt's templates build one, so a
    /// template that stopped building a connection string cannot pass by having nothing to check.
    /// </summary>
    [Fact]
    public void Every_template_connection_string_names_the_one_database_by_its_parameter_default()
    {
        var checkedParameters = new List<string>();
        foreach (string file in Under("infra", "*.bicep"))
        {
            string template = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            Assert.DoesNotMatch(CatalogLiteral(), template.Replace("Initial Catalog=${", "", StringComparison.Ordinal));
            foreach (Match catalog in CatalogFromVariable().Matches(template))
            {
                string parameter = catalog.Groups["name"].Value;
                Match declared = Regex.Match(template, $@"param\s+{parameter}\s+string\s*=\s*'(?<value>[^']*)'");
                Assert.True(declared.Success, $"{name}: Initial Catalog reads {parameter}, which has no default to check");
                Assert.Equal(TheDatabase, declared.Groups["value"].Value);
                checkedParameters.Add($"{name}:{parameter}");
            }
        }

        Assert.Contains("appservice.bicep:sqlDatabase", checkedParameters);
        Assert.Contains("jobhunt.bicep:campaignSqlDatabase", checkedParameters);
    }
    // #endregion templates

    // #region the-deploy-and-the-settings
    /// <summary>
    /// The roll builds the site's connection string from the workflow's own variables, so the variable each Initial
    /// Catalog reads is set in that workflow to the one database.
    /// </summary>
    [Fact]
    public void Every_workflow_connection_string_reads_a_variable_set_to_the_one_database()
    {
        int read = 0;
        foreach (string file in Under(Path.Combine(".github", "workflows"), "*.yml"))
        {
            string workflow = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            Assert.DoesNotMatch(CatalogLiteral(), workflow.Replace("Initial Catalog=${", "", StringComparison.Ordinal));
            foreach (Match catalog in CatalogFromVariable().Matches(workflow))
            {
                string variable = catalog.Groups["name"].Value;
                var set = Regex.Matches(workflow, $@"^\s*{variable}:\s*(?<value>\S+)\s*$", RegexOptions.Multiline);
                Assert.True(set.Count > 0, $"{name}: Initial Catalog reads {variable}, which the workflow never sets");
                Assert.All(set, value => Assert.Equal(TheDatabase, value.Groups["value"].Value));
                read++;
            }
        }

        Assert.True(read > 0, "no workflow builds a SQL connection string, so this test checked nothing");
    }

    /// <summary>
    /// The names the Admin cards read the database by, and any database name a template, a workflow or a setting
    /// writes out in full, are the one database. The two deleted ones, the site's serverless database and the
    /// campaign's own, come back only through a line like these.
    /// </summary>
    [Fact]
    public void Every_database_name_in_the_templates_workflows_and_settings_is_the_one_database()
    {
        var files = Under("infra", "*.bicep")
            .Concat(Under(Path.Combine(".github", "workflows"), "*.yml"))
            .Concat(Under("api", "appsettings*.json"))
            .Concat(Under("functions", "*.json"))
            .Concat(Under("server", "*.json"))
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "node_modules"));

        var other = files
            .SelectMany(file => DatabaseName().Matches(File.ReadAllText(file)).Select(found => (File: Path.GetRelativePath(Repo.Root(), file), Name: found.Value)))
            .Where(found => found.Name != TheDatabase)
            .Select(found => $"{found.File}: {found.Name}")
            .ToList();

        Assert.True(other.Count == 0, "a second database is named: " + string.Join(", ", other));
        Assert.Contains($"\"SqlDatabase\": \"{TheDatabase}\"", Read("api", "TheYard.Api", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains($"\"CampaignDatabase\": \"{TheDatabase}\"", Read("api", "TheYard.Api", "appsettings.json"), StringComparison.Ordinal);
    }
    // #endregion the-deploy-and-the-settings
}
