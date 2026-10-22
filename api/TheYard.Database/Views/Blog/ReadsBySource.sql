-- Where each post's reads came from (linkedin, ref, search, direct), with each source's share of the
-- post's reads, bots left out.
CREATE VIEW [blog].[ReadsBySource]
AS
SELECT
    v.[PostId],
    v.[Source],
    COUNT(*) AS [Reads],
    CAST(COUNT(*) AS float) / NULLIF(SUM(COUNT(*)) OVER (PARTITION BY v.[PostId]), 0) AS [Share]
FROM [blog].[PostView] v
WHERE v.[IsBot] = 0
GROUP BY v.[PostId], v.[Source];
GO
