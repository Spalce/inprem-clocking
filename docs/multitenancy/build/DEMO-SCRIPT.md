# Demo script (about 10 minutes)

Available once Phases 0–3 and 5 pass. Prepare beforehand: provider owner account with authenticator on your phone; organizations **Alpha** (Active, a few staff and clockings) and **Beta** (Trialing); PayPal sandbox buyer login; your phone (Twilio-verified) at hand; two browser windows (one normal, one private).

## 1. Provider Console and secure first login (2 min)
1. Open the admin host. Sign in with email, password and the authenticator code. Point out: no built-in or hardcoded admin; the first owner was created once through a setup key and an emailed invitation, and `/setup` is now closed.
2. Organizations list: search, table, **New organization** button (no form on the list page).
3. Open Alpha → tabs Overview, Subscription (Active, PayPal id), Admins, Audit.

## 2. Two organizations, isolated (2 min)
1. Private window: sign in as Alpha's admin on the app host → staff list, kiosk clock-in/out, report PDF.
2. Normal window: sign in as Beta's admin → Beta sees only Beta.
3. In Alpha's window, change a staff id in the URL to one of Beta's → not found.

## 3. New organization from the console (1 min)
1. **New organization** → wizard: Delta → owner email → plan with 14-day trial → Review → Create.
2. Show the invitation link/email; the owner sets their own password.

## 4. Subscription with PayPal (2 min)
1. As Beta's owner → Billing → Subscribe (monthly) → PayPal sandbox → approve.
2. Back on "Confirming…" → **Active**. Console: Beta now Active; Webhook events shows `BILLING.SUBSCRIPTION.ACTIVATED`.

## 5. Website signup with SMS (2 min)
1. Demo website page → pricing → sign up "Gamma" with your mobile → SMS code arrives → enter it.
2. Subscribe now → PayPal sandbox → back on the website → **Active**; activation SMS arrives.
3. Open the set-password link → Gamma's back office.

## 6. When an organization stops paying (1 min)
1. Console → Beta → Suspend (reason "demo").
2. Beta's window: try to add a staff member → blocked with a clear message; reports still open.
3. Reactivate.

## Closing points
- Same approved clocking app, now serving many organizations.
- Provider, organizations and website kept apart (separate hosts/cookies, API with its own tokens).
- Payments confirmed only by PayPal itself; no card data in our system.
- Still to come (if not yet built): invoices (Phase 4), staff alerts (Phase 6), hostname cutover and hardening (Phase 7).
