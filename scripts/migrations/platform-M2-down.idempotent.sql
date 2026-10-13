BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013083152_M2_TenantMemberships'
)
BEGIN
    DROP TABLE [platform].[TenantMemberships];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013083152_M2_TenantMemberships'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013083152_M2_TenantMemberships';
END;
GO

COMMIT;
GO

