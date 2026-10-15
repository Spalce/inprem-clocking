BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015094743_M1g_CheckoutPlanChanges'
)
BEGIN
    DROP INDEX [IX_CheckoutSessions_ExternalSubscriptionId] ON [platform].[CheckoutSessions];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015094743_M1g_CheckoutPlanChanges'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CheckoutSessions_ExternalSubscriptionId] ON [platform].[CheckoutSessions] ([ExternalSubscriptionId]) WHERE [ExternalSubscriptionId] IS NOT NULL');
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015094743_M1g_CheckoutPlanChanges'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015094743_M1g_CheckoutPlanChanges';
END;
GO

COMMIT;
GO

