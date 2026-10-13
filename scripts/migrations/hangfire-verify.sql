-- Verification for Hangfire job storage. READ-ONLY.
-- After the up script: SchemaVersion = 9, Tables = 11. Servers: job servers seen in the last 5
-- minutes (1 or more on the App site with Jobs:RunServer = true; the Admin site never adds one).
SET NOCOUNT ON;
IF OBJECT_ID(N'[hangfire].[Schema]') IS NULL
BEGIN
    SELECT NULL AS SchemaVersion, 0 AS Tables, NULL AS Servers;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT MAX([Version]) FROM [hangfire].[Schema]) AS SchemaVersion,
    (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N''hangfire'')) AS Tables,
    (SELECT COUNT(*) FROM [hangfire].[Server] WHERE [LastHeartbeat] > DATEADD(minute, -5, SYSUTCDATETIME())) AS Servers;';
GO
