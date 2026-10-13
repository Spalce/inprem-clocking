BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013080438_M1a_DataProtectionKeys'
)
BEGIN
    DROP TABLE [platform].[DataProtectionKeys];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013080438_M1a_DataProtectionKeys'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013080438_M1a_DataProtectionKeys';
END;
GO

COMMIT;
GO

