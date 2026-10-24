-- One row a Central day: sent, written in shadow, bounced and answered. Counts only; the card reads these.
CREATE VIEW [campaign].[DailyCounts]
AS
SELECT
    e.[CentralDay],
    SUM(CASE WHEN e.[Shadow] = 0 THEN 1 ELSE 0 END) AS [Sent],
    SUM(CASE WHEN e.[Shadow] = 1 THEN 1 ELSE 0 END) AS [Shadow],
    SUM(CASE WHEN e.[Bounced] = 1 THEN 1 ELSE 0 END) AS [Bounced],
    SUM(CASE WHEN e.[ReplyReceivedAt] IS NOT NULL THEN 1 ELSE 0 END) AS [Replies]
FROM [campaign].[EmailsSent] e
GROUP BY e.[CentralDay];
GO
