# Database inspection results (G0-2)

**Status: done on 2026-10-13.** One question for the developer is open (time storage, at the end).

Counts and totals only. No names, emails or ids of people are recorded here.

## Backup

- **File:** `db-backups/db_aa61e6_inpremdb3_10_5_2026_8.bak` (git-ignored). The developer describes it as the production database of the old app, the reference app.
- **Contents:** database `db_aa61e6_inpremdb3` on host server `sql9001`, backed up 2026-10-05 08:35. Hosted on SQL Server 2022 (16.0), compatibility level 160.
- **Restored as:** `InpremInspection`, in a throwaway local SQL Server 2022 container that was deleted afterwards. LocalDB on this machine is SQL Server 2019, which cannot restore a 2022 backup.

## 01 — Environment

| Item | Value |
|---|---|
| Hosted server (from the backup header) | SQL Server 2022 (16.0.1000), server `sql9001` |
| Compatibility level | 160 |
| Collation | SQL_Latin1_General_CP1_CI_AS |
| Recovery model (restored copy) | FULL |
| Size | 11 MB |
| `db_owner` / `ALTER ANY SECURITY POLICY` | Yes / yes **on the restored copy only** (we are `sa` there). The hosting account's real permissions are not known; check them before relying on row-level security (D5, Phase 7). |

## 02 — Migration history

**The production database is the same app at an earlier version.**

- **Applied:** 5 of the 15 migrations on `fix/login-admin-only`, the last being `20260928094128_AddUniqueEmailIndexes`. `InitialCreate` was recorded by EF Core 6.0.13; the other four by 8.0.0.
- **Not applied:** the 10 migrations from `20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId` to `20261003091001_AddVolunteerContactPersonDetails`. That means no `Tenants` table, no `TenantId` columns, no billing tables, and the old `Setting` table is still there.
- **Not on the branch:** none.

So if this app goes live on the old data, the deployment first applies those 10 existing migrations (which create the "Inprem Holistic Community Resource Center" organization and file every row under it), then Phase 0's new ones.

## 03 — Row counts

| Table | Rows |
|---|---|
| AspNetUsers | 3 |
| AspNetRoles | 1 (Admin) |
| AspNetUserRoles | 3 |
| AspNetUserTokens | 2 |
| Staffs | 25 |
| Volunteers | 26 |
| ClockingsStaff | 45 |
| Clockings | 48 |
| Setting | 1 |
| __EFMigrationsHistory | 5 |
| AspNetRoleClaims, AspNetUserClaims, AspNetUserLogins | 0 |

## 04 — Data quality (pre-tenancy version, `04p`)

`04-data-quality.sql` needs `TenantId`, so `04p-data-quality-pre-tenancy.sql` was used.

| Check | Problems |
|---|---|
| Emails used by more than one account | 0 |
| Accounts without the Admin role | 0 |
| Accounts with no email | 0 |
| More than one volunteer session per person per ClockDate | 0 |
| More than one staff session per person per ClockDate | 0 |
| Sessions with ClockDate 0001-01-01 (finding F-01/F-02) | **1** |
| Staff emails used more than once | 0 |
| Volunteer emails used more than once | 0 |
| Emails registered as both staff and volunteer | 0 |
| Sessions still open from before the backup day | 0 |
| Closed sessions with no WorkingHours | 0 |
| Clock out before clock in, or break end before break start | **1** |
| Stored WorkingHours differs from (out − in) − break by more than 1 minute | **3** (the same three sessions below) |

**The three odd sessions** are all from 26–29 September 2026, while the September fixes were being made. They're described by kind and date only:

1. **A staff session clocked out about 2½ days after clocking in.** Its working hours are stored as 23:59:59, the most the column can hold; this is the clamp in `ClockOut`.
2. **A volunteer session whose clock-out is earlier than its clock-in** (08:41 in, 04:42 out, same day), stored as 0 hours. It looks like an AM/PM slip, or a mix of local and UTC times.
3. **A volunteer session with ClockDate 0001-01-01**, stored as 23:59:59. It has the same clock-in as session 1, so it's probably a copy made by the Move button (findings F-01/F-02) that lost its date. **F-01/F-02 has already happened in production.**

**What this means for Phase 0:**

- **Unique emails (step 0.4, M3) will apply cleanly:** no shared emails.
- **Composite keys (step 0.5, M4) will apply cleanly:** there is a single organization, so no session can be under the wrong one.
- **Memberships (step 0.3, M2) will cover everyone:** all 3 accounts are admins and will become admins of the one organization. Someone must name the Owner, because "earliest admin" cannot be worked out (no creation date).
- **The three odd sessions do not block any migration.** Each one inflates or deflates hour totals (session 3 alone adds 23h59 to September volunteer hours under year 0001). The project owner decides whether to correct them through the app's clocking Edit page. Nothing is changed by G0.

## 05 — Report baseline (pre-tenancy version, `05p`)

These totals are the "before" figures. After the 10 existing migrations, `05-report-baseline.sql` must show the same totals under the house organization.

**R1 — staff hours by month**

| Year-Month | Sessions | Open | Worked minutes |
|---|---|---|---|
| 2026-09 | 45 | 0 | 24,164 (402h 44m) |

**R2 — volunteer hours by month**

| Year-Month | Sessions | Open | Worked minutes |
|---|---|---|---|
| 0001-01 (the session with no date) | 1 | 0 | 1,439 |
| 2026-09 | 46 | 0 | 22,797 (379h 57m) |
| 2026-10 | 1 | 0 | 6 |

**R3 — dashboard figures**

| Staff | Volunteers | Admins | Staff sessions | Volunteer sessions | Staff worked minutes (all time) |
|---|---|---|---|---|---|
| 25 | 26 | 3 | 45 | 48 | 24,164 |

The data runs from 2026-09-01 to 2026-10-01. For 92 of the 93 sessions, ClockDate agrees with the clock-in date read either way, UTC or local.

## Open question: are the stored times UTC or Columbus local time?

This app stores times in UTC and converts at the edges. `scripts/convert-existing-timestamps-to-utc.sql` is the one-time conversion for older rows. From the data alone it's impossible to tell whether production's rows are already in UTC:

- Almost all staff clock-ins are stored in the 12:00 hour (44 of 45) and almost all volunteer clock-ins in the 13:00 hour (44 of 48). Most clock-outs are stored at 20:00–21:59 (81 sessions); 11 are at 04:xx (the 29 September fixes) and 1 at 23:xx.
- **Read as UTC**, that is roughly 08:00–09:00 to 16:00–17:00 Columbus time: a normal working day. **This reading is the more likely one.**
- **Read as local**, it is roughly noon to 20:00–21:00.
- `db-backups/inprem-data.sql`, the September import, has the same stored hours, so the import did not shift anything.

**The developer should confirm:** in the old app, do these September sessions show clock-ins around 8–9 am (UTC storage, nothing to convert) or around noon (local storage)? If noon, the stored times are local, and the UTC conversion must run before (or as part of) moving this data onto the new app. Otherwise every time would show 4–5 hours off.
