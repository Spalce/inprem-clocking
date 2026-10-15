BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015170549_M5_LegacyBillingMoved'
)
BEGIN
    EXEC(N'
    UPDATE ps SET ps.[Mode] = N''Free'', ps.[CurrentPeriodStartUtc] = NULL, ps.[CurrentPeriodEndUtc] = NULL
    FROM [platform].[Subscriptions] ps
    WHERE ps.[Mode] = N''Invoiced'' AND ps.[PlanId] IS NULL AND ps.[ExternalSubscriptionId] IS NULL
      AND EXISTS (SELECT 1 FROM [platform].[TenantNotes] n WHERE n.[TenantId] = ps.[TenantId] AND n.[Author] = N''migration:M5'');
    DELETE FROM [platform].[TenantNotes] WHERE [Author] = N''migration:M5'';
    DELETE FROM [platform].[Payments] WHERE [RecordedBy] = N''migration:M5'';');
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015170549_M5_LegacyBillingMoved'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015170549_M5_LegacyBillingMoved';
END;
GO

COMMIT;
GO

