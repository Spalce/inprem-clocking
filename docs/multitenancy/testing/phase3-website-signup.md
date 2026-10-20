# Phase 3: website sign-up (demo website)

[← All test guides](README.md)

Phase 3 lets a new organization sign up and manage its subscription from Inprem's marketing website, without phoning Inprem. The website talks to the app through the **website API**.

Until the web team's site is ready, a plain **demo website** inside the test copy does the same steps. It looks basic on purpose: check what happens, not the design. Its top bar has **Pricing** and **Manage my subscription**.

- Use a new email address for each sign-up (locally the emails land in the drop folder, so any invented address works).
- Tests marked **(sandbox)** need PayPal sandbox keys.

---

## T3-1 Pricing page

**Use case:** a centre manager compares plans on the website.

1. Add `/demo-site` to the address.
   - **What it does:** the page asks the website API for the plans that have **Show on the public pricing page** ticked (T1-7).

**Expected:** each public plan with its trial length, features, and a button per price (e.g. "25 USD / month").

## T3-2 Sign up for a free trial (journey J1)

**Use case:** the manager starts a free trial on their own, at night, without calling anyone.

1. On **Pricing**, click a price button. **Register your organization** opens.
2. Fill in **Organization name**, **Web name (lower-case letters, digits, dashes)**, **Time zone**, **Your first name**, **Your last name**, **Your email**. **Mobile** is optional.
3. Tick **I accept the terms** and **I accept the privacy policy**. Leave **Subscribe now with PayPal (instead of the free trial)** unticked. Click **Register**.
   - **What it does:** checks the CAPTCHA, then creates the organization (Trialing), its owner account **without a password**, and emails a confirmation link. Inprem's console users who chose "New signups" are alerted (T5-5).
4. Before opening the email, try to sign in to the app with that email.
5. Open the email, click the link. On **Confirm your email**, type **Password** and **Password again**, click **Confirm and save password**.
   - **What it does:** confirms the email belongs to the person and sets their password. Only now can they sign in.
6. On **You are all set**, click **Sign in** and sign in with the new email and password.

**Expected:**
- **Check your email** after step 3; step 4 is refused.
- After step 6, an empty back office.
- The console shows the organization as Trialing (**Organizations** → its name → **Subscription**).

## T3-3 Mistakes on the form

**Use case:** the manager makes typical mistakes; nothing half-done may be left behind.

1. Open **Register your organization** again. In **Web name**, type the web name from T3-2 and click outside the box.
2. Fill in the rest correctly but leave **I accept the terms** unticked, click **Register**.
3. Tick both boxes, use a new **Web name**, but type the email from T3-2 in **Your email**, click **Register**.

**Expected:**
- Step 1 says "Taken. Try …" with a suggestion.
- Step 2 shows a red message about the terms.
- Step 3 says the email is already in use.
- Nothing is created.

## T3-4 Get a "manage subscription" link (journey J3)

**Use case:** an existing customer wants to see or change their subscription from the website, without a website account.

1. In the top bar click **Manage my subscription**. Type the owner email from T3-2 in **Email**, click **Send me a link**.
   - **What it does:** if the email is an organization owner's, emails a one-time link valid for 15 minutes. The page answers the same either way.
2. Do it again with an email that is not a customer.
3. Open the owner's email and click its link.
4. Open the same link again, in a new tab.

**Expected:**
- Steps 1 and 2 show the same message (the website never reveals who is a customer), and only the owner receives an email.
- Step 3 opens **Your subscription** (Trialing) with **Pay** buttons.
- Step 4: the link no longer works.

## T3-5 Pay after a trial (journey J3, sandbox)

**Use case:** the trial customer decides to pay.

1. On T3-4's **Your subscription**, click a **Pay** button. Approve at PayPal with a sandbox buyer.

**Expected:** **Confirming your subscription…**, then **You are subscribed**. The console shows Active.

## T3-6 Sign up and pay now (journey J2, sandbox)

**Use case:** a manager already decided and pays straight away.

1. Do T3-2 steps 1 to 3 with a new email, but tick **Subscribe now with PayPal (instead of the free trial)**. Approve at PayPal.
   - **What it does:** the organization has no access until PayPal confirms the payment.

**Expected:** **You are subscribed**, then the email to choose a password (T3-2 steps 5 and 6).

## T3-7 Change plan and cancel (journey J4, sandbox)

**Use case:** the customer moves to a bigger plan, and later leaves.

1. For the subscribed organization, get a link as in T3-4 and open it.
2. Click a **Change to …** button and approve at PayPal.
3. Get another link, open it, click **Cancel the subscription**.
   - **What it does:** cancels at PayPal; access continues to the end of the paid period.

**Expected:** after step 2 the new plan shows. After step 3: **Cancelled - access until** <date>, and the app keeps working until then.

## T3-8 Cancel at PayPal (journey J5, sandbox)

1. Start T3-6 with another new email, and click **Cancel** at PayPal.

**Expected:** **Checkout was not completed** with **Back to pricing**. Nothing is charged.

## T3-9 Payment problem (journey J6, sandbox)

1. The developer sets up a sandbox payment that fails; then do T3-6.

**Expected:** "We could not activate your subscription. Please contact us." The organization is not activated, and the problem appears under **Webhook events**.

## T3-10 The demo website is off unless switched on

**Use case:** the pretend website must never appear on the live site.

1. On a copy where `Demo:EnableDemoSite` is not `true` (ask the developer), add `/demo-site` to the address.

**Expected:** a "not found" page.
