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
    /// <summary>The names column's separator, the lead run writes it the same way.</summary>
    public const string NameSeparator = " | ";

    /// <inheritdoc />
    public async Task<CompanyRecord?> FindAsync(string key, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, CampaignSql.FindCompany, ("@key", key));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? CompanyFrom(reader, "Id") : null;
    }

    /// <inheritdoc />
    public Task MarkDoNotEmailAsync(int companyId, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(CampaignSql.DoNotEmail, ct, ("@id", companyId), ("@at", at));

    /// <inheritdoc />
    public async Task<int> SaveAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, CampaignSql.SaveCompany,
            ("@key", company.Key), ("@parent", company.ParentKey), ("@names", Clip(string.Join(NameSeparator, company.Names), 1000)), ("@stance", company.RemoteStance),
            ("@lastAt", company.LastDotNetPostingAt?.ToDateTime(TimeOnly.MinValue)), ("@lastTitle", company.LastDotNetPostingTitle is { } t ? Clip(t, 200) : null),
            ("@board", company.BoardKind), ("@token", company.BoardToken), ("@at", at), ("@run", Clip(run, 64)));
        int id = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        foreach (var evidence in company.Evidence)
        {
            await using var add = Command(connection, CampaignSql.SaveEvidence, ("@company", id), ("@kind", evidence.Kind), ("@title", Clip(evidence.Title, 200)), ("@url", Clip(evidence.Url, 500)), ("@seen", evidence.SeenAt));
            await add.ExecuteNonQueryAsync(ct);
        }
        return id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BoardCompany>> BoardsAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, CampaignSql.Boards);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var boards = new List<BoardCompany>();
        while (await reader.ReadAsync(ct))
        {
            boards.Add(new BoardCompany(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        return boards;
    }

    /// <inheritdoc />
    public Task MarkScannedAsync(int companyId, DateOnly? lastDotNetPostingAt, string? lastDotNetPostingTitle, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(CampaignSql.Scanned, ct, ("@id", companyId), ("@lastAt", lastDotNetPostingAt?.ToDateTime(TimeOnly.MinValue)), ("@lastTitle", lastDotNetPostingTitle is { } t ? Clip(t, 200) : null), ("@at", at));

    /// <inheritdoc />
    public Task SaveAsync(int companyId, SpoolLead lead, bool? mx, int strength, string inputsHash, string? blocked, string run, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(CampaignSql.SaveLead, ct,
            ("@company", companyId), ("@contact", Clip(lead.ContactName, 120)), ("@track", lead.Track), ("@name", Clip(lead.Company, 200)), ("@role", lead.RoleTitle is { } r ? Clip(r, 200) : null),
            ("@posting", lead.PostingUrl is { } p ? Clip(p, 500) : null), ("@what", Clip(lead.WhatTheyDo, 110)), ("@first", Clip(lead.FirstName, 60)), ("@title", Clip(lead.ContactTitle, 120)),
            ("@email", lead.ContactEmail), ("@source", lead.EmailSource is { } s ? Clip(s, 500) : null), ("@mx", mx), ("@applied", lead.AppliedAt), ("@count", lead.AppliedCount),
            ("@invited", lead.LinkedInInviteSentAt), ("@accepted", lead.LinkedInAcceptedAt), ("@proof", lead.ProofKey), ("@slug", lead.FollowUpPostSlug),
            ("@ghost", lead.GhostScore is { } g ? (byte)g : null), ("@band", lead.BandTop), ("@day", DateOnly.FromDateTime(CentralClock.Local(at)).ToDateTime(TimeOnly.MinValue)),
            ("@strength", strength), ("@step", (short)lead.StrengthStep), ("@why", lead.StrengthWhy is { } w ? Clip(w, 400) : null), ("@hash", inputsHash), ("@at", at),
            ("@blocked", blocked is { } b ? Clip(b, 200) : null), ("@ref", lead.RefCode), ("@resume", lead.Links?.Resume), ("@site", lead.Links?.Site),
            ("@github", lead.Links?.GitHub), ("@linkedin", lead.Links?.LinkedIn), ("@about", lead.Links?.About), ("@run", Clip(run, 64)));

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeadRecord>> OpenAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, CampaignSql.OpenLeads);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var leads = new List<LeadRecord>();
        while (await reader.ReadAsync(ct))
        {
            leads.Add(LeadFrom(reader));
        }
        return leads;
    }

    /// <inheritdoc />
    public Task BlockAsync(long leadId, string reason, CancellationToken ct) => ExecuteAsync(CampaignSql.Block, ct, ("@id", leadId), ("@reason", Clip(reason, 200)));

    /// <inheritdoc />
    public Task MarkUnverifiedAsync(long leadId, string reason, CancellationToken ct) => ExecuteAsync(CampaignSql.Unverify, ct, ("@id", leadId), ("@reason", Clip(reason, 200)));

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoggedEmail>> SinceAsync(DateOnly centralDay, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, CampaignSql.Since, ("@day", centralDay.ToDateTime(TimeOnly.MinValue)));
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
        await using var command = Command(connection, CampaignSql.Count, ("@day", centralDay.ToDateTime(TimeOnly.MinValue)), ("@shadow", shadow));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<long> WriteAsync(EmailRecord email, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, CampaignSql.Write,
            ("@lead", email.LeadId), ("@key", email.Key), ("@parent", email.ParentKey), ("@company", email.Company), ("@to", email.To),
            ("@template", email.TemplateId), ("@version", email.TemplateVersion), ("@subject", email.Subject),
            ("@text", email.BodyText), ("@html", email.BodyHtml), ("@sent", email.SentAt), ("@day", email.CentralDay.ToDateTime(TimeOnly.MinValue)),
            ("@touch", (byte)email.Touch), ("@shadow", email.Shadow), ("@ref", email.RefCode),
            ("@message", email.GmailMessageId), ("@thread", email.GmailThreadId), ("@rfc", email.RfcMessageId));
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SentThread>> SentThreadsAsync(DateOnly centralDay, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = Command(connection, CampaignSql.Threads, ("@day", centralDay.ToDateTime(TimeOnly.MinValue)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var threads = new List<SentThread>();
        while (await reader.ReadAsync(ct))
        {
            threads.Add(new SentThread(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                DateOnly.FromDateTime(reader.GetDateTime(5)), reader.GetBoolean(6), reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7)));
        }
        return threads;
    }

    /// <inheritdoc />
    public Task MarkBouncedAsync(long emailId, string reason, CancellationToken ct) => ExecuteAsync(CampaignSql.Bounce, ct, ("@id", emailId), ("@reason", Clip(reason, 400)));

    /// <inheritdoc />
    public Task MarkReplyAsync(string gmailThreadId, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(CampaignSql.Reply, ct, ("@thread", gmailThreadId), ("@at", at));

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
