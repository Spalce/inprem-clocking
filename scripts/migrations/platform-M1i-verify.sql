-- Verification for platform migration M1i_Signups. READ-ONLY. Counts only.
-- After the up script: Applied = 1, Table = 1, ForeignKey = 1. Pending / Verified: website signups so far.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[Signups]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS [Table], 0 AS ForeignKey, NULL AS Pending, NULL AS Verified;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261015194018_M1i_Signups'') AS Applied,
    1 AS [Table],
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N''FK_Signups_Tenants_TenantId'') AS ForeignKey,
    (SELECT COUNT(*) FROM [platform].[Signups] WHERE Status = N''PendingVerification'') AS Pending,
    (SELECT COUNT(*) FROM [platform].[Signups] WHERE Status = N''Verified'') AS Verified;';
GO
