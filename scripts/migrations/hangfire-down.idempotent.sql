-- Removes Hangfire job storage (schema [hangfire]) - reverses hangfire-up.idempotent.sql.
-- Every job, its history and the recurring-job definitions are deleted with it. Stop the App
-- site's job server (Jobs:RunServer = false, or stop the site) before running this.
-- Idempotent: does nothing when the schema is absent.
SET NOCOUNT ON;
IF SCHEMA_ID(N'hangfire') IS NULL RETURN;

DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER TABLE [hangfire].' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';' + CHAR(10)
FROM sys.foreign_keys fk WHERE fk.schema_id = SCHEMA_ID(N'hangfire');
SELECT @sql += N'DROP TABLE [hangfire].' + QUOTENAME(t.name) + N';' + CHAR(10)
FROM sys.tables t WHERE t.schema_id = SCHEMA_ID(N'hangfire');
EXEC sp_executesql @sql;
EXEC(N'DROP SCHEMA [hangfire]');
