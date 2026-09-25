/*
 * One-time migration: convert existing clocking/registration timestamps from
 * org-local wall-clock time (Eastern Standard Time - Columbus, Ohio) to UTC.
 *
 * WHY: the application used to write DateTime.Now (server-local time) directly
 * into these datetime2 columns. It has been changed to write DateTime.UtcNow
 * instead, and to convert to/from Eastern time only at the edges (display,
 * form input). Existing rows are still in the OLD local-time representation,
 * so they must be shifted once to UTC or they will suddenly read ~4-5 hours
 * off (depending on DST) as soon as the new code reads them.
 *
 * HOW: AT TIME ZONE correctly accounts for the EST/EDT boundary per-row (unlike
 * a flat +5 hour shift), so a clock-in from July and one from January are each
 * converted using the offset that was actually in effect on that date.
 *
 * SAFETY:
 *   1. BACK UP THE DATABASE before running this. This is a one-time,
 *      one-way conversion.
 *   2. Run this ONLY ONCE, and only after deploying the UTC-based application
 *      code (and before real traffic starts writing new, already-UTC rows -
 *      running it twice, or running it after new rows exist, will corrupt data).
 *   3. Take the app offline (or put it in maintenance mode) while this runs,
 *      so nothing writes a new row mid-migration.
 *   4. This script opens a transaction and does NOT auto-commit. Run the
 *      preview SELECTs first, inspect the *_Before/*_After columns, and only
 *      then COMMIT. If anything looks wrong, ROLLBACK.
 */

-- =========================================================================
-- STEP 1: PREVIEW - run this first and eyeball a few rows before touching data.
-- =========================================================================
SELECT TOP 20
    ClockingStaffId,
    ClockInTime  AS ClockInTime_Before,
    ClockInTime  AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS ClockInTime_After,
    CreatedAt    AS CreatedAt_Before,
    CreatedAt    AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS CreatedAt_After
FROM dbo.ClockingsStaff
ORDER BY ClockingStaffId DESC;

SELECT TOP 20
    ClockingId,
    ClockInTime  AS ClockInTime_Before,
    ClockInTime  AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS ClockInTime_After,
    CreatedAt    AS CreatedAt_Before,
    CreatedAt    AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS CreatedAt_After
FROM dbo.Clockings
ORDER BY ClockingId DESC;

-- =========================================================================
-- STEP 2: THE ACTUAL MIGRATION - opens a transaction, does not commit for you.
-- =========================================================================
BEGIN TRAN ConvertTimestampsToUtc;

UPDATE dbo.ClockingsStaff
SET
    ClockInTime       = CASE WHEN ClockInTime       IS NULL THEN NULL ELSE CAST(ClockInTime       AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END,
    ClockOutTime      = CASE WHEN ClockOutTime      IS NULL THEN NULL ELSE CAST(ClockOutTime      AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END,
    LeaveOnBreakTime  = CASE WHEN LeaveOnBreakTime  IS NULL THEN NULL ELSE CAST(LeaveOnBreakTime  AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END,
    ReturnOnBreakTime = CASE WHEN ReturnOnBreakTime IS NULL THEN NULL ELSE CAST(ReturnOnBreakTime AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END,
    CreatedAt         = CASE WHEN CreatedAt         IS NULL THEN NULL ELSE CAST(CreatedAt         AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END;

UPDATE dbo.Clockings
SET
    ClockInTime       = CASE WHEN ClockInTime       IS NULL THEN NULL ELSE CAST(ClockInTime       AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END,
    ClockOutTime      = CASE WHEN ClockOutTime      IS NULL THEN NULL ELSE CAST(ClockOutTime      AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END,
    LeaveOnBreakTime  = CASE WHEN LeaveOnBreakTime  IS NULL THEN NULL ELSE CAST(LeaveOnBreakTime  AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END,
    ReturnOnBreakTime = CASE WHEN ReturnOnBreakTime IS NULL THEN NULL ELSE CAST(ReturnOnBreakTime AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END,
    CreatedAt         = CASE WHEN CreatedAt         IS NULL THEN NULL ELSE CAST(CreatedAt         AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END;

UPDATE dbo.Staffs
SET CreatedAt = CASE WHEN CreatedAt IS NULL THEN NULL ELSE CAST(CreatedAt AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END;

UPDATE dbo.Volunteers
SET CreatedAt = CASE WHEN CreatedAt IS NULL THEN NULL ELSE CAST(CreatedAt AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC' AS datetime2) END;

-- =========================================================================
-- STEP 3: VERIFY, then finish the transaction yourself.
-- =========================================================================
-- Re-run a spot check inside the open transaction, e.g.:
-- SELECT TOP 20 ClockingStaffId, ClockInTime, CreatedAt FROM dbo.ClockingsStaff ORDER BY ClockingStaffId DESC;
--
-- If it looks right:
-- COMMIT TRAN ConvertTimestampsToUtc;
--
-- If anything looks wrong:
-- ROLLBACK TRAN ConvertTimestampsToUtc;
