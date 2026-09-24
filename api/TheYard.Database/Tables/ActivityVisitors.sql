-- One visitor token on one store on one UTC day (ADR: Site activity, and the
-- line an address does not cross). The token is a keyed hash of the address
-- that rotates with the day, so one address is one row within a day and cannot
-- be joined across days or back to a person; the network is the address cut to
-- its first three octets. A full address is never written here, and the
-- endpoint that reads this table answers nobody without the operator's key.
CREATE TABLE [dbo].[ActivityVisitors] (
    [Store]      nvarchar(16)   NOT NULL,
    [Day]        nvarchar(10)   NOT NULL,  -- yyyy-MM-dd, UTC, the salt of the token beside it
    [Visitor]    nvarchar(32)   NOT NULL,  -- the first 16 bytes of the keyed hash as hex
    [Network]    nvarchar(40)   NOT NULL,  -- 203.0.113.x: three octets and an x, never four
    [FirstSeen]  datetime2      NOT NULL,
    [LastSeen]   datetime2      NOT NULL,
    [Requests]   int            NOT NULL,
    [Bots]       int            NOT NULL,
    [Paths]      nvarchar(4000) NOT NULL,  -- the paths this visitor asked for most, JSON, top twenty
    [Sources]    nvarchar(4000) NULL,      -- the hosts that linked here on a page load, JSON, top twenty (1.0.3.17)
    CONSTRAINT [PK_ActivityVisitors] PRIMARY KEY ([Store], [Day], [Visitor])
);
GO
