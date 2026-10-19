BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019233541_M1n_SmsSignIn'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[TenantMessagingSettings]') AND [c].[name] = N'SmsSignInRequired');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [platform].[TenantMessagingSettings] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [platform].[TenantMessagingSettings] DROP COLUMN [SmsSignInRequired];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019233541_M1n_SmsSignIn'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019233541_M1n_SmsSignIn';
END;
GO

COMMIT;
GO

