-- 04p - Data quality for a database BEFORE the multi-tenancy migrations (no Tenants table,
-- no TenantId columns; e.g. last migration 20260928094128_AddUniqueEmailIndexes). Use
-- 04-data-quality.sql once TenantId exists. READ-ONLY. RESTORED LOCAL COPY only (README.md).
--
-- Result 1 (Summary) has counts only and is safe to paste. Results 2 onwards are DETAIL with
-- ids and emails: for the developer only, never pasted anywhere.
:on error exit
SET NOCOUNT ON;
IF '$(ConfirmLocalCopy)' <> 'YES'
BEGIN
    RAISERROR('Refusing to run: pass -v ConfirmLocalCopy=YES to confirm this is a restored local copy, never the hosted database.', 16, 1);
    SET NOEXEC ON;
END
GO

-- 1. SUMMARY (counts only).
SELECT Check_, Problems FROM (VALUES
    (1, N'Emails used by more than one account (NormalizedEmail)',
        (SELECT COUNT(*) FROM (SELECT NormalizedEmail FROM AspNetUsers WHERE NormalizedEmail IS NOT NULL
                               GROUP BY NormalizedEmail HAVING COUNT(*) > 1) d)),
    (2, N'Accounts without the Admin role',
        (SELECT COUNT(*) FROM AspNetUsers u WHERE NOT EXISTS (
            SELECT 1 FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId
            WHERE ur.UserId = u.Id AND r.Name = N'Admin'))),
    (3, N'Accounts with no email',
        (SELECT COUNT(*) FROM AspNetUsers WHERE NormalizedEmail IS NULL)),
    (4, N'People with more than one volunteer session on the same ClockDate',
        (SELECT COUNT(*) FROM (SELECT VoluntId, ClockDate FROM Clockings GROUP BY VoluntId, ClockDate HAVING COUNT(*) > 1) d)),
    (5, N'People with more than one staff session on the same ClockDate',
        (SELECT COUNT(*) FROM (SELECT StafId, ClockDate FROM ClockingsStaff GROUP BY StafId, ClockDate HAVING COUNT(*) > 1) d)),
    (6, N'Sessions with ClockDate 0001-01-01 (moved by the Move buttons, finding F-01/F-02)',
        (SELECT COUNT(*) FROM Clockings WHERE ClockDate = '00010101') + (SELECT COUNT(*) FROM ClockingsStaff WHERE ClockDate = '00010101')),
    (7, N'Staff emails used more than once',
        (SELECT COUNT(*) FROM (SELECT EmailAddress FROM Staffs GROUP BY EmailAddress HAVING COUNT(*) > 1) d)),
    (8, N'Volunteer emails used more than once',
        (SELECT COUNT(*) FROM (SELECT EmailAddress FROM Volunteers GROUP BY EmailAddress HAVING COUNT(*) > 1) d)),
    (9, N'Emails registered as both staff and volunteer',
        (SELECT COUNT(*) FROM Staffs s WHERE EXISTS (SELECT 1 FROM Volunteers v WHERE v.EmailAddress = s.EmailAddress))),
    (10, N'Sessions still open (no clock out) from before the backup day',
        (SELECT COUNT(*) FROM Clockings WHERE ClockOutTime IS NULL AND ClockDate < (SELECT CAST(MAX(ClockInTime) AS date) FROM Clockings))
      + (SELECT COUNT(*) FROM ClockingsStaff WHERE ClockOutTime IS NULL AND ClockDate < (SELECT CAST(MAX(ClockInTime) AS date) FROM ClockingsStaff))),
    (11, N'Closed sessions with no WorkingHours',
        (SELECT COUNT(*) FROM Clockings WHERE ClockOutTime IS NOT NULL AND WorkingHours IS NULL)
      + (SELECT COUNT(*) FROM ClockingsStaff WHERE ClockOutTime IS NOT NULL AND WorkingHours IS NULL)),
    (12, N'Sessions with clock out before clock in, or break end before break start',
        (SELECT COUNT(*) FROM Clockings WHERE ClockOutTime < ClockInTime OR ReturnOnBreakTime < LeaveOnBreakTime)
      + (SELECT COUNT(*) FROM ClockingsStaff WHERE ClockOutTime < ClockInTime OR ReturnOnBreakTime < LeaveOnBreakTime)),
    (13, N'Sessions whose stored WorkingHours differs from (out - in) - break by more than 1 minute',
        (SELECT COUNT(*) FROM Clockings WHERE ClockOutTime IS NOT NULL AND WorkingHours IS NOT NULL AND ABS(
            DATEDIFF(SECOND, CAST('00:00' AS time), WorkingHours)
            - (DATEDIFF(SECOND, ClockInTime, ClockOutTime) - ISNULL(DATEDIFF(SECOND, LeaveOnBreakTime, ReturnOnBreakTime), 0))) > 60)
      + (SELECT COUNT(*) FROM ClockingsStaff WHERE ClockOutTime IS NOT NULL AND WorkingHours IS NOT NULL AND ABS(
            DATEDIFF(SECOND, CAST('00:00' AS time), WorkingHours)
            - (DATEDIFF(SECOND, ClockInTime, ClockOutTime) - ISNULL(DATEDIFF(SECOND, LeaveOnBreakTime, ReturnOnBreakTime), 0))) > 60))
) AS checks (CheckNo, Check_, Problems)
ORDER BY CheckNo;

-- 2. DETAIL: accounts sharing an email.
SELECT u.NormalizedEmail, u.Id AS UserId, u.Type
FROM AspNetUsers u
WHERE u.NormalizedEmail IN (SELECT NormalizedEmail FROM AspNetUsers WHERE NormalizedEmail IS NOT NULL
                            GROUP BY NormalizedEmail HAVING COUNT(*) > 1)
ORDER BY u.NormalizedEmail;

-- 3. DETAIL: every admin account (all become admins of one organization when the
-- multi-tenancy migrations backfill; the project owner names the Owner).
SELECT u.Id AS UserId, u.NormalizedEmail, u.FirstName, u.LastName, u.Type,
       (SELECT STRING_AGG(r.Name, ', ') FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId WHERE ur.UserId = u.Id) AS Roles
FROM AspNetUsers u
ORDER BY u.NormalizedEmail;

-- 4. DETAIL: problem sessions (checks 10-13).
SELECT N'volunteer' AS Kind, ClockingId AS SessionId, VoluntId AS PersonId, ClockDate, ClockInTime, ClockOutTime, WorkingHours
FROM Clockings
WHERE (ClockOutTime IS NULL AND ClockDate < (SELECT CAST(MAX(ClockInTime) AS date) FROM Clockings))
   OR (ClockOutTime IS NOT NULL AND WorkingHours IS NULL)
   OR ClockOutTime < ClockInTime OR ReturnOnBreakTime < LeaveOnBreakTime
UNION ALL
SELECT N'staff', ClockingStaffId, StafId, ClockDate, ClockInTime, ClockOutTime, WorkingHours
FROM ClockingsStaff
WHERE (ClockOutTime IS NULL AND ClockDate < (SELECT CAST(MAX(ClockInTime) AS date) FROM ClockingsStaff))
   OR (ClockOutTime IS NOT NULL AND WorkingHours IS NULL)
   OR ClockOutTime < ClockInTime OR ReturnOnBreakTime < LeaveOnBreakTime
ORDER BY Kind, SessionId;
GO
