-- One row: where the leads stand. Open is not blocked and not yet emailed; with an address has one with its
-- public page; MX none is an address whose domain takes no mail; stopped is a company that asked not to be written to.
CREATE VIEW [card].[Leads]
AS
SELECT
    (SELECT COUNT(*) FROM [campaign].[Leads] l WHERE l.[Blocked] IS NULL
        AND NOT EXISTS (SELECT 1 FROM [campaign].[EmailsSent] e WHERE e.[LeadId] = l.[Id] AND e.[Shadow] = 0)) AS [Open],
    (SELECT COUNT(*) FROM [campaign].[Leads] l WHERE l.[Blocked] IS NULL AND l.[ContactEmail] IS NOT NULL AND l.[EmailSource] IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM [campaign].[EmailsSent] e WHERE e.[LeadId] = l.[Id] AND e.[Shadow] = 0)) AS [WithAddress],
    (SELECT COUNT(*) FROM [campaign].[Leads] l WHERE l.[Blocked] IS NULL AND l.[ContactEmail] IS NULL) AS [WaitingOnAddress],
    (SELECT COUNT(*) FROM [campaign].[Leads] l WHERE l.[Blocked] IS NOT NULL) AS [Blocked],
    (SELECT COUNT(*) FROM [campaign].[Leads] l WHERE l.[MxChecked] = 0) AS [NoMx],
    (SELECT COUNT(*) FROM [campaign].[Leads] l WHERE l.[Verified] = 0) AS [Unverified],
    (SELECT COUNT(*) FROM [campaign].[Companies] c) AS [Companies],
    (SELECT COUNT(*) FROM [campaign].[Companies] c WHERE c.[DoNotEmail] = 1) AS [Stopped],
    (SELECT COUNT(*) FROM [campaign].[Companies] c WHERE c.[BoardKind] IS NOT NULL) AS [Boards],
    (SELECT MAX(c.[ScannedAt]) FROM [campaign].[Companies] c) AS [LastScanAt],
    (SELECT MIN(e.[SentAt]) FROM [campaign].[EmailsSent] e) AS [FirstWrittenAt],
    (SELECT MIN(e.[SentAt]) FROM [campaign].[EmailsSent] e WHERE e.[Shadow] = 0) AS [FirstSentAt],
    (SELECT MAX(e.[SentAt]) FROM [campaign].[EmailsSent] e WHERE e.[Bounced] = 1) AS [LastBounceAt];
GO
