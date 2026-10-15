BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015203907_M1j_BillingAccessCodes'
)
BEGIN
    CREATE TABLE [platform].[BillingAccessCodes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] int NOT NULL,
        [CodeHash] varchar(64) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [UsedUtc] datetime2 NULL,
        CONSTRAINT [PK_BillingAccessCodes] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015203907_M1j_BillingAccessCodes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BillingAccessCodes_CodeHash] ON [platform].[BillingAccessCodes] ([CodeHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015203907_M1j_BillingAccessCodes'
)
BEGIN
    CREATE INDEX [IX_BillingAccessCodes_TenantId_CreatedUtc] ON [platform].[BillingAccessCodes] ([TenantId], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015203907_M1j_BillingAccessCodes'
)
BEGIN

    ALTER TABLE [platform].[BillingAccessCodes] ADD CONSTRAINT [FK_BillingAccessCodes_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015203907_M1j_BillingAccessCodes'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015203907_M1j_BillingAccessCodes', N'8.0.32');
END;
GO

COMMIT;
GO

