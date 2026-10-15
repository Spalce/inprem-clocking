-- Verification for platform migration M1j_BillingAccessCodes. READ-ONLY. Counts only.
-- After the up script: Applied = 1, Table = 1, ForeignKey = 1. Unused: codes not used and not expired.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[BillingAccessCodes]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS [Table], 0 AS ForeignKey, NULL AS Unused;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261015203907_M1j_BillingAccessCodes'') AS Applied,
    1 AS [Table],
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N''FK_BillingAccessCodes_Tenants_TenantId'') AS ForeignKey,
    (SELECT COUNT(*) FROM [platform].[BillingAccessCodes] WHERE UsedUtc IS NULL AND ExpiresUtc > SYSUTCDATETIME()) AS Unused;';
GO
