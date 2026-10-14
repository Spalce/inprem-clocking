BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014215921_M1e_SupportSessions'
)
BEGIN
    CREATE TABLE [platform].[SupportSessions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] int NOT NULL,
        [ProviderUserId] nvarchar(450) NOT NULL,
        [ProviderEmail] nvarchar(256) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [StartedUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [EndedUtc] datetime2 NULL,
        [EndedBy] nvarchar(300) NULL,
        [HandoffTokenHash] varchar(64) NOT NULL,
        [HandoffExpiresUtc] datetime2 NOT NULL,
        [HandoffUsedUtc] datetime2 NULL,
        CONSTRAINT [PK_SupportSessions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014215921_M1e_SupportSessions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SupportSessions_HandoffTokenHash] ON [platform].[SupportSessions] ([HandoffTokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014215921_M1e_SupportSessions'
)
BEGIN
    CREATE INDEX [IX_SupportSessions_TenantId_StartedUtc] ON [platform].[SupportSessions] ([TenantId], [StartedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014215921_M1e_SupportSessions'
)
BEGIN

    ALTER TABLE [platform].[SupportSessions] ADD CONSTRAINT [FK_SupportSessions_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014215921_M1e_SupportSessions'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014215921_M1e_SupportSessions', N'8.0.32');
END;
GO

COMMIT;
GO

