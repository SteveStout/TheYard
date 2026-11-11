-- One row for each touch, reply and room of the last nine weeks, with its channel and its Central day: the rows the
-- card's funnel by channel counts (card.Channels). A tracker row is a touch when it records a send or an application,
-- a reply when it opens INBOUND, and a room when it is an interview or a booked call. Its channel, first match wins:
-- pounce when the pounce's first touch for the same company fell on that day, then the campaign's email, an old
-- contact or a referral, a recruiter, an application, and any other LinkedIn send to a leader (the hot-manager
-- outreach). A reply or a room counts for the channel of the newest touch at the same company on or before its day.
-- The company is read here only to join a reply to its touch; it leaves this view as nothing but the channel.
CREATE VIEW [jobhunt].[ChannelRows]
AS
WITH [Read] AS (
    SELECT t.[Position], t.[Company], UPPER(t.[Person]) AS [Who], UPPER(t.[Communication]) AS [Via], UPPER(t.[Notes]) AS [Note],
        COALESCE(TRY_CONVERT(date, t.[Date], 23), TRY_CONVERT(date, t.[Date], 101)) AS [Day]
    FROM [jobhunt].[Tracker] t
),
[Kinds] AS (
    SELECT r.[Position], r.[Company], r.[Day],
        CASE
            WHEN r.[Note] LIKE 'INBOUND%' THEN 'reply'
            WHEN r.[Via] IN ('INTERVIEW', 'VIDEO INTERVIEW', 'SCREENING CALL', 'PHONE') OR r.[Note] LIKE 'INTERVIEW%'
                OR r.[Note] LIKE '%CALL BOOKED%' OR r.[Note] LIKE '%INTERVIEW BOOKED%' OR r.[Note] LIKE '%INTERVIEW SCHEDULED%' THEN 'room'
            WHEN r.[Note] LIKE '%NOT SENT%' OR r.[Note] LIKE '%NOT SUBMITTED%' OR r.[Note] LIKE 'REPLY%' THEN NULL
            WHEN r.[Note] LIKE '%APPLICATION SUBMITTED%' OR r.[Note] LIKE 'SUBMITTED%' OR r.[Note] LIKE '%EASY APPLY%' OR r.[Note] LIKE 'APPLIED %' THEN 'application'
            WHEN r.[Note] LIKE '%SENT%' THEN 'sent'
        END AS [Kind],
        CASE
            WHEN EXISTS (SELECT 1 FROM [jobhunt].[PounceRoles] p WHERE p.[Company] = r.[Company]
                AND CAST(p.[FirstTouchAt] AT TIME ZONE 'Central Standard Time' AS date) = r.[Day]) THEN 'pounce'
            WHEN r.[Note] LIKE '%CAMPAIGN EMAIL SENT%' THEN 'email campaign'
            WHEN r.[Note] LIKE '%OLD CONTACT%' OR r.[Note] LIKE '%OLD-CONTACT%' OR r.[Note] LIKE '%FORMER COLLEAGUE%' OR r.[Note] LIKE '%REFERRAL%' THEN 'old contacts'
            WHEN r.[Who] LIKE '%RECRUIT%' OR r.[Who] LIKE '%TALENT%' OR r.[Note] LIKE '%RECRUITER%' THEN 'recruiters'
            WHEN r.[Via] LIKE 'LINKEDIN%' THEN 'hotmgr'
        END AS [Channel]
    FROM [Read] r
    WHERE r.[Day] >= DATEADD(day, -63, CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Central Standard Time' AS date))
),
[Touches] AS (
    SELECT k.[Position], k.[Company], k.[Day],
        CASE WHEN k.[Kind] = 'application' AND k.[Channel] <> 'pounce' THEN 'applications' ELSE k.[Channel] END AS [Channel]
    FROM [Kinds] k
    WHERE k.[Kind] = 'application' OR (k.[Kind] = 'sent' AND k.[Channel] IS NOT NULL)
)
SELECT h.[Channel], h.[Day], 'touch' AS [Kind] FROM [Touches] h
UNION ALL
SELECT n.[Channel], k.[Day], k.[Kind]
FROM [Kinds] k
CROSS APPLY (
    SELECT TOP 1 x.[Channel] FROM [Touches] x WHERE x.[Company] = k.[Company] AND x.[Day] <= k.[Day] ORDER BY x.[Day] DESC, x.[Position] DESC
) n
WHERE k.[Kind] IN ('reply', 'room');
GO
