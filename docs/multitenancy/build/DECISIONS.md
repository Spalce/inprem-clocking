# Decisions (defaults — edit to change)

The design listed questions for others to answer. Nobody else is approving, so each is settled here with the design's recommended default. Claude Code follows this file; if you change a line, tell Claude Code in the next session.

## Architecture and tenancy

| ID | Decision | Value |
|---|---|---|
| D1 | Layered projects for new code | Yes: Domain / Application / Infrastructure / tests |
| D2 | Accounts | One account per person (email unique across the platform) + `TenantMemberships` |
| D3 | Database contexts | Separate `PlatformDbContext` (schema `platform`) for new tables; `ApplicationDbContext` keeps existing tables |
| D4 | Tenant resolution | From the signed-in membership, re-checked every request; no tenant in the URL |
| D5 | Row-level security in SQL Server | Phase 7, only if the host allows `CREATE SECURITY POLICY`; otherwise EF filters + interceptor + composite keys |
| D6 | Background jobs | Hangfire, SQL Server storage; job server only on the App site |

## Hosting and deployment

| ID | Decision | Value |
|---|---|---|
| D7 | Hostnames | `inpremclockin.org` website; `app.inpremclockin.org` back office, kiosk, website API, webhooks; `admin.inpremclockin.org` Provider Console |
| D8 | Deployment | One codebase, same build deployed as two sites (`SiteMode` = App / Admin; `All` for local development) |
| D9 | When subdomains are not available (e.g. a test site) | `SiteMode=All`, console under `/provider` on one host |
| D10 | Website API location | `https://app.inpremclockin.org/api/public/v1` |
| D11 | Hosting | Current SmarterASP hosting is supported: no server console, so bootstrap uses the one-time setup page; keep the site from idling (host setting or uptime ping) so Hangfire runs; reconciliation jobs cover any gap |
| D12 | Migrations in deployment | Reviewed idempotent scripts run by the developer; never at startup |

## Security and accounts

| ID | Decision | Value |
|---|---|---|
| D13 | First provider owner | Bootstrap command **and** one-time setup page (`/setup` on the admin host), both requiring a one-time bootstrap code from server configuration (stored as a hash); disabled forever once an owner exists |
| D14 | Provider two-factor | Mandatory; authenticator app (Identity); SMS code via Twilio Verify as an option from Phase 5 |
| D15 | Recovery | Levels 1–4 as designed; second approver(s) listed in server configuration `Security:RecoveryApprovers`; security mailbox `Security:Mailbox` |
| D16 | Minimum provider owners before production | Two (warning until met) |
| D17 | Admin site IP allow-list | Built and configurable (`AdminSite:AllowedNetworks`); empty list = allow all (for testing); set it before production |

## Billing

| ID | Decision | Value |
|---|---|---|
| D18 | Payment provider | PayPal Subscriptions + PayPal Invoicing + manual offline payments |
| D19 | Who creates PayPal subscriptions | Our server; buyer redirected to PayPal's approve link; `custom_id` = checkout-session reference |
| D20 | Activation | Only when PayPal reports `ACTIVE` (confirm call, verified webhook or reconciliation), exactly once |
| D21 | Trial | App-managed, no card; 30 days (configurable per plan) |
| D22 | Pay before email verification (website signup) | Allowed; back-office access only after verification |
| D23 | PayPal `payment_failure_threshold` | 1 |
| D24 | Retention after Suspended/Canceled before Closed | 90 days (configurable); data export always available |
| D25 | Currency | USD |
| D26 | Inprem's own organization | Free "house" plan (BillingMode = Free); change if it should pay |
| D27 | Existing customers manage billing on the website | Yes, via emailed one-time billing links; also in the app's Billing page |
| D28 | Refund limit for Provider Billing role | Configurable `Billing:RefundLimit` (default 0 = Owner only) |

## Visible changes to the approved app (C3)

| ID | Change | Setting |
|---|---|---|
| C3-1 | Sidebar: remove old SuperAdmin links; add Billing for owners | On |
| C3-2 | Status banner include in `_MainLayout.cshtml` (renders nothing for Active) | On |
| C3-3 | Block saves when Suspended/Expired/Closed (reports and export still work) | On |
| C3-4 | Block kiosk when Suspended/Expired/Closed | On |
| C3-5 | "Choose organization" page for people with more than one membership | On |
| C3-6 | Admins page: existing email gets an invitation instead of a duplicate account | On |
| C3-7 | Login lockout (5 attempts, 15 minutes) | On |
| C3-8 | Staff/volunteer mobile number and SMS consent fields | Behind organization setting (off by default) |
| C3-9 | Kiosk session (kiosk cannot open back office; end-of-day sign-out) | Behind setting `Kiosk:SessionMode` (off by default) — Phase 7 |
| C3-10 | Re-enable anti-forgery (CSRF) | On — Phase 7, after the UI suite covers every form |
| C3-11 | Approved app served on `app.inpremclockin.org`; root paths redirect | On — at cutover |

## Website and notifications

| ID | Decision | Value |
|---|---|---|
| D29 | Demo website page (until the web team's site exists) | `wwwroot/demo-site/`, enabled by `Demo:EnableDemoSite` |
| D30 | CAPTCHA | Cloudflare Turnstile behind an `ICaptchaVerifier` interface (swap if the web team chooses another); test double in tests |
| D31 | SMS sender for the US | Verified toll-free number (start registration early); Verify for login codes |
| D32 | Staff alerts default times | Missed clock-out 21:00 local; long break 60 minutes |
| D33 | Repository visibility | Recommend making it private before billing code is merged |
