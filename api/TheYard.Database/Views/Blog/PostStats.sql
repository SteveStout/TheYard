-- Each post's reads, from the read log and the event log, bots left out (ADR: Every LinkedIn post is
-- also a blog post). Readers are distinct day-hashes, so one person on two days counts twice, which is
-- the price of storing no lasting identity. Depth is the deepest quarter a reader reached, averaged;
-- time is the longest the page was open per reader, averaged, in seconds.
CREATE VIEW [blog].[PostStats]
AS
SELECT
    p.[Id] AS [PostId],
    p.[Title],
    p.[PublishedAt],
    (SELECT COUNT(*) FROM [blog].[PostView] v WHERE v.[PostId] = p.[Id] AND v.[IsBot] = 0) AS [Reads],
    (SELECT COUNT(DISTINCT v.[SessionHash]) FROM [blog].[PostView] v WHERE v.[PostId] = p.[Id] AND v.[IsBot] = 0) AS [Readers],
    (SELECT COUNT(*) FROM [blog].[PostView] v WHERE v.[PostId] = p.[Id] AND v.[IsBot] = 0 AND v.[ViewedAt] > DATEADD(day, -7, SYSDATETIMEOFFSET())) AS [Reads7d],
    (SELECT COUNT(*) FROM [blog].[PostView] v WHERE v.[PostId] = p.[Id] AND v.[IsBot] = 0 AND v.[RefCode] IS NOT NULL) AS [FromTaggedLinks],
    (SELECT MAX(v.[ViewedAt]) FROM [blog].[PostView] v WHERE v.[PostId] = p.[Id] AND v.[IsBot] = 0) AS [LastRead],
    (SELECT AVG(CAST(d.[Deepest] AS float)) FROM (
        SELECT MAX(TRY_CAST(e.[Value] AS int)) AS [Deepest] FROM [blog].[PostEvent] e
        WHERE e.[PostId] = p.[Id] AND e.[IsBot] = 0 AND e.[Type] = 'depth' GROUP BY e.[SessionHash]) d) AS [AverageDepth],
    (SELECT AVG(CAST(t.[Longest] AS float)) FROM (
        SELECT MAX(TRY_CAST(e.[Value] AS int)) AS [Longest] FROM [blog].[PostEvent] e
        WHERE e.[PostId] = p.[Id] AND e.[IsBot] = 0 AND e.[Type] = 'time' GROUP BY e.[SessionHash]) t) AS [AverageSeconds],
    (SELECT COUNT(DISTINCT e.[SessionHash]) FROM [blog].[PostEvent] e WHERE e.[PostId] = p.[Id] AND e.[IsBot] = 0 AND e.[Type] = 'click' AND e.[Value] = 'linkedin') AS [ToLinkedIn],
    (SELECT COUNT(DISTINCT e.[SessionHash]) FROM [blog].[PostEvent] e WHERE e.[PostId] = p.[Id] AND e.[IsBot] = 0 AND e.[Type] = 'click' AND e.[Value] = 'code') AS [ToCode]
FROM [blog].[Post] p
WHERE p.[IsPublished] = 1;
GO
