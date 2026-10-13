-- Verification for platform migration M1b_ProviderSecurity. READ-ONLY. Counts only (no emails, no hashes).
-- After the up script: Applied = 1, Tables = 6, AppendOnlyTrigger = 1, ForeignKeys = 2.
-- ActiveOwners: 0 until the first owner completes setup; 2 or more before production (D16).
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[ProviderUsers]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS Tables, 0 AS AppendOnlyTrigger, 0 AS ForeignKeys, NULL AS ActiveOwners, NULL AS OpenRecoveries;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261013185539_M1b_ProviderSecurity'') AS Applied,
    (SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE s.name = N''platform'' AND t.name IN (N''ProviderUsers'', N''ProviderInvitations'', N''RecoveryRecords'', N''AuditEntries'', N''UsedCodes'', N''SecurityAttempts'')) AS Tables,
    (SELECT COUNT(*) FROM sys.triggers WHERE name = N''TR_AuditEntries_AppendOnly'' AND is_disabled = 0) AS AppendOnlyTrigger,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N''FK_ProviderUsers_AspNetUsers_UserId'', N''FK_ProviderInvitations_AspNetUsers_UserId'')) AS ForeignKeys,
    (SELECT COUNT(*) FROM [platform].[ProviderUsers] WHERE Role = N''Owner'' AND Status = N''Active'') AS ActiveOwners,
    (SELECT COUNT(*) FROM [platform].[RecoveryRecords] WHERE Status IN (N''PendingApproval'', N''Approved'')) AS OpenRecoveries;';
GO
