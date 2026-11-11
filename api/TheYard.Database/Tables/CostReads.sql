-- The last read of Azure's bill, whole, in one row (ADR: What Azure charges, the
-- addendum on keeping the read). A read replaces the last, so a roll keeps the bill
-- and the slot that did not read answers from the one that did. The days and the
-- forecast are the cost port's own records as JSON: a resource's name and type, a
-- day and a cost, never a resource path, which would carry the subscription's id.
CREATE TABLE [dbo].[CostReads] (
    [Id]        int             NOT NULL,  -- always 1
    [ReadAt]    datetimeoffset  NOT NULL,  -- when Cost Management answered
    [Days]      nvarchar(max)   NOT NULL,  -- the charges by resource by day, JSON
    [Forecast]  nvarchar(max)   NOT NULL,  -- the forecast days, JSON
    CONSTRAINT [PK_CostReads] PRIMARY KEY ([Id])
);
GO
