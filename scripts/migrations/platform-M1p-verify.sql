-- Verification for platform migration M1p_StaffAlertSettings. READ-ONLY. Counts only.
-- After the up script: Applied = 1, Columns = 4, BadDefaults = 0 (rows without 21:00 / 60 that never chose). On: organizations with staff alerts switched on.
SET NOCOUNT ON;
IF COL_LENGTH(N'[platform].[TenantMessagingSettings]', N'StaffAlertsEnabled') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS Columns, NULL AS BadDefaults, NULL AS [On];
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261020010938_M1p_StaffAlertSettings'') AS Applied,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N''[platform].[TenantMessagingSettings]'')
        AND name IN (N''StaffAlertsEnabled'', N''MissedClockOutTime'', N''LongBreakMinutes'', N''AdminSummaryEnabled'')) AS Columns,
    (SELECT COUNT(*) FROM [platform].[TenantMessagingSettings] WHERE [StaffAlertsEnabled] = 0 AND ([LongBreakMinutes] < 10 OR [MissedClockOutTime] = ''00:00'')) AS BadDefaults,
    (SELECT COUNT(*) FROM [platform].[TenantMessagingSettings] WHERE [StaffAlertsEnabled] = 1) AS [On];';
GO
