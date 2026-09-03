-- One buyer's standing on one vehicle, and the only table here that changes
-- after startup. One row per buyer per vehicle: a later bid from the same person
-- replaces their earlier one, because what the application asks is "where do I
-- stand", never "what did I bid an hour ago" (ADR: Accounts and per-user bids).
CREATE TABLE [dbo].[Bids] (
    [UserId]     nvarchar(128)  NOT NULL,  -- matches AspNetUsers.Id, which a foreign key has to
    [VehicleId]  nvarchar(64)   NOT NULL,  -- the synthetic id a visitor actually bid on, so the same width as Vehicles.Id
    [Amount]     int            NOT NULL,
    [BidCount]   int            NOT NULL,
    [WonBuyNow]  bit            NOT NULL,
    [AtMs]       bigint         NOT NULL,  -- when it was placed, in the milliseconds every other timestamp on this wire uses
    -- The optimistic concurrency token. Two containers, or two requests that get
    -- past one container's lock, can both read this row and both decide to write
    -- it. rowversion is maintained by the database, so nothing in the
    -- application can forget to move it, and the second writer fails instead of
    -- silently overwriting the first. A lost update on an auction is somebody's
    -- money.
    [RowVersion] rowversion     NULL,
    CONSTRAINT [PK_Bids] PRIMARY KEY ([UserId], [VehicleId]),
    -- Deleting an account takes its bids with it. Before this constraint existed
    -- a bid whose account had been deleted stayed here forever, was loaded into
    -- BidService at every startup, and counted toward a vehicle's standing price
    -- on behalf of nobody.
    CONSTRAINT [FK_Bids_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

-- There is deliberately no foreign key from VehicleId to Vehicles, and no index
-- on UserId alone.
--
-- The foreign key would be a lie: Vehicles holds the 200-row seed, a visitor
-- bids on the 100,000 rows expanded from it in memory, and the constraint would
-- reject 99.8 per cent of legitimate bids. It becomes correct the day the
-- expansion is persisted.
--
-- The index would be a second copy of the first half of the primary key, whose
-- leading column is already UserId: a write on every bid, and nothing earned.
