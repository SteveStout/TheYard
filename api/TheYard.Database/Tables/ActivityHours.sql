-- Requests per store per UTC hour, for the graph at the top of the Admin tab
-- (ADR: Site activity, and the line an address does not cross). A counter row:
-- a batch of hits written off the request path arrives as a delta and the
-- counts move by it, so two containers writing the same hour add rather than
-- overwrite. There is no column here an address, an account or an email could
-- go in, and that is the privacy rule written as a schema.
CREATE TABLE [dbo].[ActivityHours] (
    [Store]     nvarchar(16)   NOT NULL,  -- "sql" or "cosmos", the key the toggle uses
    [Hour]      datetime2      NOT NULL,  -- UTC, minutes and seconds zero
    [Requests]  int            NOT NULL,
    [Bots]      int            NOT NULL,  -- of those, the ones that looked like a scanner or a crawler
    [Paths]     nvarchar(4000) NOT NULL,  -- the paths served most this hour with their counts, JSON, top twenty
    CONSTRAINT [PK_ActivityHours] PRIMARY KEY ([Store], [Hour])
);
GO
