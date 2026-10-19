-- Verification for platform migration M1m_Notifications. READ-ONLY. Counts only.
-- After the up script: Applied = 1, Tables = 4, ForeignKeys = 2, UniqueIndexes = 2.
-- Queued: messages waiting to be sent; OptOuts: STOP replies recorded.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[Notifications]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS Tables, 0 AS ForeignKeys, 0 AS UniqueIndexes, NULL AS Queued, NULL AS OptOuts;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261019224539_M1m_Notifications'') AS Applied,
    (SELECT COUNT(*) FROM sys.tables WHERE SCHEMA_NAME(schema_id) = N''platform''
        AND name IN (N''Notifications'', N''ConsentRecords'', N''NotificationTemplates'', N''TenantMessagingSettings'')) AS Tables,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE SCHEMA_NAME(schema_id) = N''platform''
        AND name IN (N''FK_Notifications_Tenants_TenantId'', N''FK_TenantMessagingSettings_Tenants_TenantId'')) AS ForeignKeys,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N''[platform].[Notifications]'') AND is_unique = 1
        AND name IN (N''IX_Notifications_EventKey_RecipientRef_Channel'', N''IX_Notifications_ExternalId'')) AS UniqueIndexes,
    (SELECT COUNT(*) FROM [platform].[Notifications] WHERE [Status] = N''Queued'') AS Queued,
    (SELECT COUNT(*) FROM [platform].[ConsentRecords] WHERE [Action] = N''Revoke'') AS OptOuts;';
GO
