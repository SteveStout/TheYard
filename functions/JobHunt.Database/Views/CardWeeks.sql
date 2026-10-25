-- The Sunday table (the review, 8.2 item 6): the last four weeks of real sends by template, weekday of send,
-- track and proof key, with the replies each drew. Counts only.
CREATE VIEW [card].[Weeks]
AS
SELECT
    e.[TemplateId] + ' ' + e.[TemplateVersion] AS [Template],
    DATENAME(weekday, e.[CentralDay]) AS [Weekday],
    l.[Track],
    COALESCE(l.[ProofKey], 'ai') AS [ProofKey],
    COUNT(*) AS [Sent],
    SUM(CASE WHEN e.[ReplyReceivedAt] IS NOT NULL THEN 1 ELSE 0 END) AS [Replies]
FROM [campaign].[EmailsSent] e
JOIN [campaign].[Leads] l ON l.[Id] = e.[LeadId]
WHERE e.[Shadow] = 0 AND e.[CentralDay] >= DATEADD(day, -28, CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Central Standard Time' AS date))
GROUP BY e.[TemplateId], e.[TemplateVersion], DATENAME(weekday, e.[CentralDay]), l.[Track], COALESCE(l.[ProofKey], 'ai');
GO
