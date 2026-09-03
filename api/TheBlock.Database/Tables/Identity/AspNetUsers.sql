-- ASP.NET Core Identity's user table, plus the one column this application adds
-- (ADR: Accounts and per-user bids). The shape is Identity's, not this
-- application's: these column names and widths are what IdentityDbContext
-- expects to find, so they are transcribed rather than designed.
--
-- The key is nvarchar(128) and not Identity's default nvarchar(450), because 450
-- is what makes a composite key too wide for SQL Server. A clustered index key is
-- capped at 900 bytes; nvarchar(450) is 900 on its own, so PK_AspNetUserTokens at
-- Identity's default is 2,700 bytes and PK_Bids is 1,028. The first publish of
-- this schema said so out loud, four times, and every one of those tables would
-- have failed an insert on a long enough value. The ids this application creates
-- are GUIDs, so 128 is generous.
--
-- The nvarchar(max) columns are deliberate and are the only unbounded columns in
-- this database. PasswordHash and the two stamps belong to a framework whose
-- hashing algorithm can change under this application, they hold a handful of
-- rows, and bounding them would trade nothing measurable for a truncation that
-- would appear on the day somebody upgraded.
CREATE TABLE [dbo].[AspNetUsers] (
    [Id]                   nvarchar(128)     NOT NULL,
    [CreatedAtMs]          bigint            NOT NULL,  -- this application's one addition: when the account was created
    [UserName]             nvarchar(256)     NULL,
    [NormalizedUserName]   nvarchar(256)     NULL,
    [Email]                nvarchar(256)     NULL,
    [NormalizedEmail]      nvarchar(256)     NULL,
    [EmailConfirmed]       bit               NOT NULL,
    [PasswordHash]         nvarchar(max)     NULL,
    [SecurityStamp]        nvarchar(max)     NULL,
    [ConcurrencyStamp]     nvarchar(max)     NULL,
    [PhoneNumber]          nvarchar(max)     NULL,
    [PhoneNumberConfirmed] bit               NOT NULL,
    [TwoFactorEnabled]     bit               NOT NULL,
    [LockoutEnd]           datetimeoffset    NULL,
    [LockoutEnabled]       bit               NOT NULL,
    [AccessFailedCount]    int               NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);
GO

-- Identity looks accounts up by the normalized forms, never by the display ones.
CREATE INDEX [EmailIndex] ON [dbo].[AspNetUsers] ([NormalizedEmail]);
GO

-- Filtered, because a null user name is not a duplicate of another null one.
CREATE UNIQUE INDEX [UserNameIndex] ON [dbo].[AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;
GO
