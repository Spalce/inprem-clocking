BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    DROP TABLE [platform].[Subscriptions];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    DROP TABLE [platform].[TenantNotes];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    DROP TABLE [platform].[TenantProfiles];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[TenantMemberships]') AND [c].[name] = N'LastSignInUtc');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [platform].[TenantMemberships] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [platform].[TenantMemberships] DROP COLUMN [LastSignInUtc];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014210206_M1d_Organizations';
END;
GO

COMMIT;
GO

