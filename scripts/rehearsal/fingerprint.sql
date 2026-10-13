-- Data fingerprint for migration rehearsals (design 11.2 step 5 / 11.4 verify). READ-ONLY.
-- Row counts of the approved app's tables and the hours totals behind the reports, per
-- organization. Run before and after a migration (and after its rollback): the output must be
-- identical. Platform and Hangfire tables are listed separately (they are expected to change).
SET NOCOUNT ON;

SELECT N'rows' AS Kind, t.name AS Item, SUM(p.rows) AS Value
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE SCHEMA_NAME(t.schema_id) = N'dbo' AND t.name <> N'__EFMigrationsHistory'
GROUP BY t.name

UNION ALL
SELECT N'staff seconds', CONCAT(N'tenant ', c.TenantId), SUM(CAST(DATEDIFF(second, CAST('00:00' AS time), c.WorkingHours) AS bigint))
FROM [dbo].[ClockingsStaff] c GROUP BY c.TenantId

UNION ALL
SELECT N'volunteer seconds', CONCAT(N'tenant ', c.TenantId), SUM(CAST(DATEDIFF(second, CAST('00:00' AS time), c.WorkingHours) AS bigint))
FROM [dbo].[Clockings] c GROUP BY c.TenantId

UNION ALL
SELECT N'staff checksum', CONCAT(N'tenant ', c.TenantId), CHECKSUM_AGG(CHECKSUM(c.ClockingStaffId, c.StafId, c.ClockInTime, c.ClockOutTime, c.LeaveOnBreakTime, c.ReturnOnBreakTime, c.WorkingHours, c.ClockDate))
FROM [dbo].[ClockingsStaff] c GROUP BY c.TenantId

UNION ALL
SELECT N'volunteer checksum', CONCAT(N'tenant ', c.TenantId), CHECKSUM_AGG(CHECKSUM(c.ClockingId, c.VoluntId, c.ClockInTime, c.ClockOutTime, c.LeaveOnBreakTime, c.ReturnOnBreakTime, c.WorkingHours, c.ClockDate))
FROM [dbo].[Clockings] c GROUP BY c.TenantId

UNION ALL
SELECT N'users checksum', N'AspNetUsers', CHECKSUM_AGG(CHECKSUM(u.Id, u.NormalizedEmail, u.PasswordHash, u.SecurityStamp, u.TenantId))
FROM [dbo].[AspNetUsers] u

ORDER BY Kind, Item;
