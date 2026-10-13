# Database inspection results (G0-2)

**Status: not run yet.** Waiting for a fresh backup of the hosted database. The only backup in `db-backups/` dates from 2026-09-29, before the TenantId migrations, so it cannot answer these questions.

How to run the scripts: `scripts/inspection/README.md`. Paste only the parts that README marks as safe. Never paste names, emails or ids.

## Backup

- Taken on: _(date, from the hosting control panel)_
- Restored as: _(local server / database name)_

## 01 — Environment

_(paste result)_

## 02 — Migration history

- Last applied: _(…)_ (expected `20261003091001_AddVolunteerContactPersonDetails`)
- Missing from database: _(none / list)_
- Not on branch: _(none / list)_

## 03 — Row counts

_(paste result)_

## 04 — Data quality (summary only)

_(paste result 1)_

Problems found, one line each, without personal data:

- _(e.g. "2 emails shared by accounts in two organizations — project owner to decide")_

Owner per organization for migration M2 (the project owner names them; it cannot be derived from the data, see 04 result 6):

- _(organization id → chosen owner, recorded without the email)_

## 05 — Report baseline

_(paste R1, R2, R3; compare again after every migration)_
