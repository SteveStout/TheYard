namespace JobHunt.Infrastructure.Features.Ats;

/// <summary>The reply pass's statements over jobhunt.ReplyDrafts, every column held to the SQL project by ReplyDraftRowsTests; the last is its only write.</summary>
internal static class ReplyDraftSql
{
    internal const string Ready = """
        SELECT CASE WHEN OBJECT_ID(N'[jobhunt].[ReplyDrafts]') IS NOT NULL THEN 1 ELSE 0 END
        """;

    internal const string Find = """
        SELECT r.[MessageId], r.[ThreadId], r.[Status], r.[Reason], r.[DraftId] FROM [jobhunt].[ReplyDrafts] r
        WHERE r.[MessageId] IN (SELECT [value] FROM STRING_SPLIT(@ids, ','))
        """;

    internal const string Upsert = """
        UPDATE r SET r.[ThreadId] = COALESCE(@thread, r.[ThreadId]), r.[Status] = @status, r.[Reason] = @reason,
            r.[DraftId] = COALESCE(@draft, r.[DraftId]), r.[UpdatedAt] = @at
        FROM [jobhunt].[ReplyDrafts] r WITH (UPDLOCK, SERIALIZABLE) WHERE r.[MessageId] = @id;
        IF @@ROWCOUNT = 0
            INSERT INTO [jobhunt].[ReplyDrafts] ([MessageId], [ThreadId], [Status], [Reason], [DraftId], [CreatedAt], [UpdatedAt])
            VALUES (@id, @thread, @status, @reason, @draft, @at, @at);
        """;
}
