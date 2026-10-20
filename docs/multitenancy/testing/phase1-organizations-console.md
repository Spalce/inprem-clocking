# Phase 1: organizations and the Provider Console

[← All test guides](README.md)

Phase 1 lets Inprem run many organizations from one app:
- each organization sees only its own data;
- one account (one email) can belong to several organizations;
- Inprem's staff manage everything from a separate, protected **Provider Console**, where every action is recorded in an audit log.

**Tip:** use a normal window for one account and a private window (Ctrl+Shift+N) for another.

---

## T1-1 One organization cannot see another's data

**Use case:** two community centres use the same app. Neither may ever see the other's people or hours.

1. Sign in as Alpha's admin and add `/BackOffice` to the address.
2. Click **Manage Staff**, type the name of one of **Beta's** staff in the search box, click **Search**. Do the same in **Manage Volunteers**.
   - **What it does:** searches Alpha's people only. The database filter adds "organization = Alpha" to every query, whatever is typed.
3. Click **Reports** → **Staff Clocking** and open **Select Staff**.
4. Sign out, sign in as Beta's admin, and repeat steps 2 and 3 with an Alpha name.

**Expected:** nobody from the other organization appears anywhere: lists, searches, **Select Staff**, PDFs or dashboard totals.

## T1-2 Someone in two organizations chooses one

**Use case:** a coordinator helps at two centres with one email and one password.

1. Use an account that is an admin of both Alpha and Beta (T1-3 part B makes one).
2. Sign in with it on **Admin Log in only**.
   - **What it does:** finds two active memberships, so it shows **Choose organization** instead of picking one.
3. On **Choose organization**, click the **Alpha** button.
   - **What it does:** ties this sign-in to Alpha. Everything you see now is Alpha's.
4. Add `/BackOffice` and click **Manage Staff**.
5. Sign out, sign in again, and click **Beta** this time.

**Expected:** after choosing Alpha you see only Alpha's people; after choosing Beta, only Beta's. **Sign out** on **Choose organization** signs you out without choosing.

## T1-3 Inviting another admin

**Use case:** Alpha's owner adds a colleague as admin. The colleague may already use the app elsewhere.

**Part A: a new email address**
1. As Alpha's admin, add `/BackOffice` and click **Admins**.
2. Under **Create Admin Account**, fill in **First Name**, **Last Name**, **Email** (never used in the app) and **Password**, click **Create**.
   - **What it does:** creates the account with that password and makes it an admin of Alpha.
3. Sign out and sign in with the new email and password.

**Expected:** the new admin is listed on **Admins** and, signed in, sees Alpha's data only.

