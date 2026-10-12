# 09 — Domain cutover: inpremclockin.org → app. / admin.

**When:** any time after Phase 0 exit checks pass (hostnames work from then on). The root-domain switch to the marketing website happens when the web team's site is ready.
**Entry:** C3-11 and D7–D9, D17 in `DECISIONS.md`; DNS and TLS for `app.inpremclockin.org` and `admin.inpremclockin.org` (and test equivalents); hosting for two sites (or one site with both hostnames, `SiteMode=All`).
**Design references:** sections 2.1–2.5, 10.3 (C3-11), 11.
**Branch:** `mt/domain-cutover` (configuration, redirects and runbook only).

Follow `00-guardrails.md`. Claude Code prepares code, configuration and runbooks; **the developer performs DNS changes and deployments**.

## Step C.1 — Configuration per environment

- Document and template (no secrets) the settings for each site and environment: `Site:SiteMode`, `Site:AppHost`, `Site:AdminHost`, `Site:PublicWebsiteOrigins`, email link bases (`Links:AppBaseUrl`, `Links:AdminBaseUrl`, `Links:WebsiteBaseUrl`), `Jobs:RunServer`, `AdminSite:AllowedNetworks`, PayPal return/cancel allow-list (website origins), Twilio callback URLs (`https://app.inpremclockin.org/webhooks/twilio/...`), PayPal webhook URL (`https://app.inpremclockin.org/webhooks/paypal`).

## Step C.2 — Interim root handling

- Until the website takes over, the root `inpremclockin.org` keeps serving the approved app unchanged (same site bound to both hostnames, `SiteMode=App`).
- Add an optional `LegacyRedirect` middleware (off by default) that, when enabled, answers requests on the root host for known application paths (`/backoffice`, `/BackOffice`, `/Identity/*`, `/StaffAttendance`, `/VolunteerAttendance`, `/staff-clockin/*`, `/volunteer-clockin/*`, `/Staff*`, `/Volunteer*`, `/User`, `/HoursWorked`, `/api/controls/*`, `/api/people/*`, other existing routes enumerated from the endpoint table) with **301** to the same path and query on `https://app.inpremclockin.org`. Unknown paths are left for the website.
- Provide the same list as redirect rules for the web team's hosting (generic format plus nginx/Apache/IIS examples) in `docs/multitenancy/runbooks/root-redirects.md`.

## Step C.3 — Runbook

`docs/multitenancy/runbooks/domain-cutover.md`:
1. Create DNS records and certificates for `app.` and `admin.` (staging first).
2. Deploy the same build to the App site (bound to `app.` and still to the root) and the Admin site (bound to `admin.`, allow-list on).
3. Smoke tests: baseline suite against `app.`; host-isolation suite; provider login on `admin.`.
4. Update kiosk devices and bookmarks to `app.inpremclockin.org`; notify the existing organization (message template included).
5. When the web team is ready: point the root to the website; enable redirects (their hosting or `LegacyRedirect`); verify each old path returns 301 to `app.`.
6. Rollback: point the root back to the App site; disable redirects.

## Acceptance

| Check | Measure |
|---|---|
| App on new host | Baseline suite 100% against `https://app.inpremclockin.org` (staging, then production) |
| Admin isolated | Host-isolation suite 0 failures; allow-list blocks outside addresses |
| Redirects | Every enumerated old path returns 301 to the same path on `app.` (automated check) |
| Integrations | PayPal and Twilio webhooks reach `app.` (sandbox/test) |
