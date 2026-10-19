BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019233541_M1n_SmsSignIn'
)
BEGIN
    ALTER TABLE [platform].[TenantMessagingSettings] ADD [SmsSignInRequired] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019233541_M1n_SmsSignIn'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019233541_M1n_SmsSignIn', N'8.0.32');
END;
GO

COMMIT;
GO

