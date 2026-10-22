-- How far each post's readers went: opened, read past half, finished, clicked out. Each step counts
-- distinct day-hashes, bots left out, so the steps can only narrow.
CREATE VIEW [blog].[Funnel]
AS
SELECT
    p.[Id] AS [PostId],
    (SELECT COUNT(DISTINCT v.[SessionHash]) FROM [blog].[PostView] v WHERE v.[PostId] = p.[Id] AND v.[IsBot] = 0) AS [Opened],
    (SELECT COUNT(DISTINCT e.[SessionHash]) FROM [blog].[PostEvent] e WHERE e.[PostId] = p.[Id] AND e.[IsBot] = 0 AND e.[Type] = 'depth' AND TRY_CAST(e.[Value] AS int) >= 50) AS [PastHalf],
    (SELECT COUNT(DISTINCT e.[SessionHash]) FROM [blog].[PostEvent] e WHERE e.[PostId] = p.[Id] AND e.[IsBot] = 0 AND e.[Type] = 'depth' AND TRY_CAST(e.[Value] AS int) >= 100) AS [Finished],
    (SELECT COUNT(DISTINCT e.[SessionHash]) FROM [blog].[PostEvent] e WHERE e.[PostId] = p.[Id] AND e.[IsBot] = 0 AND e.[Type] = 'click') AS [ClickedOut]
FROM [blog].[Post] p
WHERE p.[IsPublished] = 1;
GO
