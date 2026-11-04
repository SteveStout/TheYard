-- One row a Central day: the pass's mode, when it began and finished, and its counts. A day counts as run only
-- when its row has FinishedAt; a row begun two hours ago with none is a crashed pass the next hour retries.
CREATE TABLE [network].[Passes] (
    [CentralDay]      date           NOT NULL,
    [Mode]            nvarchar(8)    NOT NULL,
    [StartedAt]       datetimeoffset NOT NULL,
    [FinishedAt]      datetimeoffset NULL,
    [ThreadsRead]     int            NOT NULL,
    [Upserted]        int            NOT NULL,
    [Stopped]         int            NOT NULL,
    [Quiet]           int            NOT NULL,
    [DraftsBuilt]     int            NOT NULL,
    [StartedThisWeek] int            NOT NULL,
    [Seconds]         int            NOT NULL,
    [Line]            nvarchar(1000) NULL,
    CONSTRAINT [PK_Passes] PRIMARY KEY ([CentralDay]),
    CONSTRAINT [CK_Passes_Mode] CHECK ([Mode] IN ('shadow', 'on'))
);
