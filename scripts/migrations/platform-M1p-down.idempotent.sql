BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[TenantMessagingSettings]') AND [c].[name] = N'AdminSummaryEnabled');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [platform].[TenantMessagingSettings] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [platform].[TenantMessagingSettings] DROP COLUMN [AdminSummaryEnabled];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[TenantMessagingSettings]') AND [c].[name] = N'LongBreakMinutes');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [platform].[TenantMessagingSettings] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [platform].[TenantMessagingSettings] DROP COLUMN [LongBreakMinutes];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[TenantMessagingSettings]') AND [c].[name] = N'MissedClockOutTime');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [platform].[TenantMessagingSettings] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [platform].[TenantMessagingSettings] DROP COLUMN [MissedClockOutTime];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[TenantMessagingSettings]') AND [c].[name] = N'StaffAlertsEnabled');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [platform].[TenantMessagingSettings] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [platform].[TenantMessagingSettings] DROP COLUMN [StaffAlertsEnabled];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020010938_M1p_StaffAlertSettings';
END;
GO

COMMIT;
GO

