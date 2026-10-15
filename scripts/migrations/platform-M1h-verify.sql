-- Verification for platform migration M1h_PublicApi. READ-ONLY. Counts only.
-- After the up script: Applied = 1, Tables = 2, TokenHashIndexUnique = 1. ActiveTokens / IdempotencyRecords: as used by the website API.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[ApiTokens]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS Tables, NULL AS TokenHashIndexUnique, NULL AS ActiveTokens, NULL AS IdempotencyRecords;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261015175927_M1h_PublicApi'') AS Applied,
    (SELECT COUNT(*) FROM sys.tables t WHERE SCHEMA_NAME(t.schema_id) = N''platform'' AND t.name IN (N''ApiTokens'', N''IdempotencyRecords'')) AS Tables,
    (SELECT CAST(is_unique AS int) FROM sys.indexes WHERE name = N''IX_ApiTokens_TokenHash'' AND object_id = OBJECT_ID(N''[platform].[ApiTokens]'')) AS TokenHashIndexUnique,
    (SELECT COUNT(*) FROM [platform].[ApiTokens] WHERE RevokedUtc IS NULL AND ExpiresUtc > SYSUTCDATETIME()) AS ActiveTokens,
    (SELECT COUNT(*) FROM [platform].[IdempotencyRecords]) AS IdempotencyRecords;';
GO
