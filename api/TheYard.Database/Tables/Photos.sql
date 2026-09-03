-- The vendored stock-photo manifest: 50 rows, read whole and in order, once, at
-- startup. There is no relationship between this table and Vehicles, and that
-- is not an omission: PhotoGallery picks a vehicle's photos by hashing its id
-- against the pool for its body style, so the association is computed and never
-- stored (ADR: The SQL Server backend).
CREATE TABLE [dbo].[Photos] (
    [File]  nvarchar(128)   NOT NULL,  -- the file name, which is already unique, so it is the key
    [Seq]   int             NOT NULL,
    [Style] nvarchar(32)    NOT NULL,  -- the body-style pool this photo belongs to
    [Title] nvarchar(256)   NOT NULL,  -- the source title, which reveals the make pictured
    CONSTRAINT [PK_Photos] PRIMARY KEY NONCLUSTERED ([File])
);
GO

-- Read in order, same as the catalogue, so clustered on the same kind of column.
CREATE UNIQUE CLUSTERED INDEX [IX_Photos_Seq] ON [dbo].[Photos] ([Seq]);
GO
