-- Synthetic data for migration rehearsals (Phase 0 step 0.10). DISPOSABLE DATABASES ONLY.
-- Run on a database at the approved app's last migration (20261003091001_AddVolunteerContactPersonDetails).
-- Everything is invented: ".test" emails, 555 phone numbers, no password hashes (nobody can sign in).
--
-- Volume (defaults): 2 extra organizations next to the house one; per organization 40 staff and
-- 60 volunteers; one completed session per person per day for 400 days: about 120,000 sessions.
-- One organization gets two admins (the M2 "several admins" case); the others one each.
:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = N'20261003091001_AddVolunteerContactPersonDetails')
    THROW 50090, 'Not a baseline-schema database.', 1;
IF EXISTS (SELECT 1 FROM [Staffs]) OR EXISTS (SELECT 1 FROM [Volunteers])
    THROW 50091, 'The database already has people: refusing to add synthetic data (disposable, empty databases only).', 1;
GO

BEGIN TRANSACTION;
DECLARE @staffPerOrg int = 40, @volunteersPerOrg int = 60, @days int = 400;

INSERT INTO [Tenants] ([Name], [TimeZoneId], [IsActive], [CreatedAt], [Address], [ContactInfo])
VALUES (N'Rehearsal Org Alpha', N'America/Chicago', 1, SYSUTCDATETIME(), N'1 Test Street', N'555-0100'),
       (N'Rehearsal Org Beta', N'America/Los_Angeles', 1, SYSUTCDATETIME(), N'2 Test Street', N'555-0200');

-- Roles as the app creates them at startup (IdentitySeeder), so starting the app changes nothing.
INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp], [Description])
SELECT CONVERT(nvarchar(450), NEWID()), r.Name, UPPER(r.Name), CONVERT(nvarchar(64), NEWID()), r.Description
FROM (VALUES (N'Admin', N'Full back-office access'), (N'SuperAdmin', N'Platform operator - creates and manages tenants')) r(Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM [AspNetRoles] x WHERE x.NormalizedName = UPPER(r.Name));

-- Admins: one per organization, a second one in the first organization; no passwords.
INSERT INTO [AspNetUsers] ([Id], [UserName], [NormalizedUserName], [Email], [NormalizedEmail], [EmailConfirmed], [PasswordHash],
    [SecurityStamp], [ConcurrencyStamp], [PhoneNumber], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnd], [LockoutEnabled],
    [AccessFailedCount], [FirstName], [MiddleName], [LastName], [Type], [TenantId])
SELECT CONVERT(nvarchar(450), NEWID()), a.Email, UPPER(a.Email), a.Email, UPPER(a.Email), 1, NULL,
       CONVERT(nvarchar(64), NEWID()), CONVERT(nvarchar(64), NEWID()), N'555-0199', 0, 0, NULL, 1, 0, N'Rehearsal', NULL, N'Admin', N'Admin', a.TenantId
FROM (
    SELECT CONCAT(N'admin', t.Id, N'@rehearsal.test') AS Email, t.Id AS TenantId FROM [Tenants] t
    UNION ALL
    SELECT CONCAT(N'second.admin', MIN(t.Id), N'@rehearsal.test'), MIN(t.Id) FROM [Tenants] t
) a;

INSERT INTO [AspNetUserRoles] ([UserId], [RoleId])
SELECT u.Id, r.Id FROM [AspNetUsers] u CROSS JOIN [AspNetRoles] r
WHERE u.Email LIKE N'%@rehearsal.test' AND r.NormalizedName = N'ADMIN';

;WITH n AS (SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT INTO [Staffs] ([EmailAddress], [FirstName], [LastName], [ZipCode], [Gender], [Type], [PhoneNumber], [Address], [CreatedAt], [TenantId])
SELECT CONCAT(N'staff', n.i, N'.org', t.Id, N'@rehearsal.test'), CONCAT(N'Staff', n.i), CONCAT(N'Org', t.Id), N'00000', N'F', N'Staff',
       N'555-0101', N'Test address', SYSUTCDATETIME(), t.Id
FROM [Tenants] t CROSS JOIN n WHERE n.i <= @staffPerOrg;

;WITH n AS (SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT INTO [Volunteers] ([EmailAddress], [FirstName], [LastName], [ZipCode], [Gender], [Type], [PhoneNumber], [Address], [CreatedAt], [TenantId])
SELECT CONCAT(N'volunteer', n.i, N'.org', t.Id, N'@rehearsal.test'), CONCAT(N'Volunteer', n.i), CONCAT(N'Org', t.Id), N'00000', N'F', N'Volunteer',
       N'555-0102', N'Test address', SYSUTCDATETIME(), t.Id
FROM [Tenants] t CROSS JOIN n WHERE n.i <= @volunteersPerOrg;

-- One completed session per person per day: in 08:00 + (id mod 60) minutes, a 30-minute break, out 8 hours later.
;WITH d AS (SELECT TOP (@days) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k FROM sys.all_objects a CROSS JOIN sys.all_objects b),
      s AS (SELECT p.StaffId AS PersonId, p.FullName, p.TenantId, CAST(DATEADD(day, -d.k - 1, CAST(SYSUTCDATETIME() AS date)) AS datetime2) AS Day
            FROM (SELECT StaffId, FirstName + N' ' + LastName AS FullName, TenantId FROM [Staffs]) p CROSS JOIN d)
INSERT INTO [ClockingsStaff] ([StafId], [FullName], [ClockInTime], [ClockOutTime], [LeaveOnBreakTime], [ReturnOnBreakTime], [WorkingHours], [CreatedAt], [ClockDate], [TenantId])
SELECT PersonId, FullName,
       DATEADD(minute, 480 + PersonId % 60, Day), DATEADD(minute, 960 + PersonId % 60, Day),
       DATEADD(minute, 720, Day), DATEADD(minute, 750, Day), CAST('07:30:00' AS time),
       DATEADD(minute, 480 + PersonId % 60, Day), CAST(Day AS date), TenantId
FROM s;

;WITH d AS (SELECT TOP (@days) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k FROM sys.all_objects a CROSS JOIN sys.all_objects b),
      s AS (SELECT p.VolunteerId AS PersonId, p.FullName, p.TenantId, CAST(DATEADD(day, -d.k - 1, CAST(SYSUTCDATETIME() AS date)) AS datetime2) AS Day
            FROM (SELECT VolunteerId, FirstName + N' ' + LastName AS FullName, TenantId FROM [Volunteers]) p CROSS JOIN d)
INSERT INTO [Clockings] ([VoluntId], [FullName], [ClockInTime], [ClockOutTime], [LeaveOnBreakTime], [ReturnOnBreakTime], [WorkingHours], [CreatedAt], [ClockDate], [TenantId])
SELECT PersonId, FullName,
       DATEADD(minute, 540 + PersonId % 45, Day), DATEADD(minute, 780 + PersonId % 45, Day),
       NULL, NULL, CAST('04:00:00' AS time),
       DATEADD(minute, 540 + PersonId % 45, Day), CAST(Day AS date), TenantId
FROM s;

COMMIT;
GO
