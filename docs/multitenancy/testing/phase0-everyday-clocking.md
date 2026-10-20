# Phase 0: everyday clocking still works

[← All test guides](README.md)

Phase 0 rebuilt the foundations so that many organizations can share one app safely. Staff, volunteers and admins were meant to see **no difference**. These tests check that the daily work still behaves exactly as before.

Unless a test says otherwise, sign in as **Alpha's admin** ([Getting around the app](README.md#getting-around-the-app-organization-admins)).

**What the foundations do behind every click:**
- Every page and every request knows which organization you are signed in to, and the database only ever returns that organization's rows.
- A save that would change another organization's row is refused.
- Times are stored in UTC and shown in the organization's own time zone.

---

## T0-1 Sign in, sign out, lockout

**Use case:** Alpha's admin starts the day at the office computer. A stranger who guesses passwords must be stopped.

1. Open the address. On **Admin Log in only**, type Alpha's admin **Email** and **Password**, click **Log in**.
   - **What it does:** checks the password. Then it finds the organizations this account belongs to: one, so it goes straight in. The sign-in cookie now carries Alpha's organization.
2. Click **Logout** (top right).
   - **What it does:** ends the sign-in on this browser.
3. Type the same email with a **wrong** password and click **Log in**. Do this 5 times.
   - **What it does:** each failure is counted. At the 5th, the account is locked for 15 minutes.
4. Straight after, type the **right** password and click **Log in**.

**Expected:**
- Step 1 opens the kiosk view (**Volunteer Clocking**).
- Steps 3 and 4: after the 5th wrong password, a message says the account is locked, and even the right password is refused until 15 minutes have passed.

## T0-2 Forgot password

**Use case:** an admin forgot their password and resets it without calling anyone.

1. On **Admin Log in only**, click **Forgot your password?**
2. Type Alpha's admin **Email** and click **Send Reset Link**.
   - **What it does:** sends an email with a one-time link (locally: a new `.html` file in the email drop folder). The page says the same thing whether or not the email has an account, so nobody can probe for accounts.
3. Open the email and click its link. On **Reset password**, type the **Email**, a new **Password** and **Confirm Password**, then click **Reset**.
   - **What it does:** saves the new password and makes the link unusable.
4. Sign in with the new password. Sign out, then try the old one.

**Expected:** the email arrives, the new password works, and the old one is refused.

## T0-3 Dashboard

**Use case:** the admin checks how many hours were worked this month.

1. Add `/BackOffice` to the address. **Dashboard** opens.
   - **What it does:** counts Alpha's volunteers, staff and admins, and adds up their worked hours. Beta's are never counted.
2. Under **Select Date Range**, choose a start and an end date, click **Calculate Total Hours**.
   - **What it does:** adds up the working hours of clockings in that range (clocked time minus breaks).
3. Click **Download Report**.
   - **What it does:** makes a PDF of the same figures.

**Expected:** the **Volunteer**, **Staff** and **Admins** figures match Alpha's data only. The PDF shows the same totals.

## T0-4 Add and edit people

**Use case:** a new staff member starts; later their phone number changes.

1. In the left menu click **Manage Staff**, then **Add New**.
   - **What it does:** opens the editor window (nothing is saved yet).
2. Fill in **First Name**, **Last Name**, **Email**, **Gender**, **Phone**, **Zip** and **Address**, click **Save**.
   - **What it does:** checks the fields (letters only in names, phone at least 10 characters, address at least 10 characters), then saves the person in Alpha. The email must be new within Alpha.
3. On the new person's row click **Edit**, change **Phone**, click **Save**.
   - **What it does:** updates that person only.
4. Type the person's name in **Search by name or email...** and click **Search**.
5. Repeat steps 1 to 4 under **Manage Volunteers**. Pick a category in **Which volunteer category do you fall under?** and fill in what it asks for (e.g. **Mandated Community Hours** asks **Which of these applies to you?**).
6. Optional: on a staff row click **Move To Volunteer** and confirm.
   - **What it does:** makes the person a volunteer, with their clockings, and removes them from staff.

**Expected:**
- Each change shows at once and survives a page refresh (F5).
- After step 6, the person is under **Manage Volunteers** and no longer under **Manage Staff**.
- There is no delete button: people can't be removed from these pages. That is how the app has always worked.

## T0-5 Kiosk clocking

**Use case:** a staff member arrives, takes a lunch break and goes home, using the kiosk tablet.

