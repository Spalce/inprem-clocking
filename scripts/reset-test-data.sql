/*
 * Reset script: permanently deletes ALL Staff, Volunteer, and clocking records,
 * and ALL admin accounts except admin@inprem.org. Leaves the Admin role untouched.
 *
 * This is the exact cleanup used to reset the app back to a single-admin, empty
 * state - run it, then run seed-test-data.sql to load a fresh batch of test data.
 *
 * THIS IS DESTRUCTIVE AND IRREVERSIBLE. Take a backup first if you want one:
 *   BACKUP DATABASE [YourDbName] TO DISK = 'C:\path\to\backup.bak';
 *
 * USAGE:
 *   sqlcmd -S <server> -d <database> -E -i scripts\reset-test-data.sql
 */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

-- Clear all clocking history (child tables first, FK dependencies on Staffs/Volunteers)
DELETE FROM ClockingsStaff;
DELETE FROM Clockings;

-- Clear all staff and volunteer records
DELETE FROM Staffs;
DELETE FROM Volunteers;

-- Remove every admin account except admin@inprem.org (Identity child tables first)
DELETE FROM AspNetUserTokens WHERE UserId IN (SELECT Id FROM AspNetUsers WHERE Email <> 'admin@inprem.org');
DELETE FROM AspNetUserLogins WHERE UserId IN (SELECT Id FROM AspNetUsers WHERE Email <> 'admin@inprem.org');
DELETE FROM AspNetUserClaims WHERE UserId IN (SELECT Id FROM AspNetUsers WHERE Email <> 'admin@inprem.org');
DELETE FROM AspNetUserRoles WHERE UserId IN (SELECT Id FROM AspNetUsers WHERE Email <> 'admin@inprem.org');
DELETE FROM AspNetUsers WHERE Email <> 'admin@inprem.org';

COMMIT TRANSACTION;

SELECT
  (SELECT COUNT(*) FROM Staffs)         AS Staffs,
  (SELECT COUNT(*) FROM Volunteers)     AS Volunteers,
  (SELECT COUNT(*) FROM ClockingsStaff) AS StaffClockings,
  (SELECT COUNT(*) FROM Clockings)      AS VolunteerClockings,
  (SELECT COUNT(*) FROM AspNetUsers)    AS Users;
