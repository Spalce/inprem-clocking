-- Verification for platform migration M1a_DataProtectionKeys. READ-ONLY.
-- After the up script: Applied = 1, TableExists = 1. After the down script: Applied = 0, TableExists = 0.
SET NOCOUNT ON;
DECLARE @Applied int = 0, @TableExists bit = 0, @KeyRows int = NULL;
IF OBJECT_ID(N'[platform].[__EFMigrationsHistory]') IS NOT NULL
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261013080438_M1a_DataProtectionKeys''',
        N'@n int OUTPUT', @n = @Applied OUTPUT;
IF OBJECT_ID(N'[platform].[DataProtectionKeys]') IS NOT NULL
BEGIN
    SET @TableExists = 1;
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[DataProtectionKeys]', N'@n int OUTPUT', @n = @KeyRows OUTPUT;
END
SELECT @Applied AS Applied, @TableExists AS TableExists, @KeyRows AS KeyRows;
GO