1. In the kiosk view's left menu click **Staff Clocking** (from the back office: add `/StaffAttendance` to the address).
2. Under **Existing Staff**, type a name in **Search Staff**, then click the person's masked email when it appears.
   - **What it does:** opens that person's **Staff Clocking System** page (`/staff-clockin/<id>`). Emails are masked so passers-by can't read them.
3. Click **Clock In**.
   - **What it does:** creates today's clocking record for this person (today in Alpha's time zone). The message "You have successfully clocked in" appears, and after 3 seconds the kiosk returns to its start page.
4. Open the same person again. Click **Leave for Break**, wait a minute, open them again and click **Return from Break**.
   - **What it does:** records the break's start, then its end.
5. Open them again and click **Clock Out**.
   - **What it does:** records the end of the day and works out the working hours: clocked time minus the break.
6. Open them again and click **Clock In**.
7. Repeat steps 1 to 5 for a volunteer with **Volunteer Clocking**.

**Expected:**
- Each button shows its confirmation message.
- Step 6 is refused: "You have already clocked in and out today. If you need to step away, use Leave for Break / Return from Break instead." One clocking per person per day.

## T0-6 Attendance and manual entries

**Use case:** someone forgot to clock in yesterday; the admin enters it by hand and corrects a break.

1. In the back office's left menu click **Staff Clockings**.
   - **What it does:** lists Alpha's clockings, today's first, with their working time.
2. Under **Manual Clocking Here**, type a name in **Search Staff (name or email)** and pick the person. Set yesterday's times in **Clock In** and **Clock Out**, click **Clock In**.
   - **What it does:** creates a clocking for that day with those times.
3. Try the same for someone who already clocked in **today**.
4. On a listed entry click its edit (pencil) button. Change **Break Start** and **Break End** and click **Save**.
   - **What it does:** saves the corrected times and recalculates the working hours.
5. Repeat with **Volunteer Clockings**.

**Expected:**
- The manual entry appears with the right hours.
- Step 3 shows **Clocking not saved**: "<name> has already clocked in today. To change today's times, use Edit on their row." Nothing is saved.
- Working = clocked time minus the break.

## T0-7 Reports and downloads

**Use case:** the admin needs hour confirmations for a volunteer's school, and staff lists for the office.

1. Click **Reports**, then **Staff Clocking**. Choose a person in **Select Staff** (or **-- All --**), set **Start** and **End**, click **Filter**, then **Download PDF**.
   - **What it does:** lists the clockings in that range and makes a PDF with the organization's name and address, and clocked, break and actual hours.
2. Click **Manage Staff**. On a row click **Hours**. Set **Start Date** and **End Date**, click **Apply**, then **Download PDF**.
   - **What it does:** opens that person's **Hours Worked**, with a history and a summary, and a PDF hour confirmation.
3. On **Manage Staff** click **CSV**, then **Excel**, **Copy**, **PDF** and **Print**.
   - **What it does:** exports the staff list in that format.
4. Click **Reports** → **Staff List**, then **Download PDF**.
5. Repeat steps 1 to 4 for volunteers (**Volunteer Clocking**, **Manage Volunteers**, **Volunteer List**).

**Expected:**
- Totals match the attendance pages.
- PDFs show Alpha's name, not Inprem's (unless Alpha is Inprem).
- The exports open with the right people.

## T0-8 Times in the organization's time zone

**Use case:** an organization in New York is managed by someone in Ghana; times must be New York's.

1. On **Staff Clockings**, look at the times of the clocking made in T0-5.
2. In the console (as a Provider user): **Organizations** → Alpha. Look at **Time zone** on the **Overview** tab.

**Expected:** the times shown are Alpha's local time (its time zone), not the server's or your computer's.

## T0-9 Kiosk registration of a new person

**Use case:** a first-time volunteer registers at the kiosk; then someone with the same name registers.

1. Open **Volunteer Clocking**. Under **New Volunteer**, fill in the name, email, zip code, phone and gender, choose a category, and click **Register**.
   - **What it does:** saves the volunteer in Alpha and opens their clock page, ready to **Clock In**.
2. Register again with the **same first and last name**, but another email **and** another phone number.
   - **What it does:** the kiosk shows **Is this you?**: someone with this name is already registered. (The same email or the same phone is refused outright: "A volunteer with this email address is already registered.")
3. Click **No, this is a different person**.
   - **What it does:** registers the second person all the same.
4. Register once more with the same name. This time click **Yes, that's me**, or the masked email shown.
   - **What it does:** opens the existing person's clock page and creates nobody new.

**Expected:** steps 1 and 3 create one person each; step 4 creates nobody. Repeat with **Staff Clocking** → **New Staff**.
