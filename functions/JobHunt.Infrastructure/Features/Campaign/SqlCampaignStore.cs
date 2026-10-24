using System.Data;
using Microsoft.Data.SqlClient;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// The campaign's three ports over sqldb-jobhunt-ss (ADR-118, Decision 2), with Microsoft.Data.SqlClient and the
/// Function's managed identity: the connection string carries `Authentication=Active Directory Managed
/// Identity;User Id=client id` and no secret, the way the site's does (ADR-039). Every statement is a
/// constant here with its parameters, and CampaignSqlTests holds every column a statement names to a column
/// the SQL project declares. A connection is opened per call from the pool and closed after.
/// </summary>
public sealed class SqlCampaignStore(string connectionString) : ICompanyBook, ILeadStore, IEmailLog
{
    internal const string FindCompany = """
        SELECT c.[Id], c.[Key], c.[ParentKey], c.[Names], c.[RemoteStance], c.[LastDotNetPostingAt], c.[LastDotNetPostingTitle], c.[DoNotEmail],
               (SELECT COUNT(*) FROM [campaign].[CompanyEvidence] v WHERE v.[CompanyId] = c.[Id]) AS [Evidence]
        FROM [campaign].[Companies] c WHERE c.[Key] = @key
        """;

    internal const string DoNotEmail = """
        UPDATE [campaign].[Companies] SET [DoNotEmail] = 1, [DoNotEmailAt] = @at, [UpdatedAt] = @at WHERE [Id] = @id AND [DoNotEmail] = 0
        """;

    internal const string OpenLeads = """
        SELECT l.[Id], l.[Track], l.[Company], l.[RoleTitle], l.[WhatTheyDo], l.[FirstName], l.[ContactName], l.[ContactTitle],
               l.[ContactEmail], l.[EmailSource], l.[MxChecked], l.[Verified], l.[AppliedAt], l.[LinkedInInviteSentAt], l.[LinkedInAcceptedAt],
               l.[ProofKey], l.[FollowUpPostSlug], l.[GhostScore], l.[BandTop], l.[FirstSeen], l.[StrengthStep], l.[RefCode],
               l.[ResumeLink], l.[SiteLink], l.[GitHubLink], l.[LinkedInLink], l.[AboutLink], l.[CreatedAt],
               c.[Id] AS [CompanyId], c.[Key], c.[ParentKey], c.[Names], c.[RemoteStance], c.[LastDotNetPostingAt], c.[LastDotNetPostingTitle], c.[DoNotEmail],
               (SELECT COUNT(*) FROM [campaign].[CompanyEvidence] v WHERE v.[CompanyId] = c.[Id]) AS [Evidence]
        FROM [campaign].[Leads] l JOIN [campaign].[Companies] c ON c.[Id] = l.[CompanyId]
        WHERE l.[Blocked] IS NULL
        ORDER BY l.[CreatedAt], l.[Id]
        """;

    internal const string Block = """
        UPDATE [campaign].[Leads] SET [Blocked] = @reason, [UpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = @id
        """;

    internal const string Unverify = """
        UPDATE [campaign].[Leads] SET [Verified] = 0, [VerifiedReason] = @reason, [UpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = @id
        """;

    internal const string Since = """
        SELECT e.[LeadId], e.[Key], e.[ParentKey], e.[Company], e.[CentralDay], e.[Shadow] FROM [campaign].[EmailsSent] e WHERE e.[CentralDay] >= @day
        """;

    internal const string Count = """
        SELECT COUNT(*) FROM [campaign].[EmailsSent] e WHERE e.[CentralDay] = @day AND e.[Shadow] = @shadow
        """;

    internal const string Write = """
        INSERT INTO [campaign].[EmailsSent] ([LeadId], [Key], [ParentKey], [Company], [To], [TemplateId], [TemplateVersion], [Subject], [BodyText], [BodyHtml],
            [SentAt], [CentralDay], [Touch], [Shadow], [RefCode], [GmailMessageId], [GmailThreadId], [RfcMessageId], [Bounced])
        OUTPUT INSERTED.[Id]
        VALUES (@lead, @key, @parent, @company, @to, @template, @version, @subject, @text, @html,
            @sent, @day, @touch, @shadow, @ref, @message, @thread, @rfc, 0)
        """;

    internal const string Bounce = """
        UPDATE [campaign].[EmailsSent] SET [Bounced] = 1, [BounceReason] = @reason OUTPUT INSERTED.[LeadId] WHERE [RfcMessageId] = @rfc AND [Shadow] = 0
        """;

    internal const string Reply = """
        UPDATE [campaign].[EmailsSent] SET [ReplyReceivedAt] = @at WHERE [GmailThreadId] = @thread AND [Shadow] = 0 AND [ReplyReceivedAt] IS NULL
        """;

    /// <summary>The names column's separator, the lead run writes it the same way.</summary>
    public const string NameSeparator = " | ";

