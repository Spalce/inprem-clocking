# 10 — Phase 7: Hardening and completion

**Entry:** Phases 0–6 exit checks pass (cutover can be before or after). Decisions: C3-9, C3-10, D5, D24.
**Branch:** `mt/phase7` from `mt/integration`.
**Design references:** sections 4.2, 10.3, 11.5, 11.6, 12, 13.

Follow `00-guardrails.md`. Run all required suites after every step; update `PROGRESS.md`.

## Step 7.1 — Anti-forgery (C3-10)

- Confirm the UI suite (Playwright) covers every form and every jQuery POST in the approved app. Add missing UI tests **first** and get them green on the current code.
- Remove the global `IgnoreAntiforgeryTokenAttribute`; add a new script (new file under `wwwroot/js/security/`, included by one line in `_MainLayout.cshtml` and the kiosk layout) that adds the antiforgery header to jQuery AJAX requests.
- Run the full baseline and UI suites. Any failure means a form was missed: fix by adding the token, never by excluding the page.

## Step 7.2 — Kiosk session (C3-9, behind `Kiosk:SessionMode`, off by default)

- Implement the kiosk session: admin opens the kiosk on a device; a limited kiosk cookie replaces the admin cookie; kiosk cannot reach the back office; end-of-day expiry in the tenant's time zone; close on device (admin password) or remotely from the back office or console.
- With the setting off, behaviour is exactly as today (baseline suite). With it on, add tests for every rule above.

## Step 7.3 — Row-level security (D5)

- Write `scripts/inspection/rls-feasibility.sql`: checks `@@VERSION` (SQL Server 2016+), `HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','ALTER ANY SECURITY POLICY')`, then creates a throwaway schema, table, inline predicate function using `SESSION_CONTEXT(N'TenantId')`, and a security policy with filter and block predicates; shows that tenant 1 and tenant 2 see different rows; then drops everything. Run it against a test copy of the hosted database.
- If it works: add a migration with the predicate function and security policy for all tenant-owned tables, a connection interceptor that sets `SESSION_CONTEXT('TenantId')` (read-only) and `Bypass` for the platform context; tests that a raw SQL query only returns the current tenant's rows. If it does not work: record the result in `PROGRESS.md` and keep EF filters + interceptor + composite keys.

## Step 7.4 — Contract migration (M6)

- After at least one release with M1–M5 in use: archive and drop the old `Subscriptions` and `Invoices` tables and the old per-tenant username indexes; retire `AppUser.TenantId` reads (keep the column if any approved code still reads it; list such reads).
- Full section 11 procedure: inspection, backup, rehearsal on a copy, verification, rollback script (restore from backup for destructive steps).

## Step 7.5 — Data lifecycle

- Organization data export (owner, back office): CSV of people, clockings, invoices.
- Closing: after the retention period (D24) with warning emails at 30 and 7 days, a job deletes the organization's rows across all tenant-owned tables in a transaction, writes an audit entry, and keeps billing records required for accounting.

## Step 7.6 — Security review

- Security headers (HSTS, `X-Content-Type-Options`, `Referrer-Policy`, a Content-Security-Policy that the approved pages pass).
- Dynamic scan (OWASP ZAP baseline) against a test site: 0 high findings.
- Dependency and secret scans clean; review all `IgnoreQueryFilters` uses (only in Infrastructure, each audited).
- Logs contain no passwords, tokens, full emails, phone numbers or payment payloads (automated log-scrubbing test).

## Step 7.7 — Operations

- Health checks (`/health` for app and admin; database, Hangfire, PayPal and Twilio reachability without secrets in output).
- Runbooks complete: deployment (two sites), migrations, backup/restore, rollback, bootstrap/recovery, PayPal webhook rotation, Twilio number changes.
- Final full regression: every suite in `TESTING.md`, plus `DEMO-SCRIPT.md` executed end-to-end on a test site and recorded in `PROGRESS.md`.

## Exit checks

| ID | Check |
|---|---|
| P7-1 | Anti-forgery enforced; baseline and UI suites 100% |
| P7-2 | Kiosk session: off = no change (baseline); on = all rules tested |
| P7-3 | Row-level security enabled or its infeasibility recorded |
| P7-4 | Contract migration rehearsed and applied on a test copy; old billing code and tables gone |
| P7-5 | Export and closing tested, including retention warnings |
| P7-6 | ZAP baseline 0 high; scans clean; log-scrubbing test passes |
| P7-7 | Health checks and runbooks complete; full regression and demo script pass |
