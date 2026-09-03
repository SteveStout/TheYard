-- Identity's role table. This application creates no roles, and the table is
-- here because IdentityDbContext expects it and the foreign keys below it point
-- at it. An empty table with a reason beats a missing table with a surprise.
CREATE TABLE [dbo].[AspNetRoles] (
    [Id]               nvarchar(128) NOT NULL,
    [Name]             nvarchar(256) NULL,
    [NormalizedName]   nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);
GO

CREATE UNIQUE INDEX [RoleNameIndex] ON [dbo].[AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;
GO
