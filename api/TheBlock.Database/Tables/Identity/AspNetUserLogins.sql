-- Identity's external logins. This application issues its own session token and
-- uses no external provider, so this table stays empty for the same reason
-- AspNetRoles does.
CREATE TABLE [dbo].[AspNetUserLogins] (
    [LoginProvider]       nvarchar(128) NOT NULL,
    [ProviderKey]         nvarchar(128) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId]              nvarchar(128) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [dbo].[AspNetUserLogins] ([UserId]);
GO
