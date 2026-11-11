-- One row for every new posting the pounce judges, passed or not (pounce1110 step 2b): the instant it went live (the
-- board's when it gives one no later than the first sighting, else the sighting, LiveKnown 0), when it was seen and
-- judged, the first gate it failed, and for a role that passed its first touch, the earliest tracker row or campaign
-- email that reached a leader at the company. No person's name is kept. Written by the Function only.
CREATE TABLE [jobhunt].[PounceRoles] (
    [PostingKey]   bigint         NOT NULL,  -- jobhunt.Postings.Id
    [Company]      nvarchar(200)  NOT NULL,
    [LiveAt]       datetimeoffset NOT NULL,
    [LiveKnown]    bit            NOT NULL,  -- 1 when the board gave the instant
    [FirstSeenAt]  datetimeoffset NOT NULL,
    [JudgedAt]     datetimeoffset NOT NULL,
    [Passed]       bit            NOT NULL,
    [Failed]       nvarchar(300)  NULL,      -- the first reason it failed, in the picker's words
    [BandFlag]     nvarchar(8)    NOT NULL,  -- PayFloor: PASS, FLAG, SKIP, NONE
    [BudgetFlag]   bit            NOT NULL,  -- went live in the last 21 days of a year not yet ended
    [FirstTouchAt] datetimeoffset NULL,
    [TouchChannel] nvarchar(16)   NULL,      -- invites, inmails, campaign, messages, emails
    CONSTRAINT [PK_PounceRoles] PRIMARY KEY ([PostingKey])
);
GO
CREATE INDEX [IX_PounceRoles_Open] ON [jobhunt].[PounceRoles] ([LiveAt]) INCLUDE ([Company]) WHERE [Passed] = 1 AND [FirstTouchAt] IS NULL;
GO
