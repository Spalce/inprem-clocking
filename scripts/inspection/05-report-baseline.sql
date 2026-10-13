-- 05 - Report baseline: totals to compare before and after every migration. READ-ONLY.
-- Run against a RESTORED LOCAL COPY only (see README.md). Counts and totals only, no personal
-- data: safe to paste into docs/multitenancy/inspection-results.md.
--
-- Three reports, proposed in G0 (the prompt asks for "three agreed reports"; change them here
-- if the project owner wants others):
--   R1 staff hours worked per organization per month      (Hours Worked / Staff Clocking Report)
--   R2 volunteer hours worked per organization per month  (Hours Worked / BackOffice volunteer card)
--   R3 dashboard figures per organization                 (BackOffice: counts and all-time staff time)
-- Months are by ClockDate (the organization's local day). Hours come from the stored
-- WorkingHours, the same column the app's totals use.
:on error exit
SET NOCOUNT ON;
IF '$(ConfirmLocalCopy)' <> 'YES'
BEGIN
    RAISERROR('Refusing to run: pass -v ConfirmLocalCopy=YES to confirm this is a restored local copy, never the hosted database.', 16, 1);
    SET NOEXEC ON;
END
GO

-- R1. Staff hours per organization per month.
SELECT N'R1' AS Report, cs.TenantId, YEAR(cs.ClockDate) AS [Year], MONTH(cs.ClockDate) AS [Month],
       COUNT(*) AS Sessions,
       SUM(CASE WHEN cs.ClockOutTime IS NULL THEN 1 ELSE 0 END) AS OpenSessions,
       SUM(CAST(DATEDIFF(SECOND, CAST('00:00' AS time), cs.WorkingHours) AS bigint)) / 60 AS WorkedMinutes
FROM ClockingsStaff cs
GROUP BY cs.TenantId, YEAR(cs.ClockDate), MONTH(cs.ClockDate)
ORDER BY cs.TenantId, [Year], [Month];

-- R2. Volunteer hours per organization per month.
SELECT N'R2' AS Report, c.TenantId, YEAR(c.ClockDate) AS [Year], MONTH(c.ClockDate) AS [Month],
       COUNT(*) AS Sessions,
       SUM(CASE WHEN c.ClockOutTime IS NULL THEN 1 ELSE 0 END) AS OpenSessions,
       SUM(CAST(DATEDIFF(SECOND, CAST('00:00' AS time), c.WorkingHours) AS bigint)) / 60 AS WorkedMinutes
FROM Clockings c
GROUP BY c.TenantId, YEAR(c.ClockDate), MONTH(c.ClockDate)
ORDER BY c.TenantId, [Year], [Month];

-- R3. Dashboard figures per organization.
SELECT N'R3' AS Report, t.Id AS TenantId,
       (SELECT COUNT(*) FROM Staffs s WHERE s.TenantId = t.Id)          AS Staff,
       (SELECT COUNT(*) FROM Volunteers v WHERE v.TenantId = t.Id)      AS Volunteers,
       (SELECT COUNT(*) FROM AspNetUsers u WHERE u.TenantId = t.Id)     AS Admins,
       (SELECT COUNT(*) FROM ClockingsStaff cs WHERE cs.TenantId = t.Id) AS StaffSessions,
       (SELECT COUNT(*) FROM Clockings c WHERE c.TenantId = t.Id)        AS VolunteerSessions,
       ISNULL((SELECT SUM(CAST(DATEDIFF(SECOND, CAST('00:00' AS time), cs.WorkingHours) AS bigint)) / 60
               FROM ClockingsStaff cs WHERE cs.TenantId = t.Id), 0)      AS StaffWorkedMinutesAllTime
FROM Tenants t
ORDER BY t.Id;
GO
