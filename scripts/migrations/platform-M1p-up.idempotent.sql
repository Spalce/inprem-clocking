BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings'
)
BEGIN
    ALTER TABLE [platform].[TenantMessagingSettings] ADD [AdminSummaryEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings'
)
BEGIN
    ALTER TABLE [platform].[TenantMessagingSettings] ADD [LongBreakMinutes] int NOT NULL DEFAULT 60;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings'
)
BEGIN
    ALTER TABLE [platform].[TenantMessagingSettings] ADD [MissedClockOutTime] time NOT NULL DEFAULT '21:00:00';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings'
)
BEGIN
    ALTER TABLE [platform].[TenantMessagingSettings] ADD [StaffAlertsEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020010938_M1p_StaffAlertSettings', N'8.0.32');
END;
GO

COMMIT;
GO

