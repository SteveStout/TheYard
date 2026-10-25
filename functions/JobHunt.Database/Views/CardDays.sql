-- One row a Central day for the last 60 days: sent, written in shadow, bounced, answered.
CREATE VIEW [card].[Days]
AS
SELECT
    e.[CentralDay],
    SUM(CASE WHEN e.[Shadow] = 0 THEN 1 ELSE 0 END) AS [Sent],
    SUM(CASE WHEN e.[Shadow] = 1 THEN 1 ELSE 0 END) AS [Shadow],
    SUM(CASE WHEN e.[Bounced] = 1 THEN 1 ELSE 0 END) AS [Bounced],
    SUM(CASE WHEN e.[ReplyReceivedAt] IS NOT NULL THEN 1 ELSE 0 END) AS [Replies]
FROM [campaign].[EmailsSent] e
WHERE e.[CentralDay] >= DATEADD(day, -60, CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Central Standard Time' AS date))
GROUP BY e.[CentralDay];
GO
