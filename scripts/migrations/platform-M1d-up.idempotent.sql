BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    ALTER TABLE [platform].[TenantMemberships] ADD [LastSignInUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    CREATE TABLE [platform].[Subscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] int NOT NULL,
        [PlanId] uniqueidentifier NULL,
        [PlanPriceId] uniqueidentifier NULL,
        [Mode] nvarchar(20) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [TrialEndsUtc] datetime2 NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Subscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Subscriptions_PlanPrices_PlanPriceId] FOREIGN KEY ([PlanPriceId]) REFERENCES [platform].[PlanPrices] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Subscriptions_Plans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [platform].[Plans] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    CREATE TABLE [platform].[TenantNotes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] int NOT NULL,
        [Text] nvarchar(2000) NOT NULL,
        [Author] nvarchar(300) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_TenantNotes] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    CREATE TABLE [platform].[TenantProfiles] (
        [TenantId] int NOT NULL,
        [Slug] varchar(50) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [StatusReason] nvarchar(500) NULL,
        [StatusChangedUtc] datetime2 NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_TenantProfiles] PRIMARY KEY ([TenantId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    CREATE INDEX [IX_Subscriptions_PlanId] ON [platform].[Subscriptions] ([PlanId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    CREATE INDEX [IX_Subscriptions_PlanPriceId] ON [platform].[Subscriptions] ([PlanPriceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Subscriptions_TenantId] ON [platform].[Subscriptions] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    CREATE INDEX [IX_TenantNotes_TenantId_CreatedUtc] ON [platform].[TenantNotes] ([TenantId], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TenantProfiles_Slug] ON [platform].[TenantProfiles] ([Slug]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN

    ALTER TABLE [platform].[TenantProfiles] ADD CONSTRAINT [FK_TenantProfiles_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
    ALTER TABLE [platform].[TenantNotes] ADD CONSTRAINT [FK_TenantNotes_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
    ALTER TABLE [platform].[Subscriptions] ADD CONSTRAINT [FK_Subscriptions_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN

    INSERT INTO [platform].[TenantProfiles] ([TenantId], [Slug], [Status], [StatusReason], [StatusChangedUtc], [CreatedUtc])
    SELECT t.[Id], CONCAT('org-', t.[Id]), CASE WHEN t.[IsActive] = 1 THEN N'Active' ELSE N'Suspended' END,
           CASE WHEN t.[IsActive] = 1 THEN NULL ELSE N'Inactive before the Provider Console (migration M1d)' END,
           NULL, t.[CreatedAt]
    FROM [dbo].[Tenants] t
    WHERE NOT EXISTS (SELECT 1 FROM [platform].[TenantProfiles] p WHERE p.[TenantId] = t.[Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014210206_M1d_Organizations', N'8.0.32');
END;
GO

COMMIT;
GO

