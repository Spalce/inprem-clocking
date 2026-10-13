-- 01 - Environment. READ-ONLY. Run against a RESTORED LOCAL COPY only (see README.md).
-- sqlcmd -S <local server> -d <restored db> -E -v ConfirmLocalCopy=YES -i 01-environment.sql
:on error exit
SET NOCOUNT ON;
IF '$(ConfirmLocalCopy)' <> 'YES'
BEGIN
    RAISERROR('Refusing to run: pass -v ConfirmLocalCopy=YES to confirm this is a restored local copy, never the hosted database.', 16, 1);
    SET NOEXEC ON;
END
GO

SELECT
    @@VERSION                                                        AS SqlServerVersion,
    CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50))           AS ProductVersion,
    CAST(SERVERPROPERTY('Edition') AS nvarchar(100))                 AS Edition,
    DB_NAME()                                                        AS DatabaseName,
    d.compatibility_level                                            AS CompatibilityLevel,
    d.recovery_model_desc                                            AS RecoveryModel,
    d.collation_name                                                 AS Collation,
    CAST((SELECT SUM(CAST(size AS bigint)) * 8 / 1024.0 FROM sys.database_files) AS decimal(12, 2)) AS SizeMB,
    IS_ROLEMEMBER('db_owner')                                        AS IsDbOwner,
    HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY SECURITY POLICY') AS CanAlterSecurityPolicy
FROM sys.databases d
WHERE d.name = DB_NAME();
GO
