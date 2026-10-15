BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015094743_M1g_CheckoutPlanChanges'
)
BEGIN
    DROP INDEX [IX_CheckoutSessions_ExternalSubscriptionId] ON [platform].[CheckoutSessions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015094743_M1g_CheckoutPlanChanges'
)
BEGIN
    CREATE INDEX [IX_CheckoutSessions_ExternalSubscriptionId] ON [platform].[CheckoutSessions] ([ExternalSubscriptionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015094743_M1g_CheckoutPlanChanges'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015094743_M1g_CheckoutPlanChanges', N'8.0.32');
END;
GO

COMMIT;
GO

