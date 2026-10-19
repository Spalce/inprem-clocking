# Migration scripts

Migrations are never applied when the app starts (decision D12). Every migration is shipped here as reviewed SQL:

- an idempotent **up** script;
- an idempotent **down** script;
- **verification** queries (read-only).

Whoever deploys runs them, in order, before the new build goes live. Back up the database first.

| Order | Migration | Context | Up | Down | Verify | Notes |
|---|---|---|---|---|---|---|
| 1 | `20261013080438_M1a_DataProtectionKeys` (Phase 0, step 0.2) | `PlatformDbContext` | `platform-M1a-up.idempotent.sql` | `platform-M1a-down.idempotent.sql` | `platform-M1a-verify.sql` | Additive: schema `platform`, its own `__EFMigrationsHistory`, table `DataProtectionKeys`. **Must be applied before the step 0.2 build is deployed**: the app reads and writes its cookie keys there. |
| 2 | `20261013083152_M2_TenantMemberships` (Phase 0, step 0.3) | `PlatformDbContext` | `platform-M2-up.idempotent.sql` | `platform-M2-down.idempotent.sql` | `platform-M2-verify.sql` | Additive. Creates `platform.TenantMemberships`, with foreign keys to `dbo.AspNetUsers` (cascade) and `dbo.Tenants`. **Backfills** one Active membership per account that has an organization; the only admin of an organization becomes Owner, and organizations with several admins get Admins only (verify lists them, so someone can name the Owner). **Must be applied before the step 0.3 build is deployed**: without a membership, an admin is signed out. Down drops the table, and memberships with it. |
| 3 | `20261013161410_M3_PlatformUniqueEmail` (Phase 0, step 0.4) | `ApplicationDbContext` (dbo, the approved app's own history table) | `app-M3-up.idempotent.sql` | `app-M3-down.idempotent.sql` | `app-M3-verify.sql` | Makes `AspNetUsers.EmailIndex` unique (non-null emails): one account per email platform-wide. **Guard:** stops with error 50003 and changes nothing if two accounts share an email (list them with `scripts/inspection/04-data-quality.sql`, result 2). Down restores the non-unique index. |
| 4 | `20261013175341_M4_CompositeTenantKeys` (Phase 0, step 0.5) | `ApplicationDbContext` | `app-M4-up.idempotent.sql` | `app-M4-down.idempotent.sql` | `app-M4-verify.sql` | Alternate keys `(TenantId, StaffId)` and `(TenantId, VolunteerId)`. The session foreign keys become composite, so a session can only point at a person of its own organization; delete still cascades. **Guard:** stops with error 50004 and changes nothing if a session already belongs to another organization than its person (`scripts/inspection/04-data-quality.sql`, result 4). Down restores the single-column keys. |
| 5 | `20261013185539_M1b_ProviderSecurity` (Phase 0, step 0.7) | `PlatformDbContext` | `platform-M1b-up.idempotent.sql` | `platform-M1b-down.idempotent.sql` | `platform-M1b-verify.sql` | Additive (part of design M1, "platform tables"; runs after M2 in the platform history). Tables `ProviderUsers`, `ProviderInvitations`, `RecoveryRecords`, `AuditEntries`, `UsedCodes`, `SecurityAttempts`; foreign keys from provider users and invitations to `dbo.AspNetUsers` (no cascade). Trigger `TR_AuditEntries_AppendOnly`: any UPDATE or DELETE of an audit entry fails (error 50010). Codes and link tokens are stored only as SHA-256 hashes. **Must be applied before the step 0.7 build is deployed** (the Provider pages and the `provider` commands use these tables). Down drops the tables, and the audit log with them: export it first if it matters. |
| 6 | Hangfire 1.8.14 job storage (Phase 0, step 0.8) | Hangfire's own schema versioning (`[hangfire].[Schema]`), not EF | `hangfire-up.idempotent.sql` | `hangfire-down.idempotent.sql` | `hangfire-verify.sql` | Additive: schema `hangfire`, 11 tables (schema version 9). Generated from Hangfire.SqlServer's own installer; the app never creates or upgrades these tables (`PrepareSchemaIfNecessary = false`, D12). **Must be applied before the step 0.8 build runs a job server** (`Jobs:RunServer = true`) or anyone opens the jobs dashboard. Regenerate and review it whenever the Hangfire.SqlServer package version changes. Down deletes every job and its history. |
| 7 | `20261014204227_M1c_Plans` (Phase 1, step 1.3) | `PlatformDbContext` | `platform-M1c-up.idempotent.sql` | `platform-M1c-down.idempotent.sql` | `platform-M1c-verify.sql` | Additive: `platform.Plans` (unique `Code`) and `platform.PlanPrices`. Not part of the Phase 0 bundle; Phase 1 gets its own bundle at the end of the phase. Down drops both tables, and any plans created in the console with them. |
| 8 | `20261014210206_M1d_Organizations` (Phase 1, step 1.2) | `PlatformDbContext` | `platform-M1d-up.idempotent.sql` | `platform-M1d-down.idempotent.sql` | `platform-M1d-verify.sql` | Additive + backfill. Creates `platform.TenantProfiles` (unique slug, console status), `platform.TenantNotes` and `platform.Subscriptions` (one per organization; foreign keys to `dbo.Tenants`, `platform.Plans` and `PlanPrices`). Adds `TenantMemberships.LastSignInUtc`. **Backfills** a profile for every existing organization: slug `org-{Id}` (editable in the console), status Suspended where `Tenants.IsActive` = 0. **Must be applied before the step 1.2 build** (every admin sign-in records `LastSignInUtc`). Down drops the three tables (notes and subscriptions with them) and the column. |
| 9 | `20261014215921_M1e_SupportSessions` (Phase 1, step 1.5) | `PlatformDbContext` | `platform-M1e-up.idempotent.sql` | `platform-M1e-down.idempotent.sql` | `platform-M1e-verify.sql` | Additive: `platform.SupportSessions` (hand-off token stored as a hash, unique), foreign key to `dbo.Tenants`. **Must be applied before the step 1.5 build** (the organization page lists active support sessions). Down drops the table and the session history with it (the audit entries stay). |
| 10 | `20261015074938_M1f_PayPalBilling` (Phase 2, step 2.1) | `PlatformDbContext` | `platform-M1f-up.idempotent.sql` | `platform-M1f-down.idempotent.sql` | `platform-M1f-verify.sql` | Additive + backfill. New subscription columns (paid period, cancel at period end, outstanding balance, PayPal subscription id (unique), status date, free-access and people-limit overrides), `Plans.PayPalProductId`; tables `platform.CheckoutSessions`, `Payments`, `BillingEvents` (foreign keys to `dbo.Tenants`). **Backfills** a Free subscription for every organization that has none (D26: the house organization and organizations created before the console), so nothing changes for them when billing is enforced. **Must be applied before the step 2.1 build.** Down drops the three tables and the new columns; the backfilled Free subscriptions stay (they fit the M1d schema). |
| 11 | `20261015094743_M1g_CheckoutPlanChanges` (Phase 2, step 2.4) | `PlatformDbContext` | `platform-M1g-up.idempotent.sql` | `platform-M1g-down.idempotent.sql` | `platform-M1g-verify.sql` | Index change only: `IX_CheckoutSessions_ExternalSubscriptionId` is no longer unique, because a plan change is a new checkout on the same PayPal subscription. One PayPal subscription per organization stays enforced by the unique index on `Subscriptions`. **Must be applied before the step 2.4 build.** Down makes the index unique again and fails while plan-change checkouts exist (verify's `SharedSubscriptionIds` > 0). |
| 12 | `20261015170549_M5_LegacyBillingMoved` (Phase 2, step 2.9) | `PlatformDbContext` | `platform-M5-up.idempotent.sql` | `platform-M5-down.idempotent.sql` | `platform-M5-verify.sql` | Data only, no schema change. Carries the old billing rows into the platform tables: paid old invoices become offline payments (`legacy-invoice:{number}`); an old subscription with a price that is Active or PastDue, whose platform subscription is still the M1f Free backfill, becomes **Invoiced** (still Active, so nothing is blocked) with its old period; every old subscription with a price gets one organization note with its old terms. Everything else stays in the old tables (verify: `LeftForReview`, `OtherInvoices`). The old tables are dropped only by M6 (Phase 7). **Apply before the step 2.9 build** (new organizations no longer get an old billing row). Down returns the moved subscriptions to Free and removes the notes and payments M5 added. |
| 13 | `20261015175927_M1h_PublicApi` (Phase 3, step 3.1) | `PlatformDbContext` | `platform-M1h-up.idempotent.sql` | `platform-M1h-down.idempotent.sql` | `platform-M1h-verify.sql` | Additive: `platform.ApiTokens` (website API tokens, stored as SHA-256 hashes, unique) and `platform.IdempotencyRecords` (key per endpoint and caller, the first answer, 24 hours). **Apply before the step 3.1 build.** Down drops both tables (tokens stop working; stored answers are lost). |
| 14 | `20261015194018_M1i_Signups` (Phase 3, step 3.3) | `PlatformDbContext` | `platform-M1i-up.idempotent.sql` | `platform-M1i-down.idempotent.sql` | `platform-M1i-verify.sql` | Additive: `platform.Signups` (one per organization registered on the website: owner, mode, price, verification status, resend count), foreign keys to `dbo.Tenants` and `platform.PlanPrices`. **Apply before the step 3.3 build.** Down drops the table (the organizations stay; their signup history is lost). |
| 15 | `20261015203907_M1j_BillingAccessCodes` (Phase 3, step 3.5) | `PlatformDbContext` | `platform-M1j-up.idempotent.sql` | `platform-M1j-down.idempotent.sql` | `platform-M1j-verify.sql` | Additive: `platform.BillingAccessCodes` (one-time codes of the emailed billing links, stored as SHA-256 hashes, 15 minutes, single use), foreign key to `dbo.Tenants`. **Apply before the step 3.5 build.** Down drops the table (open links stop working). |
| 16 | `20261016022134_M1k_Invoices` (Phase 4, step 4.1) | `PlatformDbContext` | `platform-M1k-up.idempotent.sql` | `platform-M1k-down.idempotent.sql` | `platform-M1k-verify.sql` | Additive: `platform.Invoices` and `platform.InvoiceLines` (numbers from the new sequence `platform.InvoiceNumbers`, starting at 100001; one invoice that is not cancelled per subscription period), `InvoiceId` and `Method` on `platform.Payments`, foreign keys to `dbo.Tenants` and the platform tables. **Apply before the step 4.1 build.** Down drops the invoice tables, the sequence and the two payment columns (invoices and offline methods are lost). |
| 17 | `20261016063325_M1l_PaymentRefunds` (Phase 4, step 4.2) | `PlatformDbContext` | `platform-M1l-up.idempotent.sql` | `platform-M1l-down.idempotent.sql` | `platform-M1l-verify.sql` | Additive: `platform.PaymentRefunds` (one row per refund, unique PayPal refund id), foreign keys to `platform.Payments` and `dbo.Tenants`. Data: each payment refunded before (Phase 2 webhooks kept only a total) gets one row with that total; re-runnable. **Apply before the step 4.2 build.** Down drops the table (the payments keep their refunded totals). |
| 18 | `20261019224539_M1m_Notifications` (Phase 5, step 5.1) | `PlatformDbContext` | `platform-M1m-up.idempotent.sql` | `platform-M1m-down.idempotent.sql` | `platform-M1m-verify.sql` | Additive: `platform.Notifications` (the outbox: one row per email or text, queued, sent, delivered, failed or skipped with its reason; the same event never reaches the same person twice on one channel), `platform.ConsentRecords` (append-only consent history per phone number: grants with their exact wording, STOP and START), `platform.NotificationTemplates` (wordings edited in the console) and `platform.TenantMessagingSettings` (texts on or off and the monthly limit per organization); foreign keys to `dbo.Tenants` from the outbox and the settings. No data. **Rehearsal (2026-10-19, local SQL Server):** after the Phase 0 to 4 bundles, up, up again, down, down again, up; verify after up `1|4|2|2|0|0`. **Local database (2026-10-19):** applied; app data unchanged (fingerprint). The before-backup failed (a path mistake), so a verified after-backup was taken: `DB_A65635_inpremdb_after_M1m_20261019.bak`. |

## All of Phase 0 at once (the bundles)

For a deployment, use the guarded bundles instead of the six scripts one by one:

```powershell
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase0-up.sql     # ends with "Phase 0 schema complete."
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase0-down.sql   # rollback R2; ends with "Phase 0 rolled back"
```

- **Generated:** `build-phase0-bundle.ps1` builds them from the six reviewed scripts below, in table order. Never edit the bundles; edit a script and rebuild. CI fails if the committed bundles are out of date.
- **Guards:**
  - SQLCMD mode is required (SSMS: Query > SQLCMD Mode), and the bundle stops at the first error.
  - It refuses a database that isn't at the approved app's last migration ("wrong database?"), or that has migrations it doesn't know.
  - At the end it checks the expected state.
- **Rehearsal:** the whole sequence was rehearsed with timings: `docs/multitenancy/rehearsal-phase0.md`, `scripts/rehearsal/rehearse-phase0.ps1`.
- **Deployment procedure:** `docs/multitenancy/deployment-phase0.md`.

## All of Phase 1 at once (the bundles)

After the Phase 0 bundle, Phase 1 adds platform migrations M1c, M1d and M1e (rows 7 to 9):

```
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase1-up.sql     # ends with "Phase 1 schema complete."
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase1-down.sql   # ends with "Phase 1 rolled back"
```

- **Generated:** `build-phase1-bundle.ps1`, same rules and guards as the Phase 0 bundle. CI checks they are up to date.
- **Guards:** it refuses a database without the complete Phase 0 schema (error 50030), with app migrations it does not know (50031), or with platform migrations it does not know (50032).
- **Down** deletes plans, prices, organization profiles, notes, subscriptions and support sessions; the Phase 0 tables and the audit log stay. Phase 0's down bundle refuses to run while Phase 1 is still applied (it does not know those migrations): roll back Phase 1 first.
- **Rehearsal (2026-10-15, LocalDB, synthetic data, two organizations):** without Phase 0 → refused (50030). After `phase0-up`: up, up again, down, down again, up: each ended as expected, and verify gave `M1c=1|2|1|0|0 M1d=1|3|1|0|0 M1e=1|1|1|0` after up and all zeros after down. An unknown platform migration → refused (50032). Then `phase1-down` and `phase0-down` returned it to the approved baseline.

## All of Phase 2 at once (the bundles)

After the Phase 0 and Phase 1 bundles, Phase 2 adds platform migrations M1f, M1g and M5 (rows 10 to 12):

```
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase2-up.sql     # ends with "Phase 2 schema complete."
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase2-down.sql   # ends with "Phase 2 rolled back"
```

- **Generated:** `build-phase2-bundle.ps1`, same rules and guards as the earlier bundles. CI checks they are up to date.
- **Guards:** it refuses a database without the complete Phase 0 and Phase 1 schema (error 50040), with app migrations it does not know (50041), or with platform migrations it does not know (50042).
- **Down** removes checkouts, payments, PayPal events and the PayPal fields of subscriptions, and returns the moved old billing rows to Free. The audit log stays. The Phase 1 down bundle refuses to run while Phase 2 is still applied.
- **Rehearsal (2026-10-15, LocalDB, synthetic data):**
  - Without Phase 1 → refused (50040).
  - After `phase0-up` and `phase1-up`: up, up again, down, down again, up; each ended as expected. Verify after up: `M1f=1|3|2|0|2 M1g=1|0|0 M5=1|1|1|1|1|1|0|0`; after down all zeros.
  - The M5 scripts alone, twice in each direction, also passed.
  - An unknown platform migration → refused (50042). Then `phase2-down`, `phase1-down` and `phase0-down` returned it to the approved baseline.
  - The first run found that a second down failed: SQL Server compiles a skipped statement too, and M5's named columns that no longer existed. M5's SQL now runs through `EXEC`, so it is compiled only when it runs.

## All of Phase 3 at once (the bundles)

After the Phase 0 to 2 bundles, Phase 3 adds platform migrations M1h, M1i and M1j (rows 13 to 15):

```
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase3-up.sql     # ends with "Phase 3 schema complete."
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase3-down.sql   # ends with "Phase 3 rolled back"
```

- **Generated:** `build-phase3-bundle.ps1`, same rules and guards as the earlier bundles. CI checks they are up to date.
- **Guards:** refuses a database without the complete Phase 0 to 2 schema (50050), with app migrations it does not know (50051), or with platform migrations it does not know (50052).
- **Down** removes website API tokens, stored idempotent answers, signups and billing-access codes; organizations and the audit log stay.
- **Rehearsal (2026-10-15, LocalDB):** without Phase 2 → refused (50050). After the Phase 0 to 2 bundles: up, up again, down, down again, up; each ended as expected. Verify after up: `M1h=1|2|1|0|0 M1i=1|1|1|0|0 M1j=1|1|1|0`. An unknown platform migration → refused (50052). Then the four down bundles back to the approved baseline. The local database ran it as a no-op (app data unchanged).

## All of Phase 4 at once (the bundles)

After the Phase 0 to 3 bundles, Phase 4 adds platform migrations M1k and M1l (rows 16 and 17):

```
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase4-up.sql     # ends with "Phase 4 schema complete."
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase4-down.sql   # ends with "Phase 4 rolled back"
```

- **Generated:** `build-phase4-bundle.ps1`, same rules and guards as the earlier bundles. CI checks they are up to date.
- **Guards:** refuses a database without the complete Phase 0 to 3 schema (50060), with app migrations it does not know (50061), or with platform migrations it does not know (50062).
- **Down** removes invoices, invoice lines, refund rows, the invoice number sequence and the payments' invoice and method columns; payments with their refunded totals and the audit log stay.
- **Rehearsal (2026-10-16, LocalDB):** without Phase 3 → refused (50060). After the Phase 0 to 3 bundles: up, up again, down, down again, up; each ended as expected. Verify after up: `M1k=1|2|1|4|2|0 M1l=1|1|2|0|0`. An unknown platform migration → refused (50062). Then the five down bundles back to the approved baseline.
- **Local database (2026-10-16):** backup `DB_A65635_inpremdb_before_phase4_bundle_20261016.bak` verified first; then `phase4-up.sql` applied M1l (M1k was already there); verify as above; app data unchanged (fingerprint).

## Running

```powershell
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1a-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1a-verify.sql   # expect 1|1|<rows>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M2-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M2-verify.sql   # expect 1|<n>|0|<orgs without owner>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/app-M3-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/app-M3-verify.sql        # expect 1|1|0
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/app-M4-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/app-M4-verify.sql        # expect 1|2|2|0
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1b-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1b-verify.sql  # expect 1|6|1|2|<owners>|<open recoveries>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/hangfire-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/hangfire-verify.sql      # expect 9|11|<servers>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1c-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1c-verify.sql  # expect 1|2|1|<plans>|<active prices>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1d-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1d-verify.sql  # expect 1|3|1|0|<subscriptions>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1e-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1e-verify.sql  # expect 1|1|1|<open sessions>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1f-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1f-verify.sql  # expect 1|3|2|0|<free subscriptions>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1g-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1g-verify.sql  # expect 1|0|<shared>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M5-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M5-verify.sql  # see the script's header for the expected counts
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1h-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1h-verify.sql  # expect 1|2|1|<tokens>|<records>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1i-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1i-verify.sql  # expect 1|1|1|<pending>|<verified>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1j-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1j-verify.sql  # expect 1|1|1|<unused codes>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1k-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1k-verify.sql  # expect 1|2|1|4|2|<invoices>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1l-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1l-verify.sql  # expect 1|1|2|0|<refunds>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1m-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1m-verify.sql  # expect 1|4|2|2|<queued>|<opt-outs>
```

Always pass `-I` (`QUOTED_IDENTIFIER ON`). `sqlcmd` turns it off by default, and statements touching `dbo.AspNetUsers` fail without it, because that table has filtered indexes.

The scripts are safe to run twice. Down reverses up. All rehearsals ran on 2026-10-13 against throwaway LocalDB databases.

- **M1a:** verify gave before `0|0`, after up `1|1|0`, after a second up `1|1|0`, after down `0|0`, after a second down `0|0`.
- **M3:** the database had the approved app's 15 migrations and two accounts sharing an email. Verify gave before `0|0|1`. Up was refused (error 50003, exit 1) and verify was still `0|0|1`. After renaming one account, up gave `1|1|0`, a second up the same, and down `0|0|0`.
- **M4:** the database had migrations up to M3 and one staff session filed under another organization than its person. Verify gave before `0|0|0|1`. Up was refused (error 50004, exit 1) and verify was unchanged. After correcting the session, up gave `1|2|2|0`, and a second up the same. A new crossed session was then rejected by the database (`FK_ClockingsStaff_Staffs_TenantId_StafId`). Down gave `0|0|0|0`.
- **M1j:** on a database at M1i, verify gave before `0|0|0|NULL`; after up `1|1|1|0`; a second up the same; after down the before values; a second down the same; up again `1|1|1|0`.
- **M1i:** on a database at M1h, verify gave before `0|0|0|NULL|NULL`; after up `1|1|1|0|0`; a second up the same; after down the before values; a second down the same; up again `1|1|1|0|0`.
- **M1h:** on a database with the Phase 0 to 2 bundles, verify gave before `0|0|NULL|NULL|NULL`; after up `1|2|1|0|0`; a second up the same; after down the before values; a second down the same; up again `1|2|1|0|0`.
- **M5:** on a database at M1g with three synthetic organizations (paying and Active with one paid and one void invoice; paying and Suspended; zero amount), verify gave before `0|2|0|0|1|0|2|1`; after up `1|2|1|2|1|1|1|1` (the Active one moved to Invoiced, the Suspended one left for review, both noted, one payment); a second up the same; down back to the before values; a second down the same; up again the same.
- **M1g:** on a database at M1f, verify gave before `0|1|0`; after up `1|0|0`; after a second up the same; after down `0|1|0`; after a second down the same; up again `1|0|0`.
- **M1f:** on a database with the Phase 0 and Phase 1 bundles and two synthetic organizations (plus the house organization), verify gave before `0|0|0|NULL|NULL`; after up `1|3|2|0|3`; after a second up the same; after down `0|0|0|NULL|NULL`; after a second down the same; up again `1|3|2|0|3`. All three organizations had a Free, Active subscription with a status date.
- **M1e:** on a database with all app migrations and platform migrations up to M1d, verify gave before `0|0|0|NULL`; after up `1|1|1|0`; after a second up the same; after down `0|0|0|NULL`; after a second down the same.
- **M1d:** the database had the app's migrations, two organizations (one inactive) and platform migrations up to M1c. Verify gave before `0|0|0|NULL|NULL`; after up `1|3|1|0|0` with profiles `org-1` (Active) and `org-2` (Suspended, reason recorded); after a second up the same; after down `0|0|0|NULL|NULL`; after a second down the same.
- **M1c:** on a database with all app migrations and platform migrations up to M1b, verify gave before `0|0|0|NULL|NULL`; after up `1|2|1|0|0`; after a second up the same; after down `0|0|0|NULL|NULL`; after a second down the same.
- **Hangfire:** on an empty database, verify gave before `NULL|0|NULL`; after up `9|11|0`; after a second up the same; after down `NULL|0|NULL`; after a second down the same; after up again `9|11|0`. The integration tests also install it from this script before starting a job server.
- **M1b:** the database had the app's migrations and platform M1a and M2. Verify gave before `0|0|0|0`; after up `1|6|1|2|0|0`; after a second up the same. An UPDATE of an audit entry failed with the append-only error. Down gave `0|0|0|0`, a second down the same, and the platform history kept M1a and M2.
- **M2:** the database had the app's migrations, two organizations (one admin; two admins) and one operator account without an organization. Verify gave before `0`; after up `1|3|0|1`, listing the organization with 2 admins; after a second up the same; after down `0`; after up again `1|3|0|1`.

## What the step 0.2 deployment changes for users

- **Everyone is signed out once.** The sign-in cookie is renamed (`.AspNetCore.Identity.Application` → `.Inprem.App`), and the keys that protect it move from `App_Data/DataProtection-Keys` (files) to `platform.DataProtectionKeys`. Old cookies can no longer be read, so each user signs in again once. Nothing else changes for them.
- **Configuration per site:**
  - `Site:SiteMode` must be set. `appsettings.json` has `App`.
  - `Site:AppHost` / `Site:AdminHost` restrict each site to its hostname. They are empty in `appsettings.json` (any host), so set them per environment at the hostname cutover (`docs/multitenancy/build/09-domain-cutover.md`).
- **The old key files in `App_Data/DataProtection-Keys` are no longer used.** They can be deleted from the server after the deployment.

## What the step 0.8 deployment changes

- **The old daily billing sweep stops.** `BillingBackgroundService` is no longer registered: nothing renews, marks past due or suspends organizations automatically until Phase 2 replaces billing. The Platform pages still work.
- **Configuration per site:**
  - **App site:** `Jobs:RunServer = true` (it is `false` in `appsettings.json`). With nothing scheduled yet, the server just waits.
  - **Admin site:** must keep `false`; it refuses to start otherwise.
  - **Idling:** on SmarterASP, stop the site from idling (D11): turn on the host's always-on setting, or set up an uptime ping, so jobs keep running.
- **Jobs dashboard:** read-only, for Provider Owners signed in with two-factor. It is at `/jobs` on the Admin site, or `/Provider/jobs` with `Site:SiteMode = All`.

## Regenerating

Use EF Core 8 tools, not the 7.0.2 version pinned in `InpremClockingApp/.config/dotnet-tools.json` (see `scripts/inspection/README.md`):

```powershell
cd src/Inprem.Platform.Infrastructure
& $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations script 0 M1a_DataProtectionKeys --idempotent --context PlatformDbContext -o ../../scripts/migrations/platform-M1a-up.idempotent.sql
& $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations script M1a_DataProtectionKeys 0 --idempotent --context PlatformDbContext -o ../../scripts/migrations/platform-M1a-down.idempotent.sql
& $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations script M2_TenantMemberships M1b_ProviderSecurity --idempotent --context PlatformDbContext -o ../../scripts/migrations/platform-M1b-up.idempotent.sql
& $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations script M1b_ProviderSecurity M2_TenantMemberships --idempotent --context PlatformDbContext -o ../../scripts/migrations/platform-M1b-down.idempotent.sql
```
