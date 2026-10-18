-- One vehicle's standing across everybody: the price, the count, the high bidder
-- and whether it has sold. The two sites run against this one database, so the
-- standing lives here rather than in either container's memory, and every bid
-- writes it with a compare-and-set on Version: an update that names the version
-- it read and changes no row lost the race, and the bid path reads again and
-- runs the rules again (ADR: Three readers with no memory of the project, item 5).
CREATE TABLE [dbo].[Standings] (
    [VehicleId]    nvarchar(64)   NOT NULL,  -- the same width as Bids.VehicleId
    [Amount]       int            NOT NULL,
    [BidCount]     int            NOT NULL,
    [HighBidderId] nvarchar(128)  NOT NULL,  -- the same width as AspNetUsers.Id
    [SoldBuyNow]   bit            NOT NULL,
    [AtMs]         bigint         NOT NULL,
    -- A plain number the application moves by one on every write, rather than
    -- rowversion, so the compare-and-set reads the same here and on SQLite.
    [Version]      bigint         NOT NULL,
    CONSTRAINT [PK_Standings] PRIMARY KEY ([VehicleId])
);
GO

-- No foreign key on HighBidderId. Deleting an account cascades its rows out of
-- Bids, and a standing whose rows are gone is worked out again from whoever is
-- left the next time anybody bids, so the standing is never the only record of
-- a bid and a cascade here would delete a price other buyers are measured against.
