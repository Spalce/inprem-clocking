-- 03 - Row counts for every table. READ-ONLY. Run against a RESTORED LOCAL COPY only (see README.md).
:on error exit
SET NOCOUNT ON;
IF '$(ConfirmLocalCopy)' <> 'YES'
BEGIN
    RAISERROR('Refusing to run: pass -v ConfirmLocalCopy=YES to confirm this is a restored local copy, never the hosted database.', 16, 1);
    SET NOEXEC ON;
END
GO

SELECT
    s.name          AS SchemaName,
    t.name          AS TableName,
    SUM(p.row_count) AS [RowCount]
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.dm_db_partition_stats p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
GROUP BY s.name, t.name
ORDER BY s.name, t.name;
GO
