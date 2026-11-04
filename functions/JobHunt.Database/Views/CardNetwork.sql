-- The network card (ADR: The network log drafts, Steve sends): conversations started in each of the last five ISO
-- weeks, and the rows by state. Counts only: no name, company, address, subject or code leaves this view.
CREATE VIEW [card].[Network]
AS
SELECT
    'started' AS [Kind],
    DATEADD(day, -((DATEPART(weekday, CAST(c.[FirstTwoWayAt] AT TIME ZONE 'Central Standard Time' AS date)) + @@DATEFIRST + 5) % 7), CAST(c.[FirstTwoWayAt] AT TIME ZONE 'Central Standard Time' AS date)) AS [Week],
    COUNT(*) AS [Rows]
FROM [network].[Conversations] c
WHERE c.[FirstTwoWayAt] >= DATEADD(day, -35, SYSDATETIMEOFFSET())
GROUP BY DATEADD(day, -((DATEPART(weekday, CAST(c.[FirstTwoWayAt] AT TIME ZONE 'Central Standard Time' AS date)) + @@DATEFIRST + 5) % 7), CAST(c.[FirstTwoWayAt] AT TIME ZONE 'Central Standard Time' AS date))
UNION ALL
SELECT
    c.[State] AS [Kind],
    NULL AS [Week],
    COUNT(*) AS [Rows]
FROM [network].[Conversations] c
GROUP BY c.[State];
