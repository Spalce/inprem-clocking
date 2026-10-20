-- Verification for platform migration M1o_ProviderAlertPreferences. READ-ONLY. Counts only.
-- After the up script: Applied = 1, TableExists = 1. Chosen: console users who changed their alert choices.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[ProviderAlertPreferences]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS TableExists, NULL AS Chosen;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261020001530_M1o_ProviderAlertPreferences'') AS Applied,
    1 AS TableExists,
    (SELECT COUNT(*) FROM [platform].[ProviderAlertPreferences]) AS Chosen;';
GO
