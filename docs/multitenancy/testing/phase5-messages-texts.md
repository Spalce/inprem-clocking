# Phase 5: messages, texts and sign-in codes

[← All test guides](README.md)

Phase 5 adds one message system for the whole app. Every email or text:
1. is checked first: did the person agree to texts, did they reply STOP, is it quiet hours in their organization's time zone, are the monthly and daily limits reached;
2. is then **queued**, or **skipped** with the reason;
3. is sent by a background job **within a minute** and logged with its status and cost.

The same system sends:
- sign-in codes by text: for console users, and for organization admins when their organization asks for it;
- billing alerts to organization owners;
- alerts to Inprem's own staff.

**Twilio isn't connected yet.** Texts go to the **stand-in**: each text is saved as a `.txt` file in the stand-in folder ([README](README.md#the-local-copy)) and marked sent. Use a real-looking mobile number **with its country code** (e.g. `+233 24 123 4567` or `+1 202 555 0123`): no text leaves your computer. Tests marked **(Twilio)** need real Twilio test credentials.

**Quiet hours** are 21:00 to 08:00 in the organization's time zone (Inprem's own: New York). Normal texts wait until 08:00; urgent ones (test messages, system problems, sign-in codes) go at once.

---

## T5-1 Where emails and texts go (stand-in)

**Use case:** the tester needs to read what the app sends, without real phones or mailboxes.

1. Open the email drop folder and the stand-in folder (`dotnet user-secrets list --project InpremClockingApp` shows both paths).
2. Do any test that sends an email (e.g. T0-2), then look in the drop folder.
3. After T5-2 step 2, look in the stand-in folder.

**Expected:** each email is a new `.html` file (double-click to open it). Each text is a `.txt` file: `…-sms-<number>.txt` for a message, `…-code-<number>.txt` for a sign-in code.

## T5-2 Your phone for sign-in codes

**Use case:** an Inprem Owner wants a second way to sign in, in case the authenticator app is not at hand.

1. In the console, click **My phone for codes** (top). If asked, type your authenticator code on **Confirm it's you**.
   - **What it does:** adding a phone is a sensitive action, so the console asks for a fresh two-step code first.
2. Type your mobile number with its country code, click **Text me a code**.
   - **What it does:** sends a 6-digit code to that number (a `…-code-…txt` file). The code works once, for 10 minutes.
3. Type the code in **Code from the text**, click **Confirm this phone**.
   - **What it does:** saves the number as yours, now proven. The audit log records it with the number masked.
4. Click **Text me a code** three more times within 10 minutes (with **Cancel** in between).
5. Optional: **Remove this phone**.

**Expected:**
- After step 3 the page shows the confirmed number, masked.
- Step 4: after 3 codes in 10 minutes, more are refused for a while (at most 10 a day).

## T5-3 Console sign-in with a texted code

**Use case:** the Owner's phone battery is flat; they sign in with a code texted to their confirmed number instead (needs T5-2).

1. **Sign out**. Sign in with **Email** and **Password**.
2. On **Two-factor sign-in**, click **Text me a code instead**.
3. On **Sign in with a texted code**, click **Text me a code**. Open the newest `…-code-…txt` file, type the code in **Code from the text**, click **Sign in**.
   - **What it does:** checks the code with the code service; a code works once.
4. Sign out, repeat steps 1 to 3, but type a wrong code three times, then the right one.
5. Sign out, repeat, and use an old (already used) code.

**Expected:**
- Step 3 opens the console.
- Step 4: wrong codes are refused with a message; after a few, ask for a new one with **Send a new code**.
- Step 5 is refused.
- **Use the authenticator app instead** goes back to the 6-digit app code.

## T5-4 Texted sign-in codes for an organization's admins

**Use case:** an organization asks that its admins also prove their identity with a code from their phone.

1. Console: **Organizations** → the T1-5 organization → **Sign-in codes** (top, or the **Messages** tab).
2. Tick **Ask for a texted code after the password**, click **Save**.
   - **What it does:** from now on, after the password, the organization's admins must type a texted code.
3. As that organization's owner: sign out and sign in on **Admin Log in only**.
   - **What it does:** after the password, **Sign-in code** appears. The owner has no confirmed number yet, so it asks for one.
4. Type a **Mobile number** with country code, click **Text me a code**. Open the newest `…-code-…txt` file, type the code in **Code from the text**, click **Continue**.
   - **What it does:** proves the number and lets them in. They are emailed that a number was added, so a stranger who learnt the password could not add theirs unnoticed.
5. Sign out and sign in again.
6. In the console, untick the box and **Save**.

**Expected:**
- Step 4 opens the app; the drop folder has the "number added" email.
- Step 5 asks for a code again, sent to the saved number.
- After step 6, the password alone is enough.
- Admins of other organizations are never asked for a code.

## T5-5 My alerts

**Use case:** each Inprem staff member chooses which alerts they get, and how.

1. In the console, click **My alerts** (top).
2. Look at the choices:
   - **New signups on the website** (ticked by default);
   - **Billing problems (a payment failed, a subscription was suspended)**: **Each time**, **One daily summary** or **Off**;
   - **System problems** (urgent, also at night; Owners only).
