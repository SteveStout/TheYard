-- The seed catalogue: 200 vehicles, read whole and in order, once, at startup.
-- SyntheticVehicleSource expands these to 100,000 in memory, so this table is
-- the seed and not the inventory (ADR: The SQL Server backend).
--
-- Every length here was chosen from what the dataset actually holds, with
-- headroom. The longest observed value in the seed is in brackets beside each
-- one. Without a length every column would be nvarchar(max), which cannot be
-- indexed, is stored off-row past 8,000 bytes, and tells a reader nothing about
-- what belongs in it.
CREATE TABLE [dbo].[Vehicles] (
    [Id]                nvarchar(64)    NOT NULL,  -- 36 today; a bid references the synthetic id, which adds six
    [Seq]               int             NOT NULL,  -- where this row sat in the seed file; a table has no order of its own
    [Vin]               varchar(17)     NOT NULL,  -- 17 characters from a defined alphabet, by ISO 3779, so varchar and not nvarchar
    [Year]              int             NOT NULL,
    [Make]              nvarchar(64)    NOT NULL,  -- [10]
    [Model]             nvarchar(64)    NOT NULL,  -- [14]
    [Trim]              nvarchar(64)    NOT NULL,  -- [19]
    [BodyStyle]         nvarchar(32)    NOT NULL,  -- [9]
    [ExteriorColor]     nvarchar(32)    NOT NULL,  -- [16]
    [InteriorColor]     nvarchar(32)    NOT NULL,  -- [12]
    [Engine]            nvarchar(128)   NOT NULL,  -- [25]
    [Transmission]      nvarchar(64)    NOT NULL,  -- [12]
    [Drivetrain]        nvarchar(16)    NOT NULL,  -- [3]
    [OdometerKm]        int             NOT NULL,
    [FuelType]          nvarchar(32)    NOT NULL,  -- [8]
    [ConditionGrade]    decimal(3,1)    NOT NULL,  -- 1.0 to 5.0 to one decimal: compared and displayed, never accumulated, so exact and not float
    [ConditionReport]   nvarchar(1024)  NOT NULL,  -- [143]
    [DamageNotes]       nvarchar(max)   NOT NULL,  -- a JSON array: read and written whole, never queried into
    [TitleStatus]       nvarchar(32)    NOT NULL,  -- [7]
    [Province]          nvarchar(64)    NOT NULL,  -- [16]
    [City]              nvarchar(64)    NOT NULL,  -- [11]
    [AuctionStart]      datetime2(0)    NOT NULL,  -- a local wall-clock instant to the second, with no zone
    [StartingBid]       int             NOT NULL,
    [ReservePrice]      int             NULL,      -- null means no reserve
    [BuyNowPrice]       int             NULL,      -- null means no buy-now
    [Images]            nvarchar(max)   NOT NULL,  -- a JSON array, same reasoning as DamageNotes
    [SellingDealership] nvarchar(128)   NOT NULL,  -- [24]
    [Lot]               nvarchar(32)    NOT NULL,  -- [6]
    [CurrentBid]        int             NULL,      -- null until the first bid
    [BidCount]          int             NOT NULL,
    CONSTRAINT [PK_Vehicles] PRIMARY KEY NONCLUSTERED ([Id])
);
GO

-- The clustered index is the table's physical order, and the only query this
-- table serves is ORDER BY Seq, run once at startup. Putting it on Seq rather
-- than leaving it on the primary key is the difference between reading the rows
-- in order and sorting them every boot.
CREATE UNIQUE CLUSTERED INDEX [IX_Vehicles_Seq] ON [dbo].[Vehicles] ([Seq]);
GO
