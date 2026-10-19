-- The catalogue: all hundred thousand vehicles, the deterministic expansion of the
-- 200 in Vehicles, written once by the site's fill and read a page at a time. The
-- listing, the facets, a vehicle by id and the room's picks are queries on this
-- table; no process holds it in memory (ADR: The store does the work).
--
-- The dataset's columns are Vehicles' columns at Vehicles' widths. Four more are
-- written once at the fill and never change: the slot the id's FNV-1a hash gives the
-- window, as offsets from the day's UTC anchor; the price a buyer competes against on
-- the dataset's figures; and the searchable text a typed word is looked for in.
CREATE TABLE [dbo].[Catalogue] (
    [Id]                nvarchar(64)    NOT NULL,  -- the synthetic id, a seed id and six characters more
    [Seq]               int             NOT NULL,  -- the place in the expansion: the clustered key and every order's tie-break
    [Vin]               varchar(17)     NOT NULL,
    [Year]              int             NOT NULL,
    [Make]              nvarchar(64)    NOT NULL,
    [Model]             nvarchar(64)    NOT NULL,
    [Trim]              nvarchar(64)    NOT NULL,
    [BodyStyle]         nvarchar(32)    NOT NULL,
    [ExteriorColor]     nvarchar(32)    NOT NULL,
    [InteriorColor]     nvarchar(32)    NOT NULL,
    [Engine]            nvarchar(128)   NOT NULL,
    [Transmission]      nvarchar(64)    NOT NULL,
    [Drivetrain]        nvarchar(16)    NOT NULL,
    [OdometerKm]        int             NOT NULL,
    [FuelType]          nvarchar(32)    NOT NULL,
    [ConditionGrade]    decimal(3,1)    NOT NULL,
    [ConditionReport]   nvarchar(1024)  NOT NULL,
    [DamageNotes]       nvarchar(max)   NOT NULL,  -- a JSON array, read and written whole
    [TitleStatus]       nvarchar(32)    NOT NULL,
    [Province]          nvarchar(64)    NOT NULL,
    [City]              nvarchar(64)    NOT NULL,
    [AuctionStart]      datetime2(0)    NOT NULL,
    [StartingBid]       int             NOT NULL,
    [ReservePrice]      int             NULL,
    [BuyNowPrice]       int             NULL,
    [Images]            nvarchar(max)   NOT NULL,  -- a JSON array, for a body style the gallery has no pool for
    [SellingDealership] nvarchar(128)   NOT NULL,
    [Lot]               nvarchar(32)    NOT NULL,
    [CurrentBid]        int             NULL,
    [BidCount]          int             NOT NULL,
    [StartOffsetMs]     bigint          NOT NULL,  -- the window's start from the day's anchor; negative before it
    [EndOffsetMs]       bigint          NOT NULL,  -- the window's end from the day's anchor
    [Price]             int             NOT NULL,  -- CurrentBid when there is one, else StartingBid
    [SearchText]        nvarchar(400)   NOT NULL,  -- lowercased: year, make, model, trim, body style, title, province, city
    CONSTRAINT [PK_Catalogue] PRIMARY KEY NONCLUSTERED ([Id])
);
GO

-- Clustered on the expansion's place: the room's first live auctions are read in
-- this order, and every other index carries Seq as its row locator for free.
CREATE UNIQUE CLUSTERED INDEX [IX_Catalogue_Seq] ON [dbo].[Catalogue] ([Seq]);
GO

-- One index per order a page is read in, each ending in Seq so a tie reads the
-- same on every store, so a page is a seek and a scan that stops at its last row
-- rather than a sort of every match (sort only as far as the page, kept by the store).
-- The live segment, closest end first; the start rides along to test "started".
CREATE INDEX [IX_Catalogue_End] ON [dbo].[Catalogue] ([EndOffsetMs], [Seq], [StartOffsetMs]);
GO

-- The ended segment, most recent end first, ties in the expansion's order.
CREATE INDEX [IX_Catalogue_EndDescending] ON [dbo].[Catalogue] ([EndOffsetMs] DESC, [Seq]);
GO

-- The upcoming segment, soonest start first; the end rides along for the counts.
CREATE INDEX [IX_Catalogue_Start] ON [dbo].[Catalogue] ([StartOffsetMs], [Seq], [EndOffsetMs]);
GO

-- The price and grade orders carry both ends of the window, so a range's count reads the index alone.
CREATE INDEX [IX_Catalogue_Price] ON [dbo].[Catalogue] ([Price], [Seq], [StartOffsetMs], [EndOffsetMs]);
GO

CREATE INDEX [IX_Catalogue_PriceDescending] ON [dbo].[Catalogue] ([Price] DESC, [Seq], [StartOffsetMs], [EndOffsetMs]);
GO

CREATE INDEX [IX_Catalogue_Condition] ON [dbo].[Catalogue] ([ConditionGrade] DESC, [Seq], [StartOffsetMs], [EndOffsetMs]);
GO

CREATE INDEX [IX_Catalogue_Bids] ON [dbo].[Catalogue] ([BidCount] DESC, [Seq], [StartOffsetMs], [EndOffsetMs]);
GO

-- The counts: every column the three segment counts compare, so a count reads an index a few
-- dozen bytes a row wide instead of the clustered table at about two kilobytes a row. It leads
-- with the columns no page ranges over, so no page is planned off it and then sorted.
CREATE INDEX [IX_Catalogue_Counts] ON [dbo].[Catalogue] ([Province], [TitleStatus], [BodyStyle], [Make], [ConditionGrade], [Price], [StartOffsetMs], [EndOffsetMs]);
GO

-- The words: the searchable text with every column a typed word's count or page compares or
-- orders by, so a word is looked for in an index about a third of the row's width.
CREATE INDEX [IX_Catalogue_Words] ON [dbo].[Catalogue] ([SearchText], [StartOffsetMs], [EndOffsetMs], [Price], [ConditionGrade], [BidCount], [Seq]);
GO
