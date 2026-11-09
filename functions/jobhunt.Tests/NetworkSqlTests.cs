using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The network log's store and its two Gmail adapters: every column a NetworkSql statement names is declared by the
/// SQL project, the card view carries counts only, a thread's metadata reads as the rules expect, the draft's MIME
/// is held byte for byte and keeps the thread, and nothing in the adapters can send. No database or Gmail runs here.
/// </summary>
public sealed partial class NetworkSqlTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Database")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/JobHunt.Database not found above " + AppContext.BaseDirectory);
    }

    private static string Database(params string[] parts) => Path.Combine([Root(), "functions", "JobHunt.Database", .. parts]);

    private static HashSet<string> Columns(string table) =>
        Column().Matches(File.ReadAllText(Database("Tables", table + ".sql"))).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<(string Name, string Sql)> Statements() =>
        typeof(SqlConversationStore).Assembly.GetType("JobHunt.Infrastructure.Features.NetworkLog.NetworkSql", throwOnError: true)!
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name != "Columns")
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!));

    [Fact]
    public void Every_column_a_statement_names_is_declared_by_the_sql_project()
    {
        var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) { ["Conversations"] = Columns("Conversations"), ["Passes"] = Columns("Passes") };
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal) { ["c"] = "Conversations", ["p"] = "Passes" };
        var wrong = new List<string>();
        foreach (var (name, sql) in Statements().Where(s => s.Name != "CampaignOpenThreads"))
        {
            foreach (Match m in Qualified().Matches(sql))
            {
                if (!tables[aliases[m.Groups[1].Value]].Contains(m.Groups[2].Value))
                {
                    wrong.Add($"{name}: {m.Value}");
                }
            }
            if (Target().Match(sql) is { Success: true } target)
            {
                var declared = tables[target.Groups[1].Value];
                foreach (Match column in Bracketed().Matches(sql[target.Index..]).Where(c => c.Groups[1].Value is not ("network" or "Conversations" or "Passes" or "Id" or "PersonKey" or "CentralDay")))
                {
                    if (!declared.Contains(column.Groups[1].Value))
                    {
                        wrong.Add($"{name}: {target.Groups[1].Value}.{column.Groups[1].Value}");
                    }
                }
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        Assert.Equal(13, Statements().Count());
        Assert.Contains("FROM [campaign].[EmailsSent] e WHERE e.[Shadow] = 0 AND e.[ReplyReceivedAt] IS NULL", Statements().Single(s => s.Name == "CampaignOpenThreads").Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_record_s_fields_are_the_table_s_columns()
    {
        var columns = Columns("Conversations");
        var fields = typeof(Conversation).GetProperties().Where(p => p.Name != "LastExchangeAt").Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(fields.Except(columns));
        Assert.Empty(columns.Except(fields).Except(["CreatedAt", "UpdatedAt"]));
        string sql = File.ReadAllText(Database("Tables", "Conversations.sql"));
        Assert.Contains("CREATE UNIQUE INDEX [UX_Conversations_PersonKey] ON [network].[Conversations] ([PersonKey])", sql, StringComparison.Ordinal);
        Assert.Contains("[State] IN ('Open', 'Drafted', 'Quiet', 'Stopped', 'Closed')", sql, StringComparison.Ordinal);
        Assert.Equal(Enum.GetNames<ConversationState>().Select(n => $"'{n}'"), Regex.Match(sql, @"\[State\] IN \(([^)]*)\)").Groups[1].Value.Split(", "));
        Assert.True(File.Exists(Database("Schemas", "network.sql")));
    }

    [Fact]
    public void The_card_view_carries_counts_only()
    {
        string view = File.ReadAllText(Database("Views", "CardNetwork.sql"));
        Assert.Contains("CREATE VIEW [card].[Network]", view, StringComparison.Ordinal);
        foreach (string column in new[] { "[PersonKey]", "[MatchKey]", "[FirstName]", "[FullName]", "[Company]", "[CompanyKey]", "[Email]", "[ProfileUrl]", "[ThreadIds]", "[LastSubject]", "[TopicLine]", "[DraftId]", "[RefCode]", "[BlogSlug]" })
        {
            Assert.DoesNotContain(column, view, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_thread_s_metadata_reads_as_the_rules_expect()
    {
        const string json = """
            {"id":"t9","messages":[
              {"id":"m1","internalDate":"1759330800000","labelIds":["INBOX"],"snippet":"Happy to talk &amp; compare notes","payload":{"headers":[{"name":"From","value":"Pat Lee <pat@acme.com>"},{"name":"Subject","value":"Platform role"},{"name":"Message-Id","value":"<a1@acme.com>"}]}},
              {"id":"m2","internalDate":"1759417200000","labelIds":["SENT"],"snippet":"Thanks Pat","payload":{"headers":[{"name":"From","value":"Steven Stout <stevenstout11@gmail.com>"},{"name":"To","value":"pat@acme.com"}]}},
              {"id":"m3","internalDate":"1759503600000","labelIds":["DRAFT"],"snippet":"unsent","payload":{"headers":[{"name":"From","value":"Steven Stout <stevenstout11@gmail.com>"}]}}]}
            """;
        using var doc = JsonDocument.Parse(json);
        var thread = GmailThreads.Summary(doc.RootElement);
        Assert.Equal("Platform role", thread.Subject);
        Assert.Equal(2, thread.Messages.Count);
        Assert.False(thread.Messages[0].Outbound);
        Assert.True(thread.Messages[1].Outbound);
        Assert.Equal("Happy to talk & compare notes", thread.Messages[0].Snippet);
        Assert.Equal("<a1@acme.com>", thread.Messages[0].RfcMessageId);
        Assert.NotNull(TwoWayRule.Read(thread));
        Assert.Equal("newer_than:2d -in:chats", GmailThreads.Query(new ThreadSearch(2, 500)));
        Assert.Equal("newer_than:120d -in:chats", GmailThreads.Query(new ThreadSearch(120, 1500)));
    }

    [Fact]
    public void The_draft_keeps_the_thread_and_is_held_byte_for_byte()
    {
        var draft = new DraftMail("t9", "pat@acme.com", "Re: Platform role", "Hi Pat,\n\nSteve", "<div>Hi Pat,</div>", "<a1@acme.com>", "<a1@acme.com>");
        string mime = GmailDrafts.Mime(draft, "b1");
        string expected = "To: pat@acme.com\r\nSubject: Re: Platform role\r\nIn-Reply-To: <a1@acme.com>\r\nReferences: <a1@acme.com>\r\n"
            + "MIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=\"b1\"\r\n\r\n"
            + "--b1\r\nContent-Type: text/plain; charset=\"UTF-8\"\r\nContent-Transfer-Encoding: base64\r\n\r\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Hi Pat,\r\n\r\nSteve")) + "\r\n"
            + "--b1\r\nContent-Type: text/html; charset=\"UTF-8\"\r\nContent-Transfer-Encoding: base64\r\n\r\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes("<div>Hi Pat,</div>")) + "\r\n"
            + "--b1--\r\n";
        Assert.Equal(expected, mime);
        Assert.Throws<ArgumentException>(() => GmailDrafts.Mime(draft with { To = "pat@acme.com\r\nBcc: x@y.com" }, "b"));
        Assert.Throws<ArgumentException>(() => GmailDrafts.Mime(draft with { To = "a@b.com, c@d.com" }, "b"));
        Assert.Throws<ArgumentException>(() => GmailDrafts.Mime(draft with { To = "" }, "b"));
    }

    [Fact]
    public void Nothing_in_the_network_log_can_send()
    {
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Root(), "functions"), "*.cs", SearchOption.AllDirectories).Where(p => p.Contains("NetworkLog", StringComparison.Ordinal) && !p.Contains("jobhunt.Tests", StringComparison.Ordinal) && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            string code = File.ReadAllText(path);
            Assert.DoesNotContain("/send", code, StringComparison.Ordinal);
            Assert.DoesNotContain("IMailSender", code, StringComparison.Ordinal);
            Assert.DoesNotContain("calendar.readonly", code, StringComparison.Ordinal);
            Assert.DoesNotContain("googleapis.com/calendar", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_missing_table_is_recognised_and_anything_else_is_not()
    {
        Assert.Equal(208, SqlConversationStore.MissingObject);
        Assert.False(SqlConversationStore.IsMissingTable(new InvalidOperationException("208")));
    }

    [GeneratedRegex(@"^\s+\[(\w+)\]\s+(?:n?varchar|int|bigint|smallint|tinyint|bit|date|datetimeoffset|char)", RegexOptions.Multiline)]
    private static partial Regex Column();

    [GeneratedRegex(@"\b([cp])\.\[(\w+)\]")]
    private static partial Regex Qualified();

    [GeneratedRegex(@"(?:UPDATE|MERGE) \[network\]\.\[(\w+)\]")]
    private static partial Regex Target();

    [GeneratedRegex(@"\[(\w+)\]")]
    private static partial Regex Bracketed();
}
