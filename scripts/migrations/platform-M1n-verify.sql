-- Verification for platform migration M1n_SmsSignIn. READ-ONLY. Counts only.
-- After the up script: Applied = 1, ColumnExists = 1. Required: organizations whose admins enter a texted code.
SET NOCOUNT ON;
IF COL_LENGTH(N'[platform].[TenantMessagingSettings]', N'SmsSignInRequired') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS ColumnExists, NULL AS Required;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261019233541_M1n_SmsSignIn'') AS Applied,
    1 AS ColumnExists,
    (SELECT COUNT(*) FROM [platform].[TenantMessagingSettings] WHERE [SmsSignInRequired] = 1) AS Required;';
GO