3. With a confirmed phone (T5-2), tick the texts box (its wording says what you agree to), click **Save**.
   - **What it does:** saves your choices and records your agreement to Inprem's texts, with the exact wording.
4. Untick the texts box and **Save** again.
   - **What it does:** withdraws your agreement to Inprem's texts only. Texts from an organization are not affected.
5. Sign up a new organization on the demo website (T3-2), then look in the drop folder.

**Expected:**
- "Saved" after steps 3 and 4.
- Without a confirmed phone, the page says to confirm it first on **My phone for codes**.
- Step 5 produces a "[Inprem] New signup: …" email to you, and a text only if the texts box is ticked.

## T5-6 Send me a test message; the Delivery log

**Use case:** Inprem checks that messages really arrive, and looks up why one did not.

1. Console: **Notifications**. The **Delivery log** tab lists recent emails and texts.
   - **What it shows:** **When (UTC)**, **Message**, **To** (masked), **Organization**, **Status** (Queued, Sent, Delivered, Failed, Skipped), **Why / error** and **Cost**.
2. Click **Send me a test message**, then **Send**.
   - **What it does:** queues an email to you, and a text to your confirmed phone if you agreed to texts (T5-5). Test messages are urgent, so quiet hours don't hold them.
3. Wait a minute and refresh the **Delivery log**.
4. Use **Email and text** (channel) and **Any status** to filter, click **Filter**.

**Expected:**
- Your test email (and text) shows **Sent** within a minute, and the files are in the folders.
- A message that could not go shows **Skipped** with the reason, e.g. `no-consent` or `quiet-hours`.

## T5-7 Message wording

**Use case:** Inprem rewords the "trial ending" email, or switches a message off for a while.

1. **Notifications** → **Message wording** tab. Each row: **Message**, **Channel** (email or text), **Who gets it, when**, **Wording**.
2. On the trial-ending email row, click **Edit**. Change **Subject** or the text, click **Save**.
   - **What it does:** from now on that message uses your wording. Words in braces, e.g. `{organization}` and `{trialEnd}`, are filled in for each organization.
3. Click **Edit** again, untick **Send this message (unticked: nothing is sent, and the log says why)**, **Save**.
4. Click **Edit**, then **Back to the default wording**.

**Expected:** each change shows in the list. While switched off, that message is logged as skipped (`template-off`). After step 4 the original wording is back.

## T5-8 An organization's texts: on, off, monthly limit

**Use case:** an organization that sends too many texts gets a monthly cap; another wants no texts at all.

1. **Notifications** → **Texts and spend** tab.
   - **What it shows:** today's text spend for the whole platform against the daily cap ($5), and for each organization this month: texts, cost and monthly limit (50 by default).
2. Click **Change** on an organization (or **Organizations** → it → **Texts**). If asked, confirm with your code.
3. Untick **Send texts for this organization**, or set **At most this many texts a month (in the organization's time zone)** to `1`. Click **Save**.
   - **What it does:** from now on that organization's texts are skipped (`sms-off`), or skipped once the month's limit is reached (`monthly-limit`). Emails still go.
4. Optional, after Phase 6: trigger two staff alerts for that organization and check the **Delivery log**.
5. Put the settings back.

**Expected:** the new settings show in **Texts and spend**; skipped texts show their reason in the **Delivery log**.

## T5-9 Billing alerts to owners and Inprem

**Use case:** owners hear about their trial, invoices and suspension without logging in; Inprem hears about billing trouble.

1. **Suspended / reactivated:** do T1-6 steps 5 and 6. Look in the drop folder.
2. **Invoice issued:** do T4-1. The owners get a text only if they agreed to texts (not possible from the demo website, so expect none) and always the invoice email.
3. **Trial ending:** the T1-5 organization has a 7-day trial. The daily check runs at **03:15 UTC** (03:15 in Ghana), only while the app is running: on the local copy, leave it running overnight. After the first run following T1-5, look in the drop folder and the **Delivery log**.
   - **What it does:** the daily check finds trials ending in about 7 days, and again the day before, and emails the owners once for each.
4. **Invoice overdue:** create an offline invoice (T4-1) with today as **Due date** and leave it unpaid. The next day's daily check sends the owners one overdue email.
5. **Payment failed, suspended by PayPal (sandbox):** a failed sandbox payment emails the owners and the console users who chose **Billing problems**.

**Expected:**
- Each alert arrives once, even if the check runs again.
- The **Delivery log** shows them under the organization's name.

## T5-10 Real texts, STOP and START (Twilio)

**Use case:** someone replies STOP to a text; from that moment they get no more texts from anyone in the app, until they reply START.

Needs Twilio test credentials and a public address for the test copy:
1. The developer sets `Twilio:Mode` to `Twilio` with the credentials, and the webhook addresses at Twilio (`docs/multitenancy/runbooks/site-configuration.md`).
2. Do T5-6 with your real phone. Then reply **STOP** to the text.
3. Do T5-6 again. Open **Notifications** → **Opt-outs**.
4. Reply **START**, and do T5-6 again.

**Expected:**
- Step 2: the text arrives, and the log turns it **Delivered** with its real cost.
- Step 3: the text is skipped (`opted-out`), and your number (masked) is under **Opt-outs**.
- Step 4: texts arrive again.
