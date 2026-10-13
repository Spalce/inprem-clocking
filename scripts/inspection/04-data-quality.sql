-- 04 - Data quality checks Phase 0 depends on. READ-ONLY. Run against a RESTORED LOCAL COPY
-- only (see README.md).
--
-- Result 1 (Summary) has counts only and is safe to paste into
-- docs/multitenancy/inspection-results.md. Results 2 onwards are DETAIL for the developer:
-- they contain ids and emails. Do not paste them anywhere; summarize instead.
:on error exit
SET NOCOUNT ON;
IF '$(ConfirmLocalCopy)' <> 'YES'
BEGIN
    RAISERROR('Refusing to run: pass -v ConfirmLocalCopy=YES to confirm this is a restored local copy, never the hosted database.', 16, 1);
    SET NOEXEC ON;
END
GO

-- 1. SUMMARY (counts only). Phase 0 expects the first six to be 0.
SELECT Check_, Problems FROM (VALUES
    (1, N'Emails used by more than one account (NormalizedEmail)',
        (SELECT COUNT(*) FROM (SELECT NormalizedEmail FROM AspNetUsers WHERE NormalizedEmail IS NOT NULL
                               GROUP BY NormalizedEmail HAVING COUNT(*) > 1) d)),
    (2, N'Accounts with no organization that are not SuperAdmin',
        (SELECT COUNT(*) FROM AspNetUsers u WHERE u.TenantId IS NULL
            AND NOT EXISTS (SELECT 1 FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId
                            WHERE ur.UserId = u.Id AND r.Name = N'SuperAdmin'))),
    (3, N'Volunteer sessions whose volunteer is in another organization',
        (SELECT COUNT(*) FROM Clockings c JOIN Volunteers v ON v.VolunteerId = c.VoluntId WHERE c.TenantId <> v.TenantId)),
    (4, N'Staff sessions whose staff member is in another organization',
        (SELECT COUNT(*) FROM ClockingsStaff cs JOIN Staffs s ON s.StaffId = cs.StafId WHERE cs.TenantId <> s.TenantId)),
    (5, N'People with more than one volunteer session on the same ClockDate',
        (SELECT COUNT(*) FROM (SELECT VoluntId, ClockDate FROM Clockings GROUP BY VoluntId, ClockDate HAVING COUNT(*) > 1) d)),
    (6, N'People with more than one staff session on the same ClockDate',
        (SELECT COUNT(*) FROM (SELECT StafId, ClockDate FROM ClockingsStaff GROUP BY StafId, ClockDate HAVING COUNT(*) > 1) d)),
    (7, N'Sessions with ClockDate 0001-01-01 (moved by the Move buttons, finding F-01/F-02)',
        (SELECT COUNT(*) FROM Clockings WHERE ClockDate = '00010101') + (SELECT COUNT(*) FROM ClockingsStaff WHERE ClockDate = '00010101')),
    (8, N'Organizations with no admin account',
        (SELECT COUNT(*) FROM Tenants t WHERE NOT EXISTS (SELECT 1 FROM AspNetUsers u WHERE u.TenantId = t.Id))),
    (9, N'Accounts without the Admin or SuperAdmin role',
        (SELECT COUNT(*) FROM AspNetUsers u WHERE NOT EXISTS (
            SELECT 1 FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId
            WHERE ur.UserId = u.Id AND r.Name IN (N'Admin', N'SuperAdmin')))),
    (10, N'Tenants (rows)',       (SELECT COUNT(*) FROM Tenants)),
    (11, N'Subscriptions (rows)', (SELECT COUNT(*) FROM Subscriptions)),
    (12, N'Invoices (rows)',      (SELECT COUNT(*) FROM Invoices))
) AS checks (CheckNo, Check_, Problems)
ORDER BY CheckNo;

-- 2. DETAIL: accounts sharing an email (the project owner decides how to merge them).
SELECT u.NormalizedEmail, u.Id AS UserId, u.TenantId, u.Type, u.EmailConfirmed
FROM AspNetUsers u
WHERE u.NormalizedEmail IN (SELECT NormalizedEmail FROM AspNetUsers WHERE NormalizedEmail IS NOT NULL
                            GROUP BY NormalizedEmail HAVING COUNT(*) > 1)
ORDER BY u.NormalizedEmail, u.TenantId;

-- 3. DETAIL: accounts with no organization that are not SuperAdmin.
SELECT u.Id AS UserId, u.NormalizedEmail, u.Type
FROM AspNetUsers u
WHERE u.TenantId IS NULL
  AND NOT EXISTS (SELECT 1 FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId
                  WHERE ur.UserId = u.Id AND r.Name = N'SuperAdmin');

-- 4. DETAIL: sessions whose person is in another organization.
SELECT N'volunteer' AS Kind, c.ClockingId AS SessionId, c.TenantId AS SessionTenantId, v.VolunteerId AS PersonId, v.TenantId AS PersonTenantId, c.ClockDate
FROM Clockings c JOIN Volunteers v ON v.VolunteerId = c.VoluntId WHERE c.TenantId <> v.TenantId
UNION ALL
SELECT N'staff', cs.ClockingStaffId, cs.TenantId, s.StaffId, s.TenantId, cs.ClockDate
FROM ClockingsStaff cs JOIN Staffs s ON s.StaffId = cs.StafId WHERE cs.TenantId <> s.TenantId
ORDER BY Kind, SessionId;

-- 5. DETAIL: more than one session per person per ClockDate.
SELECT N'volunteer' AS Kind, VoluntId AS PersonId, ClockDate, COUNT(*) AS Sessions
FROM Clockings GROUP BY VoluntId, ClockDate HAVING COUNT(*) > 1
UNION ALL
SELECT N'staff', StafId, ClockDate, COUNT(*)
FROM ClockingsStaff GROUP BY StafId, ClockDate HAVING COUNT(*) > 1
ORDER BY Kind, PersonId, ClockDate;

-- 6. DETAIL: admins per organization (input for choosing each organization's Owner in
-- migration M2). AspNetUsers has no creation date and Id is a GUID, so "earliest admin" cannot
-- be derived from this table: the project owner names each Owner from this list.
SELECT t.Id AS TenantId, t.Name AS Organization, u.Id AS UserId, u.NormalizedEmail,
       u.FirstName, u.LastName,
       COUNT(u.Id) OVER (PARTITION BY t.Id) AS AdminsInOrganization
FROM Tenants t
LEFT JOIN AspNetUsers u ON u.TenantId = t.Id
ORDER BY t.Id, u.NormalizedEmail;
GO
