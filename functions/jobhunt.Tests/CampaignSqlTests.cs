using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The campaign's database (ADR-118, Decision 2): every column a SqlCampaignStore statement names is a column
/// the SQL project declares, every template variable has its Leads column, the template makes one Basic
/// database of 5 DTU and 2 GB on the existing server and nothing else, the finder finds nothing, and the
/// database client is reachable from Infrastructure alone. No database runs here; the deploy's read-back is
/// in the DONE note.
/// </summary>
public sealed partial class CampaignSqlTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "infra", "jobhunt-sql.bicep")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("infra/jobhunt-sql.bicep not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Each table's columns, read from its CREATE TABLE in the SQL project.</summary>
    private static Dictionary<string, HashSet<string>> Tables()
    {
        var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Root(), "functions", "JobHunt.Database", "Tables"), "*.sql"))
        {
            string sql = File.ReadAllText(path);
            var table = CreateTable().Match(sql);
            Assert.True(table.Success, path + " has no CREATE TABLE [campaign].[...]");
            var columns = Column().Matches(sql).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            tables[table.Groups[1].Value] = columns;
        }
        return tables;
    }

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["c"] = "Companies",
        ["l"] = "Leads",
        ["e"] = "EmailsSent",
        ["v"] = "CompanyEvidence",
    };

    private static IEnumerable<(string Name, string Sql)> Statements() =>
        typeof(SqlCampaignStore).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!));

    [Fact]
    public void Every_column_a_statement_names_is_declared_by_the_sql_project()
    {
        var tables = Tables();
        Assert.Equal(["Companies", "CompanyEvidence", "EmailsSent", "Leads"], tables.Keys.Order(StringComparer.Ordinal));
        var wrong = new List<string>();
        foreach (var (name, sql) in Statements())
        {
            foreach (Match m in Qualified().Matches(sql))
            {
                string table = Aliases[m.Groups[1].Value];
                if (!tables[table].Contains(m.Groups[2].Value))
                {
                    wrong.Add($"{name}: {table}.{m.Groups[2].Value}");
                }
            }
            var target = Target().Match(sql);
            if (target.Success)
            {
                string table = target.Groups[1].Value;
                string body = sql[target.Index..];
                body = body.Contains("WHERE", StringComparison.Ordinal) ? body[..body.IndexOf("WHERE", StringComparison.Ordinal)] : body;
                foreach (Match column in Bracketed().Matches(body).Skip(2))
                {
                    if (!tables[table].Contains(column.Groups[1].Value))
                    {
                        wrong.Add($"{name}: {table}.{column.Groups[1].Value}");
                    }
                }
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        Assert.True(Statements().Count() >= 10);
    }

    // The variables of TEMPLATE-A-posted-v1.md and TEMPLATE-B-cold-v1.md (J:\mentor\email-templates, read
    // 2026-10-24) and the optional line of 8.2; the templates come into the repository with the renderer.
    [Theory]
    [InlineData("FirstName", "Leads", "FirstName")]
    [InlineData("Company", "Leads", "Company")]
    [InlineData("WhatTheyDo", "Leads", "WhatTheyDo")]
    [InlineData("RoleTitle", "Leads", "RoleTitle")]
    [InlineData("AppliedWhen", "Leads", "AppliedAt")]
    [InlineData("ResumeLink", "Leads", "ResumeLink")]
    [InlineData("SiteLink", "Leads", "SiteLink")]
    [InlineData("GitHubLink", "Leads", "GitHubLink")]
    [InlineData("LinkedInLink", "Leads", "LinkedInLink")]
    [InlineData("AboutLink", "Leads", "AboutLink")]
    [InlineData("Proof", "Leads", "ProofKey")]
    [InlineData("LastDotNetPostingTitle", "Companies", "LastDotNetPostingTitle")]
    [InlineData("LastDotNetPostingMonth", "Companies", "LastDotNetPostingAt")]
    public void Every_template_variable_has_its_column(string variable, string table, string column)
    {
        Assert.True(Tables()[table].Contains(column), $"{{{variable}}} has no {table}.{column}");
    }

    [Fact]
    public void The_rows_carry_what_the_rules_and_the_bounce_join_read()
    {
        var tables = Tables();
        foreach (string column in new[] { "Key", "ParentKey", "Names", "RemoteStance", "LastDotNetPostingAt", "BoardKind", "BoardToken", "ScannedAt", "DoNotEmail", "DoNotEmailAt" })
        {
            Assert.Contains(column, tables["Companies"]);
        }
        foreach (string column in new[] { "Track", "ContactEmail", "EmailSource", "MxChecked", "Verified", "AppliedAt", "LinkedInInviteSentAt", "LinkedInAcceptedAt", "ProofKey", "FollowUpPostSlug", "GhostScore", "Strength", "StrengthWhy", "RankInputsHash", "RankedAt", "Blocked", "RefCode", "CreatedBy" })
        {
            Assert.Contains(column, tables["Leads"]);
        }
        foreach (string column in new[] { "LeadId", "Key", "To", "TemplateId", "TemplateVersion", "Subject", "BodyText", "BodyHtml", "SentAt", "GmailMessageId", "GmailThreadId", "RfcMessageId", "Touch", "Shadow", "Bounced", "BounceReason", "ReplyReceivedAt" })
        {
            Assert.Contains(column, tables["EmailsSent"]);
        }
        Assert.Equal(3, Directory.EnumerateFiles(Path.Combine(Root(), "functions", "JobHunt.Database", "Views"), "*.sql").Count());
    }

    [Fact]
    public void The_template_makes_one_basic_database_on_the_existing_server_and_nothing_else()
    {
        string bicep = File.ReadAllText(Path.Combine(Root(), "infra", "jobhunt-sql.bicep"));
        Assert.Single(Regex.Matches(bicep, @"^resource \w+ '[^']+' = \{", RegexOptions.Multiline));
        Assert.Contains("resource server 'Microsoft.Sql/servers@2023-08-01-preview' existing", bicep, StringComparison.Ordinal);
        Assert.Contains("param sqlServerName string = 'sql-theyard-ss-westus3'", bicep, StringComparison.Ordinal);
        Assert.Contains("param databaseName string = 'sqldb-jobhunt-ss'", bicep, StringComparison.Ordinal);
        Assert.Matches(@"name: 'Basic'\s+tier: 'Basic'\s+capacity: 5", bicep);
        Assert.Contains("maxSizeBytes: 2147483648", bicep, StringComparison.Ordinal);
        Assert.Contains("requestedBackupStorageRedundancy: 'Local'", bicep, StringComparison.Ordinal);
        Assert.DoesNotContain("useFreeLimit", bicep, StringComparison.Ordinal);
        Assert.DoesNotContain("administratorLoginPassword", bicep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task There_is_no_finder_and_it_spends_nothing()
    {
        IAddressFinder finder = new NoFinder();
        Assert.Null(await finder.FindAsync("Pat Lee", "acme.com", CancellationToken.None));
        Assert.Equal("none (public sources only)", finder.Name);
    }

    [GeneratedRegex(@"CREATE TABLE \[campaign\]\.\[(\w+)\]")]
    private static partial Regex CreateTable();

    [GeneratedRegex(@"^\s+\[(\w+)\]\s+(?:n?varchar|int|bigint|smallint|tinyint|bit|date|datetimeoffset|char)", RegexOptions.Multiline)]
    private static partial Regex Column();

    [GeneratedRegex(@"\b([clev])\.\[(\w+)\]")]
    private static partial Regex Qualified();

    [GeneratedRegex(@"(?:UPDATE|INSERT INTO) \[campaign\]\.\[(\w+)\]")]
    private static partial Regex Target();

    [GeneratedRegex(@"\[(\w+)\]")]
    private static partial Regex Bracketed();
}
