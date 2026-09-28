/*
 * Seed script: adds a realistic set of test data for manually exercising the app -
 * enough rows to trigger pagination everywhere it exists, plus a few deliberately
 * unusual clocking records to exercise specific bug fixes. See TESTING.md at the
 * repo root for the full guided walkthrough this data is designed to support.
 *
 * WHAT IT CREATES (all brand new rows - never modifies or deletes anything):
 *   - 25 Staff, 25 Volunteers, with unique emails/phones/zips/addresses.
 *   - 45 Staff clocking records, 45 Volunteer clocking records:
 *       - 20 people (per side) get two complete 8-hour shifts each, on different
 *         calendar days spread over the last ~4 weeks (for date-range report
 *         filters and pagination).
 *       - 4 people (per side) are left clocked in *today*, with no clock-out -
 *         use these to try the Clock Out / Break Start / Break End dropdown.
 *       - 1 person (per side) was clocked in 2 days ago and never clocked out -
 *         use this to confirm the WorkingHours-overflow crash fix: clocking
 *         them out should succeed and show 23:59:59 instead of crashing.
 *
 * SAFE TO RE-RUN: each run adds another full batch on top of whatever is already
 * there (emails are suffixed with a fixed name, so re-running will hit the unique
 * email constraint and fail - run reset-test-data.sql first if you want a clean
 * batch again).
 *
 * USAGE:
 *   sqlcmd -S <server> -d <database> -E -i scripts\seed-test-data.sql
 */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------
