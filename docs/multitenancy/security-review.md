# Security review (Phase 7 step 7.6)

What step 7.6 checked, what it changed, and what is left. Started 2026-10-22 at the developer's request, before the Phase 0–6 exit checks that wait for PayPal, Twilio and staging.

| Part | Status |
|---|---|
| Security headers and Content-Security-Policy | Done. The policy is in `ReportOnly` mode until the test site's reports are clean |
| Reads past the organization filters (`IgnoreQueryFilters`) | Reviewed, 20 in 6 files; pinned by a test |
| Logs: no passwords, tokens, codes, full emails, phone numbers or payloads | Checked by a test; nothing found |
| Dependency and secret scans | In CI (vulnerable packages, Gitleaks). One low advisory is still to review (below) |
| OWASP ZAP baseline scan | **Not run yet:** it needs Docker Desktop and a running copy (below) |

---

## 1. Security headers

`InpremClockingApp/Hosting/SecurityHeaders.cs` adds them to every response of both sites: pages, redirects, errors, static files, the API and the console. It runs as a startup filter registered in `Hosting/SiteHosting.cs`; `Program.cs` and the frozen pages are unchanged. A header a page sets itself is kept: the console's `Referrer-Policy: no-referrer` stays.

| Header | Value | Why |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | The browser uses a file only as the type the server says |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Other sites see only the address's origin, never its path or query (where links carry codes). The console sends `no-referrer` |
| `X-Frame-Options` | `SAMEORIGIN` | No other site can show the app in a frame (click-jacking). Older browsers; `frame-ancestors` below for the others |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), payment=()` | Nothing in the app uses them; a script injected into a page can't either |
| `Strict-Transport-Security` | ASP.NET Core's default | Already set outside Development by `UseHsts` (`Program.cs` line 171); unchanged |

### Content-Security-Policy

The policy lists exactly what the pages load today:

```
default-src 'self';
script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://challenges.cloudflare.com;
style-src 'self' 'unsafe-inline' https://fonts.googleapis.com;
font-src 'self' https://fonts.gstatic.com data:;
img-src 'self' data: blob:;
connect-src 'self';
frame-src 'self' blob: https://challenges.cloudflare.com;
object-src 'none'; base-uri 'self'; frame-ancestors 'self';
report-uri /csp-report
```

| Source | Used by |
|---|---|
| `'unsafe-inline'` scripts and styles | The approved pages (C1) have inline `<script>` blocks, `onclick=` handlers and `style=` attributes: 28 pages with scripts, 17 with handlers. Removing them would change frozen files |
| `cdnjs.cloudflare.com` | jQuery validation on the Identity pages (`Areas/Identity/Pages/_ValidationScriptsPartial.cshtml`) |
| `fonts.googleapis.com`, `fonts.gstatic.com` | Material Icons in the three layouts |
| `challenges.cloudflare.com` | Cloudflare Turnstile on the demo website (script and frame) |
| `data:`, `blob:` | Icons and images; the report pages' PDF and Excel downloads |

**What it protects against, and what not.** It stops a page from loading scripts, styles, frames or plugins from any other site. It stops other sites from framing the app, and changing the page's base address. It does **not** stop an injected *inline* script, because the approved pages need `'unsafe-inline'`. Removing that would need nonces in every page, a change to frozen files. Razor's HTML encoding remains the main defence there.

**Modes** (`SecurityHeaders:ContentSecurityPolicy`, see `runbooks/site-configuration.md`):
- `ReportOnly` (the default): sent as `Content-Security-Policy-Report-Only`. Browsers block nothing and post what they *would* block to `/csp-report`.
- `Enforce`: sent as `Content-Security-Policy`. Browsers block.
- `Off`: no policy.

**Reports.** `/csp-report` answers `204` and logs a warning `Content-Security-Policy report: <directive> blocked <address> on <page>`. Query strings and fragments are removed, since they can hold tokens. At most 30 reports a minute are logged, so a broken page can't flood the log.

**Switching to Enforce.** Run the test site with `ReportOnly` for a while, using every page. Then search its log for "Content-Security-Policy report". When there are none, except from browser extensions (`chrome-extension://…`), set `Enforce` on the test site, check again, then production.

**Tests:**
- `SecurityHeadersTests` (Integration, 23):
  - every kind of response and both sites;
  - the three modes, and a bad value stopping start-up;
  - the console's own header;
  - the reports: no query strings, bad reports ignored, the limit.
- `ContentSecurityPolicyBrowserTests` (UI, 4): with the policy **enforced**, a real browser opens and checks:
  - every page of an organization owner (the back office, kiosk pages, reports, Billing, Staff alerts, the Account pages);
  - every console page, signed in with two-factor;
  - the signed-out pages, the demo website and the API docs page.

  It fails on anything blocked, and on a page that sends the browser to sign-in instead of opening. A fourth test checks that a script from another site really is blocked and reported.
