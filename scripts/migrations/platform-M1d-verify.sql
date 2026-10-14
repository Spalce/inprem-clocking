-- Verification for platform migration M1d_Organizations. READ-ONLY. Counts only.
-- After the up script: Applied = 1, Tables = 3, LastSignInColumn = 1, OrganizationsWithoutProfile = 0.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[TenantProfiles]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS Tables, 0 AS LastSignInColumn, NULL AS OrganizationsWithoutProfile, NULL AS Subscriptions;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261014210206_M1d_Organizations'') AS Applied,
    (SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE s.name = N''platform'' AND t.name IN (N''TenantProfiles'', N''TenantNotes'', N''Subscriptions'')) AS Tables,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N''[platform].[TenantMemberships]'') AND name = N''LastSignInUtc'') AS LastSignInColumn,
    (SELECT COUNT(*) FROM [dbo].[Tenants] t WHERE NOT EXISTS (SELECT 1 FROM [platform].[TenantProfiles] p WHERE p.TenantId = t.Id)) AS OrganizationsWithoutProfile,
    (SELECT COUNT(*) FROM [platform].[Subscriptions]) AS Subscriptions;';
GO
