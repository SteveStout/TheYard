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
            if (File.Exists(Path.Combine(dir.FullName, "infra", "jobhunt.bicep")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("infra/jobhunt.bicep not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Each table's columns, read from its CREATE TABLE in the SQL project.</summary>
    private static Dictionary<string, HashSet<string>> Tables()
    {
        var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Root(), "functions", "JobHunt.Database", "Tables"), "*.sql"))
        {
            string sql = File.ReadAllText(path);
            var table = CreateTable().Match(sql);
            if (!table.Success)
            {
                // The tracker's tables sit beside the campaign's under the jobhunt schema (ADR-118, the addendum of 1.0.4.72).
                Assert.True(sql.Contains("CREATE TABLE [jobhunt].[", StringComparison.Ordinal), path + " has no CREATE TABLE [campaign].[...] or [jobhunt].[...]");
                continue;
            }
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
        typeof(SqlCampaignStore).Assembly.GetType("JobHunt.Infrastructure.Features.Campaign.CampaignSql", throwOnError: true)!
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
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
            var merge = Merge().Match(sql);
            if (merge.Success)
            {
                string table = merge.Groups[1].Value;
                foreach (Match column in Bracketed().Matches(sql).Where(c => c.Groups[1].Value != "campaign" && !tables.ContainsKey(c.Groups[1].Value)))
                {
                    if (!tables[table].Contains(column.Groups[1].Value))
                    {
                        wrong.Add($"{name}: {table}.{column.Groups[1].Value}");
                    }
                }
                continue;
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
        Assert.True(Statements().Count() >= 16);
    }

    // Every variable the templates may name, with the column the row gives it from; the next test holds the
    // checked-in templates to this list.
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
    public void Every_variable_of_the_checked_in_templates_is_one_the_rows_give()
    {
        var given = typeof(CampaignSqlTests).GetMethod(nameof(Every_template_variable_has_its_column))!
            .GetCustomAttributes<Xunit.InlineDataAttribute>().Select(a => (string)a.GetData(null!).Single()[0]).ToHashSet(StringComparer.Ordinal);
        var named = CampaignTemplate.All.SelectMany(t => TemplateFill.Variables(t.Text)).Distinct(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(named);
        Assert.All(named, variable => Assert.Contains(variable, given));
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
        Assert.Equal(6, Directory.EnumerateFiles(Path.Combine(Root(), "functions", "JobHunt.Database", "Views"), "*.sql").Count());
        // The card schema holds counts only: no view in it names a column that holds a person, a company, a subject or a code (an address is only counted, in a filter).
        foreach (string view in Directory.EnumerateFiles(Path.Combine(Root(), "functions", "JobHunt.Database", "Views"), "Card*.sql"))
        {
            string sql = File.ReadAllText(view);
            foreach (string column in new[] { "[ContactName]", "[FirstName]", "[To]", "[Subject]", "[RefCode]", "[Names]", "[Company]", "[Key]", "[BodyText]", "[BodyHtml]" })
            {
                Assert.DoesNotContain(column, sql, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_schemas_live_in_the_site_s_database_and_no_template_makes_a_second_one()
    {
        // ADR-118, the addendum of 1.0.4.72: one database. The Function's connection names the site's database, the
        // jobhunt schema sits beside campaign and card, and the template that made a database of the job hunt's own is gone.
        string bicep = File.ReadAllText(Path.Combine(Root(), "infra", "jobhunt.bicep"));
        Assert.Contains("param campaignSqlDatabase string = 'sqldb-theyard-ss-basic'", bicep, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Root(), "infra", "jobhunt-sql.bicep")));
        Assert.True(File.Exists(Path.Combine(Root(), "functions", "JobHunt.Database", "Schemas", "jobhunt.sql")));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(Root(), "infra"), "*.bicep").Select(File.ReadAllText), text => text.Contains("sqldb-jobhunt-ss", StringComparison.Ordinal));
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

    [Fact]
    public void A_date_goes_to_SQL_as_a_date()
    {
        // The store's one command builder, called without a connection: a DateOnly becomes a `date` parameter,
        // whose range starts in the year 1, and everything else keeps the type AddWithValue infers.
        var build = typeof(SqlCampaignStore).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Static)!;
        (string Name, object? Value)[] values = [("@day", new DateOnly(2026, 10, 26)), ("@first", new DateOnly(1, 1, 1)), ("@at", new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero)), ("@shadow", true)];
        using var command = (Microsoft.Data.SqlClient.SqlCommand)build.Invoke(null, [null, "SELECT 1", values])!;

        Assert.Equal(System.Data.SqlDbType.Date, command.Parameters["@day"].SqlDbType);
        Assert.Equal(new DateTime(2026, 10, 26), command.Parameters["@day"].Value);
        Assert.Equal(System.Data.SqlDbType.Date, command.Parameters["@first"].SqlDbType);
        Assert.Equal(System.Data.SqlDbType.DateTimeOffset, command.Parameters["@at"].SqlDbType);
        Assert.Equal(System.Data.SqlDbType.Bit, command.Parameters["@shadow"].SqlDbType);

        // Every day the store sends is handed over as a DateOnly, so it goes through the line above: no call
        // turns a day into a DateTime first, and nothing in the Function asks the log for the year 1.
        string store = File.ReadAllText(Path.Combine(Root(), "functions", "JobHunt.Infrastructure", "Features", "Campaign", "SqlCampaignStore.cs"));
        Assert.DoesNotContain("ToDateTime(TimeOnly.MinValue)", store, StringComparison.Ordinal);
        Assert.Matches(@"\(""@day"", centralDay\)", store);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Root(), "functions"), "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !p.Contains("jobhunt.Tests", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("Async(DateOnly.MinValue", File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_days_of_on_are_read_by_their_own_statement()
    {
        string sql = Statements().Single(s => s.Name == "OnDays").Sql;
        Assert.Contains("SELECT DISTINCT e.[CentralDay]", sql, StringComparison.Ordinal);
        Assert.Contains("e.[Shadow] = 0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\b([clev])\.\[(\w+)\]")]
    private static partial Regex Qualified();

    [GeneratedRegex(@"(?:UPDATE|INSERT INTO) \[campaign\]\.\[(\w+)\]")]
    private static partial Regex Target();

    [GeneratedRegex(@"\[(\w+)\]")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"^MERGE \[campaign\]\.\[(\w+)\]", RegexOptions.Multiline)]
    private static partial Regex Merge();
}
