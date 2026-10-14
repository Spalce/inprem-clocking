-- Verification for platform migration M1c_Plans. READ-ONLY. Counts only.
-- After the up script: Applied = 1, Tables = 2, CodeIndex = 1. Plans / ActivePrices: as created in the console.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[Plans]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS Tables, 0 AS CodeIndex, NULL AS Plans, NULL AS ActivePrices;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261014204227_M1c_Plans'') AS Applied,
    (SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = N''platform'' AND t.name IN (N''Plans'', N''PlanPrices'')) AS Tables,
    (SELECT COUNT(*) FROM sys.indexes WHERE name = N''IX_Plans_Code'' AND is_unique = 1) AS CodeIndex,
    (SELECT COUNT(*) FROM [platform].[Plans]) AS Plans,
    (SELECT COUNT(*) FROM [platform].[PlanPrices] WHERE Status = N''Active'') AS ActivePrices;';
GO
