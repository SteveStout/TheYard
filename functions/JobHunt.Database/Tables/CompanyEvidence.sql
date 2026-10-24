-- Why a company counts as a .NET shop: one row a posting that asks for .NET. A posting that accepts .NET
-- as one language of many is not a row (the prompt's rung 5). Strength counts these, up to three.
CREATE TABLE [campaign].[CompanyEvidence] (
    [Id]        int            NOT NULL IDENTITY,
    [CompanyId] int            NOT NULL,
    [Kind]      nvarchar(16)   NOT NULL,  -- required: the posting's required stack; other: another engineering posting
    [Title]     nvarchar(200)  NOT NULL,
    [Url]       nvarchar(500)  NOT NULL,
    [SeenAt]    datetimeoffset NOT NULL,
    CONSTRAINT [PK_CompanyEvidence] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CompanyEvidence_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [campaign].[Companies] ([Id]),
    CONSTRAINT [CK_CompanyEvidence_Kind] CHECK ([Kind] IN ('required', 'other'))
);
GO
CREATE UNIQUE INDEX [IX_CompanyEvidence_Company_Url] ON [campaign].[CompanyEvidence] ([CompanyId], [Url]);
GO
