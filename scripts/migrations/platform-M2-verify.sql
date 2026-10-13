-- Verification for platform migration M2_TenantMemberships. READ-ONLY. Counts and ids only.
-- After the up script: Applied = 1, AccountsWithoutMembership = 0.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[TenantMemberships]') IS NULL
BEGIN
    SELECT 0 AS Applied, NULL AS Memberships, NULL AS AccountsWithoutMembership, NULL AS OrganizationsWithoutOwner;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261013083152_M2_TenantMemberships'') AS Applied,
    (SELECT COUNT(*) FROM [platform].[TenantMemberships]) AS Memberships,
    (SELECT COUNT(*) FROM [dbo].[AspNetUsers] u WHERE u.TenantId IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM [platform].[TenantMemberships] m WHERE m.UserId = u.Id AND m.TenantId = u.TenantId)) AS AccountsWithoutMembership,
    (SELECT COUNT(DISTINCT m.TenantId) FROM [platform].[TenantMemberships] m
        WHERE NOT EXISTS (SELECT 1 FROM [platform].[TenantMemberships] o WHERE o.TenantId = m.TenantId AND o.Role = N''Owner'' AND o.Status = N''Active'')) AS OrganizationsWithoutOwner;

-- Organizations whose Owner still has to be chosen (several admins; see migration M2).
SELECT m.TenantId, COUNT(*) AS ActiveAdmins
FROM [platform].[TenantMemberships] m
WHERE m.Status = N''Active''
  AND NOT EXISTS (SELECT 1 FROM [platform].[TenantMemberships] o WHERE o.TenantId = m.TenantId AND o.Role = N''Owner'' AND o.Status = N''Active'')
GROUP BY m.TenantId
ORDER BY m.TenantId;';
GO
