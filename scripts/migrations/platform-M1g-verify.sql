-- Verification for platform migration M1g_CheckoutPlanChanges. READ-ONLY. Counts only.
-- After the up script: Applied = 1, IndexUnique = 0. Before it (or after down): IndexUnique = 1.
-- SharedSubscriptionIds: PayPal subscriptions with more than one checkout (plan changes); down cannot run while it is above 0.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[CheckoutSessions]') IS NULL
BEGIN
    SELECT 0 AS Applied, NULL AS IndexUnique, NULL AS SharedSubscriptionIds;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261015094743_M1g_CheckoutPlanChanges'') AS Applied,
    (SELECT CAST(is_unique AS int) FROM sys.indexes WHERE name = N''IX_CheckoutSessions_ExternalSubscriptionId'' AND object_id = OBJECT_ID(N''[platform].[CheckoutSessions]'')) AS IndexUnique,
    (SELECT COUNT(*) FROM (SELECT ExternalSubscriptionId FROM [platform].[CheckoutSessions] WHERE ExternalSubscriptionId IS NOT NULL GROUP BY ExternalSubscriptionId HAVING COUNT(*) > 1) d) AS SharedSubscriptionIds;';
GO
