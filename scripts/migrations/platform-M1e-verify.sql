-- Verification for platform migration M1e_SupportSessions. READ-ONLY. Counts only.
-- After the up script: Applied = 1, Table = 1, ForeignKey = 1. OpenSessions: sessions not ended and not past their time.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[SupportSessions]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS [Table], 0 AS ForeignKey, NULL AS OpenSessions;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261014215921_M1e_SupportSessions'') AS Applied,
    1 AS [Table],
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N''FK_SupportSessions_Tenants_TenantId'') AS ForeignKey,
    (SELECT COUNT(*) FROM [platform].[SupportSessions] WHERE EndedUtc IS NULL AND ExpiresUtc > SYSUTCDATETIME()) AS OpenSessions;';
GO