    /// <inheritdoc />
    public async Task<CompanyRecord?> FindAsync(string key, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, FindCompany, ("@key", key));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? CompanyFrom(reader, "Id") : null;
    }

    /// <inheritdoc />
    public Task MarkDoNotEmailAsync(int companyId, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(DoNotEmail, ct, ("@id", companyId), ("@at", at));

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeadRecord>> OpenAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, OpenLeads);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var leads = new List<LeadRecord>();
        while (await reader.ReadAsync(ct))
        {
            leads.Add(LeadFrom(reader));
        }
        return leads;
    }

    /// <inheritdoc />
    public Task BlockAsync(long leadId, string reason, CancellationToken ct) => ExecuteAsync(Block, ct, ("@id", leadId), ("@reason", Clip(reason, 200)));

    /// <inheritdoc />
    public Task MarkUnverifiedAsync(long leadId, string reason, CancellationToken ct) => ExecuteAsync(Unverify, ct, ("@id", leadId), ("@reason", Clip(reason, 200)));

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoggedEmail>> SinceAsync(DateOnly centralDay, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, Since, ("@day", centralDay.ToDateTime(TimeOnly.MinValue)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var sends = new List<LoggedEmail>();
        while (await reader.ReadAsync(ct))
        {
            sends.Add(new LoggedEmail(
                reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
                DateOnly.FromDateTime(reader.GetDateTime(4)), reader.GetBoolean(5)));
        }
        return sends;
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(DateOnly centralDay, bool shadow, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, Count, ("@day", centralDay.ToDateTime(TimeOnly.MinValue)), ("@shadow", shadow));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<long> WriteAsync(EmailRecord email, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, Write,
            ("@lead", email.LeadId), ("@key", email.Key), ("@parent", email.ParentKey), ("@company", email.Company), ("@to", email.To),
            ("@template", email.TemplateId), ("@version", email.TemplateVersion), ("@subject", email.Subject),
            ("@text", email.BodyText), ("@html", email.BodyHtml), ("@sent", email.SentAt), ("@day", email.CentralDay.ToDateTime(TimeOnly.MinValue)),
            ("@touch", (byte)email.Touch), ("@shadow", email.Shadow), ("@ref", email.RefCode),
            ("@message", email.GmailMessageId), ("@thread", email.GmailThreadId), ("@rfc", email.RfcMessageId));
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<long?> MarkBouncedAsync(string rfcMessageId, string reason, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, Bounce, ("@rfc", rfcMessageId), ("@reason", Clip(reason, 400)));
        object? lead = await command.ExecuteScalarAsync(ct);
        return lead is null or DBNull ? null : Convert.ToInt64(lead, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public Task MarkReplyAsync(string gmailThreadId, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(Reply, ct, ("@thread", gmailThreadId), ("@at", at));

    private async Task<SqlConnection> ConnectAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal static SqlCommand Command(SqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection) { CommandType = CommandType.Text, CommandTimeout = 30 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..length];

    private static CompanyRecord CompanyFrom(SqlDataReader r, string idColumn) => new(
        r.GetInt32(r.GetOrdinal(idColumn)),
        r.GetString(r.GetOrdinal("Key")),
        Text(r, "ParentKey"),
        [.. r.GetString(r.GetOrdinal("Names")).Split(NameSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        r.GetString(r.GetOrdinal("RemoteStance")),
        r.IsDBNull(r.GetOrdinal("LastDotNetPostingAt")) ? null : DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("LastDotNetPostingAt"))),
        Text(r, "LastDotNetPostingTitle"),
        r.GetInt32(r.GetOrdinal("Evidence")),
        r.GetBoolean(r.GetOrdinal("DoNotEmail")));

    private static LeadRecord LeadFrom(SqlDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        Company = CompanyFrom(r, "CompanyId"),
        Track = r.GetString(r.GetOrdinal("Track"))[0],
        CompanyName = r.GetString(r.GetOrdinal("Company")),
        RoleTitle = Text(r, "RoleTitle"),
        WhatTheyDo = r.GetString(r.GetOrdinal("WhatTheyDo")),
        FirstName = r.GetString(r.GetOrdinal("FirstName")),
        ContactName = r.GetString(r.GetOrdinal("ContactName")),
        ContactTitle = r.GetString(r.GetOrdinal("ContactTitle")),
        ContactEmail = Text(r, "ContactEmail"),
        EmailSource = Text(r, "EmailSource"),
        MxChecked = Flag(r, "MxChecked"),
        Verified = Flag(r, "Verified"),
        AppliedAt = When(r, "AppliedAt"),
        LinkedInInviteSentAt = When(r, "LinkedInInviteSentAt"),
        LinkedInAcceptedAt = When(r, "LinkedInAcceptedAt"),
        ProofKey = Text(r, "ProofKey"),
        FollowUpPostSlug = Text(r, "FollowUpPostSlug"),
        GhostScore = r.IsDBNull(r.GetOrdinal("GhostScore")) ? null : r.GetByte(r.GetOrdinal("GhostScore")),
        BandTop = r.IsDBNull(r.GetOrdinal("BandTop")) ? null : r.GetInt32(r.GetOrdinal("BandTop")),
        FirstSeen = DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("FirstSeen"))),
        StrengthStep = r.GetInt16(r.GetOrdinal("StrengthStep")),
        RefCode = Text(r, "RefCode"),
        ResumeLink = Text(r, "ResumeLink"),
        SiteLink = Text(r, "SiteLink"),
        GitHubLink = Text(r, "GitHubLink"),
        LinkedInLink = Text(r, "LinkedInLink"),
        AboutLink = Text(r, "AboutLink"),
        CreatedAt = r.GetDateTimeOffset(r.GetOrdinal("CreatedAt")),
    };

    private static string? Text(SqlDataReader r, string column) => r.IsDBNull(r.GetOrdinal(column)) ? null : r.GetString(r.GetOrdinal(column));

    private static bool? Flag(SqlDataReader r, string column) => r.IsDBNull(r.GetOrdinal(column)) ? null : r.GetBoolean(r.GetOrdinal(column));

    private static DateTimeOffset? When(SqlDataReader r, string column) => r.IsDBNull(r.GetOrdinal(column)) ? null : r.GetDateTimeOffset(r.GetOrdinal(column));
}
