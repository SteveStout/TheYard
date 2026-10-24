-- Every real send without its bodies, for the Sheet mirror later and the tracker's Communication rows.
CREATE VIEW [campaign].[SendsMirror]
AS
SELECT
    e.[Id] AS [EmailId], e.[LeadId], e.[Company], e.[TemplateId], e.[TemplateVersion], e.[Subject],
    e.[SentAt], e.[CentralDay], e.[Touch], e.[RefCode], e.[Bounced], e.[ReplyReceivedAt]
FROM [campaign].[EmailsSent] e
WHERE e.[Shadow] = 0;
GO
