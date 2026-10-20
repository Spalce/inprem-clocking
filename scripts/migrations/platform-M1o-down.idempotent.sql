BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020001530_M1o_ProviderAlertPreferences'
)
BEGIN
    DROP TABLE [platform].[ProviderAlertPreferences];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020001530_M1o_ProviderAlertPreferences'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020001530_M1o_ProviderAlertPreferences';
END;
GO

COMMIT;
GO

