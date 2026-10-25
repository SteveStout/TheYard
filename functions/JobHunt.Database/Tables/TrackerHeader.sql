-- The tracker file's frame (PROMPT-JOBHUNT-TRACKER-SQL, rung 1): its header record as written (the quoted Count
-- cell carries byte order marks inside its quotes), whether the file opens with a byte order mark and ends with a
-- line break, how many rows it holds, and the whole file's SHA-256, which is what the Merger compares before it writes.
CREATE TABLE [jobhunt].[TrackerHeader] (
    [Id]            int            NOT NULL,  -- always 1: one file
    [Record]        nvarchar(4000) NOT NULL,
    [Bom]           bit            NOT NULL,
    [EndsWithBreak] bit            NOT NULL,
    [Rows]          int            NOT NULL,
    [FileHash]      char(64)       NOT NULL,
    [WrittenAt]     datetimeoffset NOT NULL,
    [ParityAt]      datetimeoffset NULL,      -- the last daily parity check
    [ParitySame]    bit            NULL,      -- what it found: the file rebuilt from these rows is the CSV, byte for byte
    CONSTRAINT [PK_TrackerHeader] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_TrackerHeader_One] CHECK ([Id] = 1)
);
GO
