-- Verification for platform migration M1f_PayPalBilling. READ-ONLY. Counts only.
-- After the up script: Applied = 1, Tables = 3, ForeignKeys = 2, OrganizationsWithoutSubscription = 0.
-- FreeSubscriptions: organizations on the Free mode (D26: the house organization and those created before the console).
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[CheckoutSessions]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS Tables, 0 AS ForeignKeys, NULL AS OrganizationsWithoutSubscription, NULL AS FreeSubscriptions;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261015074938_M1f_PayPalBilling'') AS Applied,
    (SELECT COUNT(*) FROM sys.tables t WHERE SCHEMA_NAME(t.schema_id) = N''platform'' AND t.name IN (N''CheckoutSessions'', N''Payments'', N''BillingEvents'')) AS Tables,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N''FK_CheckoutSessions_Tenants_TenantId'', N''FK_Payments_Tenants_TenantId'')) AS ForeignKeys,
    (SELECT COUNT(*) FROM [dbo].[Tenants] t WHERE NOT EXISTS (SELECT 1 FROM [platform].[Subscriptions] s WHERE s.TenantId = t.Id)) AS OrganizationsWithoutSubscription,
    (SELECT COUNT(*) FROM [platform].[Subscriptions] WHERE Mode = N''Free'') AS FreeSubscriptions;';
GO