-- Name pools (25 of each, zipped 1:1 for Staff, rotated for Volunteers
-- so the two lists of people don't look identical)
------------------------------------------------------------------
DECLARE @FirstNames TABLE (n INT PRIMARY KEY, Name NVARCHAR(50));
INSERT INTO @FirstNames (n, Name) VALUES
(1,'James'),(2,'Mary'),(3,'Robert'),(4,'Patricia'),(5,'John'),
(6,'Jennifer'),(7,'Michael'),(8,'Linda'),(9,'David'),(10,'Elizabeth'),
(11,'William'),(12,'Barbara'),(13,'Richard'),(14,'Susan'),(15,'Joseph'),
(16,'Jessica'),(17,'Thomas'),(18,'Sarah'),(19,'Charles'),(20,'Karen'),
(21,'Daniel'),(22,'Nancy'),(23,'Matthew'),(24,'Lisa'),(25,'Anthony');

DECLARE @LastNames TABLE (n INT PRIMARY KEY, Name NVARCHAR(50));
INSERT INTO @LastNames (n, Name) VALUES
(1,'Smith'),(2,'Johnson'),(3,'Williams'),(4,'Brown'),(5,'Jones'),
(6,'Garcia'),(7,'Miller'),(8,'Davis'),(9,'Rodriguez'),(10,'Martinez'),
(11,'Hernandez'),(12,'Lopez'),(13,'Gonzalez'),(14,'Wilson'),(15,'Anderson'),
(16,'Thomas'),(17,'Taylor'),(18,'Moore'),(19,'Jackson'),(20,'Martin'),
(21,'Lee'),(22,'Perez'),(23,'Thompson'),(24,'White'),(25,'Harris');

------------------------------------------------------------------
-- 25 Staff, 25 Volunteers
------------------------------------------------------------------
INSERT INTO Staffs (EmailAddress, FirstName, LastName, ZipCode, Gender, Type, PhoneNumber, Address, CreatedAt)
SELECT
  LOWER(f.Name) + '.' + LOWER(l.Name) + '.staff' + CAST(f.n AS VARCHAR(3)) + '@example.com',
  f.Name,
  l.Name,
  CAST(43000 + f.n AS VARCHAR(10)),
  CASE WHEN f.n % 2 = 0 THEN 'F' ELSE 'M' END,
  'Staff',
  '6145550' + RIGHT('000' + CAST(f.n AS VARCHAR(3)), 3),
  CAST(100 + f.n AS VARCHAR(5)) + ' Example Street, Columbus, OH',
  SYSUTCDATETIME()
FROM @FirstNames f JOIN @LastNames l ON l.n = f.n;

INSERT INTO Volunteers (EmailAddress, FirstName, LastName, ZipCode, Gender, Type, PhoneNumber, Address, VolunteerCategory, CreatedAt)
SELECT
  LOWER(f.Name) + '.' + LOWER(l2.Name) + '.vol' + CAST(f.n AS VARCHAR(3)) + '@example.com',
  f.Name,
  l2.Name,
  CAST(43100 + f.n AS VARCHAR(10)),
  CASE WHEN f.n % 2 = 0 THEN 'M' ELSE 'F' END,
  'Volunteer',
  '6145560' + RIGHT('000' + CAST(f.n AS VARCHAR(3)), 3),
  CAST(200 + f.n AS VARCHAR(5)) + ' Example Avenue, Columbus, OH',
  CASE f.n % 5
    WHEN 0 THEN 'Mandated Community Hours'
    WHEN 1 THEN 'MOFC Volunteer Hub'
    WHEN 2 THEN 'Educational Purposes'
    WHEN 3 THEN 'Corporate Volunteering'
    ELSE 'Personal or Individual Volunteering'
  END,
  SYSUTCDATETIME()
FROM @FirstNames f
JOIN @LastNames l2 ON l2.n = ((f.n + 4) % 25) + 1;

------------------------------------------------------------------
-- Map the generated rows back to their identity values, using the
-- (unique, deterministic) ZipCode offset assigned above
------------------------------------------------------------------
DECLARE @StaffMap TABLE (StaffId BIGINT, n INT, FullName NVARCHAR(210));
INSERT INTO @StaffMap SELECT StaffId, CAST(ZipCode AS INT) - 43000, FirstName + ' ' + LastName FROM Staffs
WHERE ZipCode LIKE '4300%' AND LEN(ZipCode) = 5;

DECLARE @VolMap TABLE (VolunteerId BIGINT, n INT, FullName NVARCHAR(210));
INSERT INTO @VolMap SELECT VolunteerId, CAST(ZipCode AS INT) - 43100, FirstName + ' ' + LastName FROM Volunteers
WHERE ZipCode LIKE '4310%' AND LEN(ZipCode) = 5;

DECLARE @TodayLocal DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time' AS DATE);

------------------------------------------------------------------
-- Staff clocking history
--   n 1..20  -> two complete 8-hour shifts each (n days ago, and n+7 days ago)
--   n 21..24 -> still clocked in today (no clock-out) - for testing the
--               Clock Out / Break Start / Break End dropdown actions live
--   n 25     -> a session clocked in 2 days ago and never closed out - for
--               re-verifying the WorkingHours-overflow fix on demand
------------------------------------------------------------------
INSERT INTO ClockingsStaff (StafId, FullName, ClockInTime, ClockOutTime, LeaveOnBreakTime, ReturnOnBreakTime, WorkingHours, CreatedAt, ClockDate)
SELECT
  m.StaffId, m.FullName,
  CAST(s.ClockInLocal    AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.ClockOutLocal   AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.BreakStartLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.BreakEndLocal   AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST('08:00:00' AS TIME),
  CAST(s.ClockInLocal    AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  s.LocalDate
FROM @StaffMap m
CROSS APPLY (
  SELECT
    DATEADD(DAY, -m.n, @TodayLocal) AS LocalDate,
    DATEADD(MINUTE, 8*60,        CAST(DATEADD(DAY, -m.n, @TodayLocal) AS DATETIME2)) AS ClockInLocal,
    DATEADD(MINUTE, 16*60+30,    CAST(DATEADD(DAY, -m.n, @TodayLocal) AS DATETIME2)) AS ClockOutLocal,
    DATEADD(MINUTE, 12*60,       CAST(DATEADD(DAY, -m.n, @TodayLocal) AS DATETIME2)) AS BreakStartLocal,
    DATEADD(MINUTE, 12*60+30,    CAST(DATEADD(DAY, -m.n, @TodayLocal) AS DATETIME2)) AS BreakEndLocal
) s
WHERE m.n BETWEEN 1 AND 20;

INSERT INTO ClockingsStaff (StafId, FullName, ClockInTime, ClockOutTime, LeaveOnBreakTime, ReturnOnBreakTime, WorkingHours, CreatedAt, ClockDate)
SELECT
  m.StaffId, m.FullName,
  CAST(s.ClockInLocal    AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.ClockOutLocal   AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.BreakStartLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.BreakEndLocal   AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST('08:00:00' AS TIME),
  CAST(s.ClockInLocal    AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  s.LocalDate
FROM @StaffMap m
CROSS APPLY (
  SELECT
    DATEADD(DAY, -(m.n + 7), @TodayLocal) AS LocalDate,
    DATEADD(MINUTE, 8*60,        CAST(DATEADD(DAY, -(m.n + 7), @TodayLocal) AS DATETIME2)) AS ClockInLocal,
    DATEADD(MINUTE, 16*60+30,    CAST(DATEADD(DAY, -(m.n + 7), @TodayLocal) AS DATETIME2)) AS ClockOutLocal,
    DATEADD(MINUTE, 12*60,       CAST(DATEADD(DAY, -(m.n + 7), @TodayLocal) AS DATETIME2)) AS BreakStartLocal,
    DATEADD(MINUTE, 12*60+30,    CAST(DATEADD(DAY, -(m.n + 7), @TodayLocal) AS DATETIME2)) AS BreakEndLocal
) s
WHERE m.n BETWEEN 1 AND 20;

INSERT INTO ClockingsStaff (StafId, FullName, ClockInTime, ClockOutTime, LeaveOnBreakTime, ReturnOnBreakTime, WorkingHours, CreatedAt, ClockDate)
SELECT
  m.StaffId, m.FullName,
  CAST(s.ClockInLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  NULL, NULL, NULL, NULL,
  CAST(s.ClockInLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  @TodayLocal
FROM @StaffMap m
CROSS APPLY (
  SELECT DATEADD(MINUTE, 8*60 + m.n, CAST(@TodayLocal AS DATETIME2)) AS ClockInLocal
) s
WHERE m.n BETWEEN 21 AND 24;

INSERT INTO ClockingsStaff (StafId, FullName, ClockInTime, ClockOutTime, LeaveOnBreakTime, ReturnOnBreakTime, WorkingHours, CreatedAt, ClockDate)
SELECT
  m.StaffId, m.FullName,
  CAST(s.ClockInLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  NULL, NULL, NULL, NULL,
  CAST(s.ClockInLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  s.LocalDate
FROM @StaffMap m
CROSS APPLY (
  SELECT
    DATEADD(DAY, -2, @TodayLocal) AS LocalDate,
    DATEADD(MINUTE, 18*60+13, CAST(DATEADD(DAY, -2, @TodayLocal) AS DATETIME2)) AS ClockInLocal
) s
WHERE m.n = 25;

------------------------------------------------------------------
-- Volunteer clocking history - identical shape to the Staff block above
------------------------------------------------------------------
INSERT INTO Clockings (VoluntId, FullName, ClockInTime, ClockOutTime, LeaveOnBreakTime, ReturnOnBreakTime, WorkingHours, CreatedAt, ClockDate)
SELECT
  m.VolunteerId, m.FullName,
  CAST(s.ClockInLocal    AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.ClockOutLocal   AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.BreakStartLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.BreakEndLocal   AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST('08:00:00' AS TIME),
  CAST(s.ClockInLocal    AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  s.LocalDate
FROM @VolMap m
CROSS APPLY (
  SELECT
    DATEADD(DAY, -m.n, @TodayLocal) AS LocalDate,
    DATEADD(MINUTE, 9*60,        CAST(DATEADD(DAY, -m.n, @TodayLocal) AS DATETIME2)) AS ClockInLocal,
    DATEADD(MINUTE, 17*60+30,    CAST(DATEADD(DAY, -m.n, @TodayLocal) AS DATETIME2)) AS ClockOutLocal,
    DATEADD(MINUTE, 13*60,       CAST(DATEADD(DAY, -m.n, @TodayLocal) AS DATETIME2)) AS BreakStartLocal,
    DATEADD(MINUTE, 13*60+30,    CAST(DATEADD(DAY, -m.n, @TodayLocal) AS DATETIME2)) AS BreakEndLocal
) s
WHERE m.n BETWEEN 1 AND 20;

INSERT INTO Clockings (VoluntId, FullName, ClockInTime, ClockOutTime, LeaveOnBreakTime, ReturnOnBreakTime, WorkingHours, CreatedAt, ClockDate)
SELECT
  m.VolunteerId, m.FullName,
  CAST(s.ClockInLocal    AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.ClockOutLocal   AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.BreakStartLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST(s.BreakEndLocal   AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  CAST('08:00:00' AS TIME),
  CAST(s.ClockInLocal    AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  s.LocalDate
FROM @VolMap m
CROSS APPLY (
  SELECT
    DATEADD(DAY, -(m.n + 7), @TodayLocal) AS LocalDate,
    DATEADD(MINUTE, 9*60,        CAST(DATEADD(DAY, -(m.n + 7), @TodayLocal) AS DATETIME2)) AS ClockInLocal,
    DATEADD(MINUTE, 17*60+30,    CAST(DATEADD(DAY, -(m.n + 7), @TodayLocal) AS DATETIME2)) AS ClockOutLocal,
    DATEADD(MINUTE, 13*60,       CAST(DATEADD(DAY, -(m.n + 7), @TodayLocal) AS DATETIME2)) AS BreakStartLocal,
    DATEADD(MINUTE, 13*60+30,    CAST(DATEADD(DAY, -(m.n + 7), @TodayLocal) AS DATETIME2)) AS BreakEndLocal
) s
WHERE m.n BETWEEN 1 AND 20;

INSERT INTO Clockings (VoluntId, FullName, ClockInTime, ClockOutTime, LeaveOnBreakTime, ReturnOnBreakTime, WorkingHours, CreatedAt, ClockDate)
SELECT
  m.VolunteerId, m.FullName,
  CAST(s.ClockInLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  NULL, NULL, NULL, NULL,
  CAST(s.ClockInLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  @TodayLocal
FROM @VolMap m
CROSS APPLY (
  SELECT DATEADD(MINUTE, 9*60 + m.n, CAST(@TodayLocal AS DATETIME2)) AS ClockInLocal
) s
WHERE m.n BETWEEN 21 AND 24;

INSERT INTO Clockings (VoluntId, FullName, ClockInTime, ClockOutTime, LeaveOnBreakTime, ReturnOnBreakTime, WorkingHours, CreatedAt, ClockDate)
SELECT
  m.VolunteerId, m.FullName,
  CAST(s.ClockInLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  NULL, NULL, NULL, NULL,
  CAST(s.ClockInLocal AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS DATETIME2),
  s.LocalDate
FROM @VolMap m
CROSS APPLY (
  SELECT
    DATEADD(DAY, -2, @TodayLocal) AS LocalDate,
    DATEADD(MINUTE, 18*60+13, CAST(DATEADD(DAY, -2, @TodayLocal) AS DATETIME2)) AS ClockInLocal
) s
WHERE m.n = 25;

COMMIT TRANSACTION;

------------------------------------------------------------------
-- Summary
------------------------------------------------------------------
SELECT
  (SELECT COUNT(*) FROM Staffs)         AS Staffs,
  (SELECT COUNT(*) FROM Volunteers)     AS Volunteers,
  (SELECT COUNT(*) FROM ClockingsStaff) AS StaffClockings,
  (SELECT COUNT(*) FROM Clockings)      AS VolunteerClockings;
