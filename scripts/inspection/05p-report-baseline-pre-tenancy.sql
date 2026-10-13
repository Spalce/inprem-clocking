-- 05p - Report baseline for a database BEFORE the multi-tenancy migrations (no TenantId).
-- Same reports as 05-report-baseline.sql, for the single organization. After the migrations
-- the totals in 05 for the house organization must equal these. READ-ONLY. RESTORED LOCAL
-- COPY only (README.md). Counts and totals only: safe to paste.
:on error exit
SET NOCOUNT ON;
IF '$(ConfirmLocalCopy)' <> 'YES'
BEGIN
    RAISERROR('Refusing to run: pass -v ConfirmLocalCopy=YES to confirm this is a restored local copy, never the hosted database.', 16, 1);
    SET NOEXEC ON;
END
GO

-- R1. Staff hours per month (by ClockDate).
SELECT N'R1' AS Report, YEAR(ClockDate) AS [Year], MONTH(ClockDate) AS [Month], COUNT(*) AS Sessions,
       SUM(CASE WHEN ClockOutTime IS NULL THEN 1 ELSE 0 END) AS OpenSessions,
       SUM(CAST(DATEDIFF(SECOND, CAST('00:00' AS time), WorkingHours) AS bigint)) / 60 AS WorkedMinutes
FROM ClockingsStaff
GROUP BY YEAR(ClockDate), MONTH(ClockDate)
ORDER BY [Year], [Month];

-- R2. Volunteer hours per month (by ClockDate).
SELECT N'R2' AS Report, YEAR(ClockDate) AS [Year], MONTH(ClockDate) AS [Month], COUNT(*) AS Sessions,
       SUM(CASE WHEN ClockOutTime IS NULL THEN 1 ELSE 0 END) AS OpenSessions,
       SUM(CAST(DATEDIFF(SECOND, CAST('00:00' AS time), WorkingHours) AS bigint)) / 60 AS WorkedMinutes
FROM Clockings
GROUP BY YEAR(ClockDate), MONTH(ClockDate)
ORDER BY [Year], [Month];

-- R3. Dashboard figures.
SELECT N'R3' AS Report,
       (SELECT COUNT(*) FROM Staffs)         AS Staff,
       (SELECT COUNT(*) FROM Volunteers)     AS Volunteers,
       (SELECT COUNT(*) FROM AspNetUsers)    AS Admins,
       (SELECT COUNT(*) FROM ClockingsStaff) AS StaffSessions,
       (SELECT COUNT(*) FROM Clockings)      AS VolunteerSessions,
       ISNULL((SELECT SUM(CAST(DATEDIFF(SECOND, CAST('00:00' AS time), WorkingHours) AS bigint)) / 60 FROM ClockingsStaff), 0) AS StaffWorkedMinutesAllTime;

-- T. Are session times stored in UTC or in Columbus local time? Clock-ins per hour of the day
-- as stored. A kiosk day of roughly 08:00-18:00 local shows up as about 12:00-22:00 if the
-- values are UTC, and as about 08:00-18:00 if they are still local (before the UTC change;
-- see scripts/convert-existing-timestamps-to-utc.sql).
SELECT N'T' AS Report, DATEPART(HOUR, ClockInTime) AS StoredHour,
       SUM(CASE WHEN Kind = 'staff' THEN 1 ELSE 0 END) AS StaffClockIns,
       SUM(CASE WHEN Kind = 'volunteer' THEN 1 ELSE 0 END) AS VolunteerClockIns
FROM (SELECT ClockInTime, 'staff' AS Kind FROM ClockingsStaff
      UNION ALL SELECT ClockInTime, 'volunteer' FROM Clockings) s
WHERE ClockInTime IS NOT NULL
GROUP BY DATEPART(HOUR, ClockInTime)
ORDER BY StoredHour;

-- T2. Date range of the data, and how many sessions' ClockDate matches their clock-in date
-- read as UTC vs read as local time (the ClockDate backfill assumed UTC).
SELECT N'T2' AS Report,
       (SELECT MIN(ClockDate) FROM (SELECT ClockDate FROM ClockingsStaff UNION ALL SELECT ClockDate FROM Clockings) d WHERE ClockDate > '00010101') AS FirstClockDate,
       (SELECT MAX(ClockDate) FROM (SELECT ClockDate FROM ClockingsStaff UNION ALL SELECT ClockDate FROM Clockings) d) AS LastClockDate,
       (SELECT COUNT(*) FROM (SELECT ClockDate, ClockInTime FROM ClockingsStaff UNION ALL SELECT ClockDate, ClockInTime FROM Clockings) d
         WHERE ClockDate = CAST(ClockInTime AS date)) AS ClockDateEqualsStoredDate,
       (SELECT COUNT(*) FROM (SELECT ClockDate, ClockInTime FROM ClockingsStaff UNION ALL SELECT ClockDate, ClockInTime FROM Clockings) d
         WHERE ClockDate = CAST(CAST(ClockInTime AS datetime2) AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time' AS date)) AS ClockDateEqualsDateIfStoredIsUtc,
       (SELECT COUNT(*) FROM (SELECT ClockDate FROM ClockingsStaff UNION ALL SELECT ClockDate FROM Clockings) d) AS Sessions;
GO
