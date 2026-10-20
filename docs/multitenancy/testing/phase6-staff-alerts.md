# Phase 6: staff and volunteer alerts

[← All test guides](README.md)

Phase 6 lets an organization text short reminders to its staff and volunteers:
- **missed clock-out:** you are still clocked in at the organization's set time (e.g. 20:30);
- **long break:** your break has lasted longer than the set minutes;
- **end-of-day summary:** optional, one email to the admins listing who is still clocked in.

The rules:
- **Off by default.** Only the organization's **owner** can switch it on. With it off, the forms look exactly as before.
- **Opt-in only.** A person is texted only if they ticked the box agreeing to texts, with a number that has its country code. STOP stops everything.
- **Each reminder at most once a day** per person.
- **Quiet hours drop reminders.** A reminder that would fall in quiet hours (21:00 to 08:00, the organization's time) is not sent at all: by the morning it would be wrong.

A background job checks every **5 minutes**, so a reminder comes up to 5 minutes after the set time. Texts go to the stand-in folder ([Phase 5](phase5-messages-texts.md)).

**Set up once, before T6-5:** in the console, **Organizations** → the T1-5 organization → **Edit**. Set **Time zone** to your own (e.g. **Africa/Accra**) and **Save**. The times below then match your clock. Check that **Texts** for this organization is on (T5-8).

---

## T6-1 Staff alerts settings (owner only)

**Use case:** the owner of a centre decides to remind volunteers who forget to clock out.

1. Sign in as the T1-5 organization's **owner**, add `/BackOffice`, click **Staff alerts** in the left menu.
   - **What it shows:**
     - **Send staff alerts** (off);
     - **Still clocked in at (your organization's time)** (21:00);
     - **A break is long after (minutes)** (60);
     - **At that time, email the admins one message: who is still clocked in** (off);
     - a line on quiet hours.
2. Type `5` in **A break is long after (minutes)** and click **Save**.
3. Set it back to `60`. Tick **Send staff alerts**, click **Save**.
   - **What it does:** switches staff alerts on for this organization only. The staff and volunteer forms now show the texts box (T6-3, T6-4). The audit log records `organization.staff-alerts-changed`.
4. Sign in as an admin of the organization who is **not** the owner. Look for **Staff alerts**, then type `/Settings/StaffAlerts` after the address.

**Expected:**
- Step 2 is refused: "A long break is 10 to 720 minutes."
- Step 3 says "Staff alerts are on…".
- Step 4: no menu item, and the page is refused. It is also hidden during a support session (T1-8).

## T6-2 Alerts off: the forms are unchanged

**Use case:** organizations that never switch alerts on must see no change at all.

1. As **Alpha's** admin (alerts off), open **Volunteer Clocking** → **New Volunteer** and **Staff Clocking** → **New Staff**.
2. In the back office, open **Manage Staff** → **Add New**, and **Manage Volunteers** → **Add New**.

**Expected:** no texts box anywhere; the forms are exactly as in Phase 0.

## T6-3 Kiosk registration with the texts box

**Use case:** a new volunteer agrees, at the kiosk, to get reminders by text.

1. As the T1-5 organization's owner (alerts on from T6-1), open **Volunteer Clocking**. Under **New Volunteer**, below the form, find the unticked box: "Text me clock-in reminders from Inprem Clock-in (still clocked in late, long breaks). Msg frequency varies. Msg & data rates may apply. Reply STOP to opt out, HELP for help."
2. Fill in the form with a phone **without** country code (e.g. `0241234567`). Tick the box and click **Register**.
   - **What it does:** the box's check stops the form: a text can't reach a number without its country code.
3. Change the phone to `+233 24 123 4567` (any number with a country code) and click **Register**.
   - **What it does:** registers the volunteer, then records their agreement: the exact wording, the number, the time, and "kiosk registration" as where it was given.
4. Register another person **without** ticking the box.
5. In the console, open **Audit log**, choose the organization in **All organizations**, click **Filter**.

**Expected:**
- Step 2 shows "Add the country code to the phone number (e.g. +1 614 516 1812), or untick the box." and nothing is saved.
- Steps 3 and 4 register both people.
- The audit log has one `notifications.consent-granted` entry, "Texts agreed to (kiosk-registration).", with the number masked, for step 3 only.

## T6-4 Manage Staff / Manage Volunteers with the texts box

**Use case:** the admin adds a staff member's agreement given on paper.

1. As the owner, **Manage Staff** → **Add New**. Fill in the form with a **Phone** that has its country code, tick the texts box, click **Save**.
   - **What it does:** saves the person, then records their agreement ("back office" as where it was given).
2. Click **Add New** again.
3. On another staff row click **Edit**, change **Phone** to one with a country code, tick the box, **Save**.
4. **Edit** someone whose phone has no country code, tick the box, **Save**.
5. Repeat step 1 under **Manage Volunteers**.

**Expected:**
- Steps 1, 3 and 5 save the person and record a consent: a `notifications.consent-granted` entry "Texts agreed to (back-office)." in the audit log.
- Step 2: the box is unticked again; a tick never carries over to the next person.
- Step 4 shows the country-code message and does not save.

## T6-5 Missed clock-out reminder

**Use case:** a volunteer forgets to clock out; at the organization's set time they get a text.

1. Use the volunteer who agreed in T6-3: open their clock page and click **Clock In**.
2. Use the person registered in T6-3 step 4 (no agreement): give them a phone with country code (**Manage Volunteers** → **Edit**, without ticking the box) and **Clock In** too.
3. As the owner, **Staff alerts**: set **Still clocked in at** to about **10 minutes from now** (your time, between 08:00 and 20:50). Click **Save**.
4. Wait until 5 minutes after that time. Look in the stand-in folder and in the console's **Notifications** → **Delivery log**.
   - **What it does:** at the set time, the job finds everyone clocked in before it and not clocked out. Each gets one text, through the Phase 5 checks.
5. Wait another 10 minutes and look again.
6. **Clock Out** the volunteer.

**Expected:**
- Step 4: a new `…-sms-<number>.txt` for the T6-3 volunteer: "<Organization>: you are still clocked in (since <time>). Please clock out at the kiosk, or tell an admin. Reply STOP to opt out."
- The **Delivery log** shows it **Sent**. The step 2 person shows **Skipped** with `no-consent`, and no file.
- Step 5: nothing new; each person is reminded at most once a day.
- Someone who clocked in **after** the set time is not reminded.

## T6-6 Long-break reminder

**Use case:** a staff member left for lunch and forgot to end the break.

1. As the owner, **Staff alerts**: set **A break is long after (minutes)** to `10`, **Save**.
2. Use the staff member who agreed in T6-4: open their clock page, **Clock In**, then (open it again) **Leave for Break**.
3. Wait 15 minutes. Look in the stand-in folder.
4. Another agreed person: **Clock In**, **Leave for Break**, and **Return from Break** after 5 minutes.

**Expected:**
- Step 3: one text: "<Organization>: your break started at <time> and is still open. Please end it at the kiosk when you are back. Reply STOP to opt out."
- Step 4: no text; the break ended in time.

## T6-7 End-of-day summary to admins

**Use case:** the coordinator wants one email at closing time listing who is still clocked in, not one per person.

1. As the owner, **Staff alerts**: tick **At that time, email the admins one message: who is still clocked in**. Set **Still clocked in at** about 10 minutes ahead. **Save**.
2. Make sure two or more people are clocked in today and not out (agreement or not doesn't matter here).
3. Wait until 5 minutes after the set time. Look in the email drop folder.
4. Optional: another day, with nobody clocked in at the set time.

**Expected:**
- Step 3: **one** email to each owner and admin, "<Organization>: <n> still clocked in at <time>", listing each name with the clock-in time (and "on a break since …").
- Never by text.
- Step 4: no email.

## T6-8 Quiet hours, no consent, alerts off

**Use case:** reminders must never wake anyone at night, and never reach someone who did not agree.

1. As the owner, **Staff alerts**: set **Still clocked in at** to **21:00**, **Save**. Leave an agreed person clocked in past 21:00 (organization's time), and wait until 21:05.
   - **What it does:** 21:00 is the start of quiet hours. The reminder is dropped, not held for the morning.
2. Look at the **Delivery log**.
3. Untick **Send staff alerts**, **Save**. The next day, leave someone clocked in past the set time.

**Expected:**
- Step 2: the reminder shows **Skipped** with `quiet-hours`, and no file appears, not even the next morning.
- Step 3: nothing at all is sent or logged, and the texts box is gone from the forms.

## T6-9 Real staff texts (Twilio)

**Use case:** the volunteer receives the reminder on their own phone, and replies STOP.

Needs Twilio test credentials (as in T5-10):
1. Do T6-3 with your real phone number and T6-5.
2. Reply **STOP** to the reminder. The next day, repeat T6-5.

**Expected:**
- Step 1: the reminder arrives on your phone.
- Step 2: no reminder; the **Delivery log** shows `opted-out`.
