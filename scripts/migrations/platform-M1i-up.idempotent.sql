BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015194018_M1i_Signups'
)
BEGIN
    CREATE TABLE [platform].[Signups] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] int NOT NULL,
        [OwnerUserId] nvarchar(450) NOT NULL,
        [Mode] nvarchar(20) NOT NULL,
        [PlanPriceId] uniqueidentifier NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [VerifiedUtc] datetime2 NULL,
        [TermsAcceptedUtc] datetime2 NOT NULL,
        [SmsOptIn] bit NOT NULL,
        [ResendWindowStartUtc] datetime2 NULL,
        [ResendCount] int NOT NULL,
        CONSTRAINT [PK_Signups] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Signups_PlanPrices_PlanPriceId] FOREIGN KEY ([PlanPriceId]) REFERENCES [platform].[PlanPrices] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015194018_M1i_Signups'
)
BEGIN
    CREATE INDEX [IX_Signups_PlanPriceId] ON [platform].[Signups] ([PlanPriceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015194018_M1i_Signups'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Signups_TenantId] ON [platform].[Signups] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015194018_M1i_Signups'
)
BEGIN

    ALTER TABLE [platform].[Signups] ADD CONSTRAINT [FK_Signups_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015194018_M1i_Signups'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015194018_M1i_Signups', N'8.0.32');
END;
GO

COMMIT;
GO

