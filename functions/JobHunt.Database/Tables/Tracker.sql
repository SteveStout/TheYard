-- The tracker's rows in SQL beside Job_Hunt_Tracker.csv, which stays the master (ADR: The tracker merger runs in
-- Azure). One row a record of the CSV, the seven columns as
-- text exactly as the file holds them (no date parsing, no trimming, no length limit), the record as the file holds it, and its
-- SHA-256, so the file can be rebuilt byte for byte and a changed row is found by its hash.
CREATE TABLE [jobhunt].[Tracker] (
    [Position]      int            NOT NULL,  -- the record's place in the file, 1 for the first row under the header
    [Count]         nvarchar(max)  NOT NULL,  -- as written: "3135", "NA" or anything else the cell holds
    [Person]        nvarchar(max)  NOT NULL,
    [Company]       nvarchar(max)  NOT NULL,
    [Communication] nvarchar(max)  NOT NULL,
    [Date]          nvarchar(max)  NOT NULL,  -- M/d/yyyy and yyyy-MM-dd both, as written
    [PhoneEmail]    nvarchar(max)  NOT NULL,
    [Notes]         nvarchar(max)  NOT NULL,
    [Record]        nvarchar(max)  NOT NULL,  -- the record's text, its quotes and inner line breaks as written
    [RecordHash]    char(64)       NOT NULL,  -- lower-case hex SHA-256 of the record's UTF-8 bytes
    [WrittenAt]     datetimeoffset NOT NULL,
    CONSTRAINT [PK_Tracker] PRIMARY KEY ([Position])
);
GO
