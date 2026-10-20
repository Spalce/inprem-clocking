# Phase 2: billing and unpaid accounts

[← All test guides](README.md)

Phase 2 adds subscriptions, paid through PayPal or by invoice. An organization whose trial ended unpaid, or that Inprem suspended, keeps all its data and can still read it, but cannot add or change anything until it is sorted out.

Tests marked **(sandbox)** need PayPal sandbox keys. Without them, mark the test **Blocked**.

---

## T2-1 The in-app Billing page

**Use case:** the owner of the organization created in T1-5 wants to know what they pay and when the trial ends.

1. Sign in as that organization's **owner**, add `/BackOffice`, click **Billing** in the left menu.
   - **What it does:** shows the organization's own subscription, invoices and payments. Only the owner sees this page.
2. Sign in as an admin who is **not** the owner, and look for **Billing** in the menu. Then type `/Billing` after the address.

**Expected:**
- Under **Your subscription**: **Plan**, **Price**, **Status** (Trialing) and **Trial ends**.
- Under **Payments**: "No payments yet. PayPal also emails you a receipt for each payment."
- Step 2: the admin has no **Billing** item, and `/Billing` is refused.

## T2-2 Extend a trial and give free access (console)

**Use case:** a prospect asks for one more week to decide; another centre gets a free month as a goodwill gesture.

1. In the console: **Organizations** → the T1-5 organization → **Subscription** tab.
2. Click **Extend trial**. Pick a later date in **New last day of the trial**, click **Extend trial**.
   - **What it does:** moves the trial's end date. The button only appears for a trial (or an ended trial) that does not pay by PayPal.
3. Open **another** organization's **Subscription** tab, click **Free access**. Pick a date in **Last day of free access**, type a **Reason (required; recorded in the audit log)**, click **Save**.
   - **What it does:** gives full access until that date, whatever the subscription says. **Remove the free access instead** takes it away again.
4. As each organization's owner, open **Billing**.

**Expected:** the new dates show in the console (**Trial ends**, **Free access until**) and on each owner's **Billing** page. Both actions are on each organization's **Audit** tab.

## T2-3 An unpaid or suspended account becomes read-only

**Use case:** Inprem suspends Alpha (for example, a payment dispute). Alpha must not lose data or reports, but must not change anything either.

1. In the console: **Organizations** → **Alpha** → **Suspend**. Reason "test unpaid", click **Suspend**.
2. As Alpha's admin, add `/BackOffice`.
   - **What it does:** the app checks Alpha's access on every request; now it is read-only.
3. Open **Manage Staff**. Run **Reports** → **Staff Clocking** → **Filter** → **Download PDF**. On **Manage Staff** click **CSV**.
   - **What it does:** reading, reports and downloads are always allowed.
4. On **Manage Staff**, click **Add New**, then **Edit** on a row, then **Move To Volunteer**.
   - **What it does:** each click is stopped before any form opens.
5. On **Admins**, look at **Create Admin Account**. On **Staff Clockings**, look at **Manual Clocking Here**.
6. In the kiosk view open a staff member's clock page (as in T0-5) and click **Clock In**.
7. In the console: **Alpha** → **Reactivate**, a reason, **Reactivate**. Repeat step 4.

**Expected:**
- A banner at the top: "**Read-only:** Inprem has suspended this organization's account. Please contact Inprem. Reports and exports still work; changes cannot be saved and the kiosk is paused."
- Step 3 works.
- Step 4 shows a notice at once, e.g. "Your organization's account is suspended. You cannot add new staff.", and no form opens.
- Step 5: the forms are greyed out with the same sentence.
- Step 6: opening the clock page shows **The kiosk is paused**. A page already open shows the app's usual error message.
- After reactivating, everything works again and no data was lost.

## T2-4 Cancel from the console (sandbox)

**Use case:** an organization asks Inprem by phone to stop its PayPal subscription.

1. Open an organization with an active PayPal subscription (after T2-7 or T3-6), **Subscription** tab.
2. Click **Cancel subscription**. Type a **Reason (required; sent to PayPal and recorded in the audit log)**, click **Cancel subscription** (or **Keep it** to go back).
   - **What it does:** cancels the subscription at PayPal. The organization keeps full access until the end of the period it has paid for.

**Expected:** the status shows the subscription ends at the end of the paid period. Without a PayPal subscription the button is not shown: **Blocked**.

## T2-5 Webhook events page

**Use case:** Inprem checks that PayPal's notifications ("payment received", "subscription cancelled") arrive and are processed.

1. In the console click **Webhook events**. Choose a status in **Any status**, or type an event type, and click **Filter**.
2. Click an event's time to open it. **Replay** processes it again (safe: an event never counts twice).

**Expected:** the page lists PayPal's messages (empty until the sandbox tests run). A Support user has no **Webhook events** in the menu.

## T2-6 Publish a plan to PayPal (sandbox)

**Use case:** before customers can pay for "Standard" at PayPal, PayPal must know the plan.

1. **Plans** → the plan from T1-7 → **Publish to PayPal**.
   - **What it does:** creates the plan and each price at PayPal and stores PayPal's ids.

**Expected:** each price gets a value in **PayPal plan**. Without keys the page says PayPal is not configured on this site: **Blocked**.

## T2-7 Subscribe from the Billing page (sandbox)

**Use case:** at the end of the trial, the owner pays by PayPal.

1. As a trial organization's owner, click **Billing**.
2. Under **Subscribe**, click a price's button (e.g. "25.00 USD / month").
   - **What it does:** starts a checkout at PayPal, for this organization and this price.
3. At PayPal, sign in with a sandbox buyer account and approve.
4. Back in the app: **Confirming your subscription…**.
   - **What it does:** asks PayPal for the state of the subscription, every few seconds. Coming back alone is never proof of payment: only PayPal saying "active" activates it.

**Expected:** the page ends with Active. The console shows the organization Active, and the PayPal message is under **Webhook events**.

## T2-8 Cancel at PayPal (sandbox)

**Use case:** the owner starts paying, then changes their mind at PayPal.

1. Start as in T2-7, but click **Cancel** (or "Cancel and return") at PayPal.

**Expected:** a page saying the checkout was not completed; the organization stays as it was (still Trialing).