**Part B: an email that already has an account** (e.g. Beta's admin)
1. As Alpha's admin, on **Admins** → **Create Admin Account**, type Beta's admin email (any password), click **Create**.
   - **What it does:** one email is one account across the whole app. So instead of a second account, it sends an **invitation** to join Alpha. The typed password is ignored.
2. Sign out. Sign in as Beta's admin. Add `/Account/Invitations` to the address.
3. Under Alpha's invitation, click **Accept**.
   - **What it does:** makes the account an admin of Alpha too. **Decline** would refuse it. **Continue** goes to the back office without deciding.

**Expected:** step 1 says an invitation was sent. After **Accept**, **Choose organization** appears with Alpha and Beta (fixed 2026-10-16).

## T1-4 Console sign-in needs the two-step code

**Use case:** Inprem's console controls every organization, so a stolen password alone must not open it.

1. An existing Provider Owner invites you: in the console, **Provider users** → **Invite**, your **Email**, **Role** **Owner**, click **Send invitation**. If asked, they type a fresh 6-digit code on **Confirm it's you**.
   - **What it does:** creates a one-time setup link, valid 24 hours, and emails it. **Confirm it's you** appears before sensitive actions when you have not typed a two-step code recently.
2. Open the email, click the link. On **Provider account**, type **Password** and **Confirm password**, click **Set password**.
3. On **Set up two-factor sign-in**, scan the QR code with your authenticator app (**+** → **Scan a QR code**), type the 6-digit code it shows, click **Turn on two-factor sign-in**.
   - **What it does:** links the app on your phone to the account. From now on every console sign-in needs its code.
4. **Save your recovery codes** shows 10 codes. Keep them safe, then click **I have saved them - continue**.
   - **What it does:** each code works once, if the phone is lost (T1-11).
5. Click **Sign out**. Sign in again: **Email**, **Password**, **Sign in**, then the **Authenticator code**, **Sign in**.
6. In a private window, open `/Provider` and sign in with an **organization admin's** email and password.

**Expected:**
- The console opens only after the 6-digit code.
- Step 6: the organization admin can't get in (a refusal, or **No access**). Write down exactly what you see.
- After 30 minutes without activity the console signs you out.
- The very first Owner of a new site has nobody to invite them: see `docs/multitenancy/runbooks/bootstrap-and-recovery.md`.

## T1-5 Create an organization (the wizard)

**Use case:** a new centre signs a contract by phone; Inprem sets it up in under 3 minutes. **This organization is reused in Phases 2, 5 and 6.**

1. Start a timer. In the console click **Organizations**, then **New organization**.
2. Fill in **Organization name**, **Address name** (lower-case letters, digits and dashes; it says at once if it's taken), **Time zone**, and optionally **Address (shown on reports)** and **Contact (phone, email; shown on reports)**. Click **Next**.
   - **What it does:** checks the page; nothing is saved yet. **Back** returns to the previous page.
3. Fill in **Owner's first name**, **Last name**, **Owner's email** and optionally **Mobile (optional)**. Click **Next**.
4. Choose a **Plan and price**. In **Billing** choose **Invoiced (paid by invoice)**, set **Free trial (days)** to **7**, click **Next**.
   - **What it does:** decides how the organization pays: **Invoiced** (Inprem sends invoices), **Free**, or **PayPal subscription**. The 7-day trial is used again in T5-9.
5. Check the summary and click **Create organization**.
   - **What it does:** creates the organization, its trial subscription and the owner's account, and emails the owner a link to choose a password. One audit entry is written.
6. Open the owner's email, click the link, choose a password, and sign in on **Admin Log in only**.

**Expected:** under 3 minutes to step 5. The organization appears under **Organizations**. The owner reaches an empty back office with **Billing** and **Staff alerts** in the menu.

## T1-6 Manage an organization

**Use case:** Inprem looks after a customer: corrects details, takes notes, and suspends the account in a dispute.

1. Click **Organizations**, then the T1-5 organization's name.
2. Click each tab:
   - **Overview**: status, owner, time zone, address, people, plan, last activity;
   - **Subscription**: plan, trial and paid period, buttons such as **Extend trial**;
   - **Invoices and payments**;
   - **Admins**: with **Resend invitation**, **Send password reset**, **Make owner**;
   - **Kiosk**: empty until kiosk sessions (Phase 7);
   - **Messages**: with **Open the delivery log**, **Texts settings**, **Sign-in codes**;
   - **Audit**;
   - **Notes**.
3. Click **Edit**, change **Name**, click **Save**.
4. On **Notes** click **Add note**, type in **Note**, click **Add note**.
   - **What it does:** saves a private note for Inprem staff; the organization never sees it.
5. Click **Suspend**. Type a **Reason**. Leave **Include the reason in the email to the owners** unticked. Click **Suspend**.
   - **What it does:** makes the organization read-only (T2-3) and emails its owners. The email gives the reason only if the box is ticked. If the organization pays by PayPal, the page warns that PayPal keeps charging.
6. Click **Reactivate**, type a **Reason**, click **Reactivate**.
   - **What it does:** restores full access at once and emails the owners.
7. Open the **Audit** tab.

**Expected:**
- Each change shows at once, and the owner receives the suspension and reactivation emails.
- **Audit** lists each action once with the time. **Who** shows an account id: a known issue (see the README); the **Audit log** page in the menu shows the email.

## T1-7 Plans

**Use case:** Inprem offers a "Standard" plan, monthly and yearly, on the website.

1. Click **Plans**, then **New plan**.
2. Fill in **Code** (e.g. `standard`; cannot be changed later), **Name**, **Features (one per line, shown on the pricing page)**, **Free trial (days)**. Tick **Show on the public pricing page** (Phase 3 needs it).
3. Under **First price**, choose **Billing cycle** Monthly, an **Amount** and the **Currency**, click **Create plan**.
4. On the plan's page click **Add price**, choose Yearly and an amount, click **Add price**.
   - **What it does:** adds a second price. A price is never edited: a new amount is a new price, so existing customers keep theirs.
5. Click **Retire plan** and confirm. Then click **Offer again**.
   - **What it does:** **Retire plan** stops offering the plan to new customers (existing ones keep it); **Offer again** brings it back.

**Expected:** the plan shows both prices; after **Offer again** it is Active again.

## T1-8 Support session

**Use case:** Alpha's admin phones: "a report looks wrong". Inprem looks inside Alpha's app, with Alpha's knowledge, for a limited time.

1. In the console: **Organizations** → **Alpha** → **Start support session**.
2. Type a **Reason (required; shown to the organization)**, choose **15 minutes** in **Length**, click **Start and open the app**.
   - **What it does:** records the session with its reason and end time, and opens a hand-off page.
3. On **Support session**, click **Continue**.
   - **What it does:** signs you in to Alpha's app as a support visitor. A banner at the top says who is working there and until when.
4. Add `/BackOffice` and look around. Then add `/Billing` to the address.
   - **What it does:** support visitors can't see or change Alpha's billing, and the menu has no **Billing** or **Staff alerts**.
5. Click **End session** in the banner.
   - **What it does:** ends the session and signs you out of Alpha.
6. Sign in as Alpha's admin and add `/Account/SupportSessions` to the address.

**Expected:**
- The banner names you and the end time, and `/Billing` is refused.
- After **End session** you're signed out of Alpha.
- Alpha's admin sees the session with its reason.
- Local copy only: afterwards the console asks you to sign in again (known; see the README).

## T1-9 Provider users and roles

**Use case:** Inprem hires a support assistant who may help organizations but must not touch billing or other staff accounts.

1. **Provider users** → **Invite**. Type a second email, choose **Support — dashboard, organizations, plans (view), notes** in **Role**, click **Send invitation** (type a fresh code if asked).
2. In a private window, set up that account as in T1-4 steps 2 to 4.
3. As the Support user, look at the menu. Open **Plans**, click a plan, try **Edit**.
4. Back as Owner: **Provider users** → the new user's email. Try **Change role** (to **Billing**), then **Disable** (type a **Reason**).
   - **What it does:** **Change role** changes what they may do, at once. **Disable** blocks their sign-in; its reason is recorded and sent to the Owners.
5. Click **Audit log**. Choose your email in **Anyone**, click **Filter**, then **Download CSV**.

**Expected:**
- The Support user has no **Billing**, **Webhook events**, **Notifications**, **Jobs**, **Audit log**, **Provider users** or **Settings**, and plan changes are refused.
- After **Disable** they can't sign in.
- The audit log and CSV list every action you took, with emails.

## T1-10 Console dashboard

**Use case:** each morning Inprem glances at the state of all customers.

1. Click **Dashboard**.
2. Click the **Active organizations** tile, then go back and click **Full audit log →**.

**Expected:**
- The tiles count Active and Suspended organizations.
- **Subscriptions** and **Trials ending soon** match the organizations above (the T1-5 one is in its trial).
- **Latest activity** shows your recent actions.
- The links open the filtered lists.

## T1-11 Sign in with a recovery code

**Use case:** an Inprem Owner's phone was lost; they use one of the saved recovery codes.

1. Sign out of the console. Sign in with **Email** and **Password**.
2. On **Two-factor sign-in**, click **Use a recovery code**. Type one of the codes from T1-4 step 4, click **Sign in**.
   - **What it does:** accepts the code once and lets you only set up a new authenticator: the old link to your phone is replaced, and new recovery codes are issued.
3. Set up the authenticator again and save the **new** codes.
4. Try the same recovery code again on a later sign-in.

**Expected:** step 2 leads straight to **Set up two-factor sign-in**, not to the console. Step 4 is refused: each code works once. For the other recovery levels see `docs/multitenancy/runbooks/bootstrap-and-recovery.md`.
