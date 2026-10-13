# Database inspection scripts (read-only)

These scripts answer the questions Phase 0 depends on. Is the database on the expected migration? Do two accounts share an email? Are there sessions filed under the wrong organization? What are the report totals to compare before and after each migration?

They only read. Each one also refuses to start unless you pass `-v ConfirmLocalCopy=YES`.

> **Run them against a restored local copy only.** Never run them against the hosted database or any shared server. Take a backup from the hosting control panel and restore it on your own machine first.

| Script | What it shows | Safe to paste into `inspection-results.md`? |
|---|---|---|
| `01-environment.sql` | SQL Server version, database, compatibility level, recovery model, size, `db_owner`, `ALTER ANY SECURITY POLICY` permission | Yes |
| `02-migration-history.sql` | Applied app migrations vs this branch's: the approved app's 15, plus Phase 0's `M3_PlatformUniqueEmail` and `M4_CompositeTenantKeys`. A database that has not had the Phase 0 deployment shows those as missing; that is expected. | Yes |
| `03-row-counts.sql` | Row count of every table | Yes |
| `04-data-quality.sql` | Result 1: a summary of counts only. Results 2–6: detail with ids and emails | **Result 1 only.** Summarize the detail; never paste emails |
| `05-report-baseline.sql` | Report totals R1–R3 (staff hours per org per month, volunteer hours per org per month, dashboard figures) | Yes (counts and totals only) |
| `04p-data-quality-pre-tenancy.sql` | Like 04, for a database **before** the multi-tenancy migrations (no `TenantId`). Adds checks on session times and hours | **Result 1 only** |
| `05p-report-baseline-pre-tenancy.sql` | Like 05 for a pre-tenancy database, plus T/T2: whether stored times look like UTC or local time | Yes |

Use 02 first: if `TenantId` does not exist yet (last migration `20260928094128_AddUniqueEmailIndexes` or earlier), run `04p`/`05p` instead of `04`/`05`. The production backup of 2026-10-05 is such a database; see `docs/multitenancy/inspection-results.md`.

**A SQL Server 2022 backup cannot be restored on LocalDB 2019.** Restore it in a throwaway SQL Server 2022 container instead (`mcr.microsoft.com/mssql/server:2022-latest`, `docker cp` the `.bak` in, then `RESTORE DATABASE ... WITH MOVE`), and delete the container afterwards.

The scripts are also run by the automated suite against the seeded test database (`tests/InpremClockingApp.Tests.Baseline/Inspection`), so they stay correct as the schema changes.

## 1. Restore the backup locally

The backup is a `.bak` file from the hosting control panel. Keep it outside the repository; `db-backups/` is git-ignored if you keep it there.

LocalDB (Windows), from a terminal:

```powershell
sqllocaldb start MSSQLLocalDB
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "RESTORE FILELISTONLY FROM DISK = N'C:\path\to\backup.bak'"
```

The second command lists the logical file names (for example `DB_A65635_inpremdb` and `DB_A65635_inpremdb_log`). Use them in the restore:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "RESTORE DATABASE [InpremInspection] FROM DISK = N'C:\path\to\backup.bak' WITH MOVE N'<data logical name>' TO N'C:\path\to\InpremInspection.mdf', MOVE N'<log logical name>' TO N'C:\path\to\InpremInspection_log.ldf', RECOVERY"
```

Restoring under a new name (`InpremInspection`) makes it obvious which database you are looking at.

## 2. Run the scripts

From the repository root:

```powershell
foreach ($s in Get-ChildItem scripts/inspection/*.sql | Sort-Object Name) {
  sqlcmd -S "(localdb)\MSSQLLocalDB" -E -d InpremInspection -v ConfirmLocalCopy=YES -W -s "|" -i $s.FullName -o "$env:TEMP\$($s.BaseName).txt"
}
```

The output files are written to your temp folder, not the repository, because `04` contains personal data. If you prefer SSMS, turn on **Query > SQLCMD Mode** and add `:setvar ConfirmLocalCopy YES` at the top.

## 3. Record the results

Copy the safe parts (see the table above) into `docs/multitenancy/inspection-results.md`:

- the date of the backup;
- `01` in full;
- `02` (summary row plus any missing or extra migrations);
- `03` row counts;
- `04` result 1 only;
- `05` in full.

For any non-zero problem count in `04`, write one line saying what it is, without names or emails, for example "2 emails shared by accounts in two organizations". The project owner decides how to resolve each one before Phase 0 step 0.4 (unique emails) and step 0.5 (composite keys).

Once `inspection-results.md` is written, delete the restored database and the output files:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "DROP DATABASE [InpremInspection]"
```

## Model drift check

`dotnet ef migrations has-pending-model-changes` tells you whether the C# model has changes no migration covers. It exists in **EF Core 8** tools, not 7.

`InpremClockingApp/.config/dotnet-tools.json` still pins `dotnet-ef` **7.0.2**, and that pin wins inside `InpremClockingApp/`. Until it is updated (a Phase 0 follow-up), install EF 8 tools outside the repository and call them by path:

```powershell
dotnet tool install dotnet-ef --version 8.0.31 --tool-path $env:USERPROFILE\.dotnet\ef8
cd InpremClockingApp
& $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations has-pending-model-changes
```

Expected today: `No changes have been made to the model since the last migration.` (checked 2026-10-13 on `1061ea8`). From Phase 0 on there are two contexts; run the check for each one with `--context`.
