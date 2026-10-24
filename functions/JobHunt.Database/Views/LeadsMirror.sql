-- The leads as the Sheet mirror will read them later (the review, section 4): no address, no bodies,
-- one row a lead with its company and its last send.
CREATE VIEW [campaign].[LeadsMirror]
AS
SELECT
    l.[Id] AS [LeadId], l.[Track], l.[Company], c.[Key], l.[RoleTitle], l.[ContactTitle], l.[ProofKey],
    l.[GhostScore], l.[Strength], l.[Blocked], l.[CreatedAt],
    (SELECT MAX(e.[SentAt]) FROM [campaign].[EmailsSent] e WHERE e.[LeadId] = l.[Id] AND e.[Shadow] = 0) AS [LastSentAt]
FROM [campaign].[Leads] l
JOIN [campaign].[Companies] c ON c.[Id] = l.[CompanyId];
GO
