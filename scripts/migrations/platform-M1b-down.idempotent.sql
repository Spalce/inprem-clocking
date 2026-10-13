BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    DROP TABLE [platform].[AuditEntries];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    DROP TABLE [platform].[ProviderInvitations];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    DROP TABLE [platform].[ProviderUsers];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    DROP TABLE [platform].[RecoveryRecords];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    DROP TABLE [platform].[SecurityAttempts];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    DROP TABLE [platform].[UsedCodes];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013185539_M1b_ProviderSecurity';
END;
GO

COMMIT;
GO