- **All browser tests now run with the policy enforced** (`KestrelAppFixture`): the kiosk buttons, Manage Staff, demo website journeys and the others prove the page scripts still work. For new browser tests: don't use Playwright's `WaitForFunctionAsync`, which evaluates a string the policy refuses. Wait with a locator instead; `EvaluateAsync` is fine.

---

## 2. Reads past the organization filters

Every organization's rows are hidden from other organizations by EF Core query filters (fail-closed). `IgnoreQueryFilters()` turns them off for one query. There are 20 such calls in 6 files, all in the web project, none in `src/`. Each was checked for this: could it show or change another organization's data to whoever triggers it?

| File | Calls | What for | Who can trigger it | Verdict |
|---|---|---|---|---|
| `Data/AppUserStore.cs` | 1 (`AllAccounts`, used by find by id, user name, email) | Sign-in, password reset, the security-stamp check, invitations: they look an account up before an organization is chosen | Anyone signing in or resetting a password; the app's own code with the signed-in person's id | OK. Listing accounts (`UserManager.Users`) stays filtered. No page loads a user by an id from the address or a form; the app's calls use the signed-in person's own id, a support session's or a sign-up's |
| `Helpers/PlatformUserValidator.cs` | 2 | An email is one account platform-wide (D2); the uniqueness check must see every account | Creating or changing an account | OK. Returns only "taken" |
| `Services/CurrentTenantProfile.cs` | 1 | The current organization's own row | Every page | OK. By the validated organization id from the tenant context |
| `Services/TenantClock.cs` | 1 | The current organization's time zone | Every page | OK. Same |
| `StaffAlerts/StaffAlertSource.cs` | 3 | The staff-alerts job: organizations with alerts on, then each one's open clockings | The background job only (no request) | OK. Each organization's rows are read by its own id; people are matched to the same organization |
| `Provider/Organizations/OrganizationStore.cs` | 12 | The Provider Console's organization list, details, counts, last activity, edits; the slug check | Console users (sign-in with two-factor, roles), and the website API's slug check | OK. The console sees every organization by design. The slug check (`SlugTakenAsync`) only learns whether an `org-<id>` name is free |

The architecture test `CrossOrganizationReadTests` lists these files with their counts. A new call, or one more in a listed file, fails CI until it is reviewed and added here and to the test.

**Noted, not changed (by design):**
- Adding an admin whose email already has an account in another organization (`AuthService.AddAdminAsync`) answers "invited" instead of "created". So an admin learns that the email has an account somewhere. Decision D2 accepts this (the website API's `email_in_use` is the same).
- The platform tables (`platform.*`) have no query filters. Their stores filter by organization id explicitly (the organization comes from the validated tenant context or a token, never from the request). They have no `IgnoreQueryFilters` to pin. A review of each platform store's queries is not part of this step.

---

## 3. Logs

The guardrails say logs never hold passwords, tokens, full emails, phone numbers or payment payloads. `Security/LogScrubbingTests` (Integration) runs the real app through the flows that handle them, records every log line, and searches each line for the values used. It records:
- at the levels a server keeps (`appsettings.json`);
- at Debug for the app's own code;
- every structured value and every exception.

The flows:
- a website sign-up, with email and mobile;
- the signup token;
- email verification with a password, and its code;
- sign-in with a wrong and then the right password;
- a billing link: its code and its token;
- a PayPal webhook with a payer email and payload;
- an incoming text (a question and STOP) from the owner's phone.

**Result:** nothing found. The existing code already masks phone numbers (`PhoneNumber.Mask`: `+1555•••4567`) and logs ids, not emails. The test also checks that its search finds a leak when there is one.

---

## 4. Still to do in 7.6

- **OWASP ZAP baseline scan** (0 high findings). Needs Docker Desktop running and a copy of the app the scanner can reach. For example, the local copy at `https://localhost:7078`:
  ```powershell
  docker run --rm -t ghcr.io/zaproxy/zaproxy:stable zap-baseline.py -t https://host.docker.internal:7078 -r zap-report.html
  ```
  Or the test site's address. Record the result here.
- **Low advisory GHSA-g4vj-cjjj-v7hg** (NuGet.Packaging / NuGet.Protocol 6.11.0, from the design-time packages). Low severity, so the CI gate doesn't fail on it. Review whether a newer design-time package removes it.
- **Secret scan:** CI's Gitleaks job passes with one known finding: the `EmailService` SMTP credential, in `.gitleaksignore` since G0. Rotating it and removing the ignore line waits for the mailbox owner (PROGRESS, open follow-ups).
