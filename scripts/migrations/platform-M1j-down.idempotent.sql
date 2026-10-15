BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015203907_M1j_BillingAccessCodes'
)
BEGIN
    DROP TABLE [platform].[BillingAccessCodes];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015203907_M1j_BillingAccessCodes'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015203907_M1j_BillingAccessCodes';
END;
GO

COMMIT;
GO

