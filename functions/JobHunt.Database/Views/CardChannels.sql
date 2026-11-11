-- The funnel by channel (ADR: The Nightly Function on the Admin tab, the addendum on the funnel by channel): for each
-- channel and each week, Monday first, the touches, the replies from people and the rooms booked, from the rows
-- jobhunt.ChannelRows attributes. Channels: pounce, hotmgr, email campaign, old contacts, applications, recruiters.
-- Counts only: no person, company, subject or code leaves this view.
CREATE VIEW [card].[Channels]
AS
SELECT
    c.[Channel],
    DATEADD(day, -((DATEPART(weekday, c.[Day]) + @@DATEFIRST + 5) % 7), c.[Day]) AS [Week],
    SUM(CASE WHEN c.[Kind] = 'touch' THEN 1 ELSE 0 END) AS [Touches],
    SUM(CASE WHEN c.[Kind] = 'reply' THEN 1 ELSE 0 END) AS [Replies],
    SUM(CASE WHEN c.[Kind] = 'room' THEN 1 ELSE 0 END) AS [Rooms]
FROM [jobhunt].[ChannelRows] c
GROUP BY c.[Channel], DATEADD(day, -((DATEPART(weekday, c.[Day]) + @@DATEFIRST + 5) % 7), c.[Day]);
GO
