BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    DROP TABLE [platform].[ConsentRecords];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    DROP TABLE [platform].[Notifications];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    DROP TABLE [platform].[NotificationTemplates];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    DROP TABLE [platform].[TenantMessagingSettings];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019224539_M1m_Notifications';
END;
GO

COMMIT;
GO

