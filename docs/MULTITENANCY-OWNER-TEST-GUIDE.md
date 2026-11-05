# Provider Console Owner Familiarization and Test Guide

## Purpose

This guide is for the project owner. It explains the Provider Console in plain language and gives simple tests for the features built so far.

The Provider Console is the administration area for the Inprem platform. It is separate from the application used by individual organizations.

**This guide does not cover PayPal or Twilio. Those integrations are not ready for testing.**

## Before testing

Use a development, QA, or staging environment and test data only. Prepare a Provider Owner account, test email addresses, and two test organizations. Do not use real customer data or disable the only Owner account.

## Provider roles

| Role | Access |
|---|---|
| **Support** | Dashboard, Organizations, organization notes, and read-only Plans. |
| **Billing** | Support access plus Plans, Billing, invoices, and Webhook events. |
| **Owner** | All Provider Console features, including Notifications, Jobs, Audit log, Provider users, and Settings. |

Every Provider user is expected to use two-factor sign-in.

## Opening and signing in

Open the deployed application and go to:

```text
https://your-domain.com/Provider
```

1. Sign in as a Provider Owner.
2. Complete two-factor sign-in.
3. Confirm that the page says **Inprem Provider Console**.
4. Confirm that the left-hand menu is visible.
5. Check the signed-in email shown at the top.

**Expected result:** The Owner reaches the Provider Console and sees the menus allowed for the Owner role.

- [ ] Passed
- [ ] Failed

---

# Provider Console menu guide

## 1. Dashboard

### What it does

The Dashboard provides a quick view of active and suspended organizations, trials ending soon, failed background jobs, Provider Owners with two-factor sign-in, subscription counts, and recent activity. The organization tiles open filtered organization lists.

### Why it is necessary

It is the daily starting point for the Provider team. It helps the Owner notice account, security, and system problems quickly.

### Test it

1. Open **Dashboard**.
2. Confirm that the tiles and recent activity load.
3. Select the active organization count and confirm that Organizations opens with Active selected.
4. Return and select the suspended count, if one exists.
5. Review Trials ending soon and Failed background jobs.
6. Confirm that recent actions appear in Latest activity.

- [ ] Passed
- [ ] Failed

## 2. Organizations

### What it does

This lists every organization using the platform. You can search or filter by name, slug, owner email, status, plan, and creation date. The list also shows plan, people count, last activity, and creation date. Owners can select **New organization**.

### Why it is necessary

This is the main place to find and manage tenant organizations and open their details.

### Test it

1. Open **Organizations**.
2. Search for a test organization.
3. Filter by Active status, plan, and date range.
4. Clear the filters.
5. Open an organization and use Previous/Next if there are multiple pages.
6. If Owner access is available, create one test organization and confirm it appears once.

Do not test PayPal options in the New organization wizard.

- [ ] Passed
- [ ] Failed

## 3. Organization details

Selecting an organization opens its details page. It shows the name, slug, status, and creation date.

### Actions

- **Start support session:** lets authorized support staff help with an organization's application.
- **Edit:** changes organization information.
- **Sign-in codes:** manages organization sign-in-code settings.
- **Texts:** displays organization text/alert settings; do not test Twilio delivery.
- **Suspend:** temporarily restricts an active organization.
- **Reactivate:** returns a suspended organization to active status.

### Tabs

| Tab | What it shows | Why it matters |
|---|---|---|
| **Subscription** | Plan, status, trial dates, access status, outstanding amount, and people limit. | Explains whether the organization should have normal or restricted access. |
| **Invoices** | Organization invoices and amounts. | Provides a billing record. Do not test PayPal payment actions. |
| **People** | People connected with the organization. | Confirms who belongs to the organization. |
| **Activity** | Recent organization activity. | Helps explain what happened during troubleshooting. |
| **Notes** | Internal Provider notes. | Records useful context without changing operational data. |
| **Alerts** | Organization alert information, where available. | Helps identify issues that need attention. |

### Test it

1. Open each available tab and confirm the organization name stays the same.
2. Add a test note such as `Test note - remove after review`.
3. Refresh and confirm the note remains.
4. Perform a safe test action and look for it under Activity.
5. With a test organization, select Suspend and confirm the status changes.
6. Check the organization's application for the expected restricted behavior.
7. Select Reactivate and confirm normal access returns as expected.

Never suspend a real customer organization during testing.

- [ ] Details and tabs passed
- [ ] Notes passed
- [ ] Suspend/reactivate passed
- [ ] Failed

## 4. Plans

### What it does

Plans contain the offerings available to organizations. The list shows name, code, status, prices, trial period, public visibility, and the number of organizations using each plan. Plans can be searched and filtered. Billing-authorized users can create or change plans and prices.

### Why it is necessary

Plans define what organizations can select and what trial periods or limits apply. Public plans can also be used by tenant registration.

### Test it

1. Open **Plans**.
2. Search by plan name and filter by status.
3. Open a plan and review its description, trial duration, features, limits, and prices.
4. Confirm public plans are marked as shown and console-only plans are not public.
5. If authorized, create a clearly named test plan and confirm it appears once.

Do not test PayPal synchronization or payment processing.

- [ ] Passed
- [ ] Failed

## 5. Billing

### What it does

Billing provides an overview of organization subscriptions. It filters by subscription status and billing mode and shows organization, billing mode, status, trial end, paid-through date, and outstanding amount. It links to Invoices and Reconciliation.

### Why it is necessary

It helps the Provider team understand which organizations have active, trial, expired, or otherwise restricted subscription states.

### Test it

1. Open **Billing**.
2. Filter by status and billing mode.
3. Open an organization and confirm it opens on the Subscription tab.
4. Open Invoices and Reconciliation.
5. Confirm both pages load and show a clear empty state if there is no data.

Do not test PayPal actions.

- [ ] Passed
- [ ] Failed

## 6. Webhook events

### What it does

This displays external service events received by the platform, including date, type, status, and processing errors. An event may have details or a replay action.

### Why it is necessary

It helps technical and billing staff understand whether an external event was received and processed.

### Test it now

1. Open **Webhook events**.
2. Confirm the page loads.
3. Confirm that test events or a clear empty state appear.
4. Open an existing test event if available and confirm its details are readable.

Do not create, replay, or validate PayPal events in this test.

- [ ] Passed
- [ ] Failed
- [ ] Not tested

## 7. Notifications

### What it does

Notifications shows messages generated by the platform. The log filters by email/text channel and status and shows time, template, destination, organization, status, reason, and cost when recorded.

It includes **Templates**, **Opt-outs**, **Spend**, and **Send me a test message**.

### Why it is necessary

It helps the Owner determine whether an important message was created, delivered, delayed, or rejected.

### Test it

1. Open **Notifications** and filter the log by email and status.
2. Open Templates, Opt-outs, and Spend.
3. Use **Send me a test message** only with a test email address.
4. Confirm the email arrives if email is configured.
5. Confirm it appears in the notification log.

Twilio is not implemented. Do not expect real text messages; any text stand-in is record-only.

- [ ] Log passed
- [ ] Templates passed
- [ ] Opt-outs passed
- [ ] Spend passed
- [ ] Test email passed
- [ ] Failed

## 8. Jobs

### What it does

Jobs opens background-job monitoring. Background jobs perform work outside the immediate web request, such as queued notifications or scheduled tasks.

### Why it is necessary

A failed background job can prevent a queued task from completing. This page helps the Owner notice failures.

### Test it

1. Open **Jobs**.
2. Confirm the monitoring page loads.
3. Review job counts and statuses.
4. Compare the failed count with the Dashboard.
5. If a safe test job is available, confirm it completes.

Do not delete or retry production jobs during testing.

- [ ] Passed
- [ ] Failed
- [ ] Not tested

## 9. Audit log

### What it does

The Audit log records important platform activity in newest-first order. Entries cannot be changed or deleted. It filters by Provider user, organization, action, date range, and page size and can download CSV.

### Why it is necessary

It answers who did what, when, and for which organization. This supports accountability and investigation.

### Test it

1. Open **Audit log**.
2. Filter by test organization, Provider user, and date range.
3. Change page size and move between pages.
4. Download the CSV and open it.
5. Add a test organization note.
6. Find that action in the Audit log.

**Expected result:** User, time, action, organization, and details are clear.

- [ ] Passed
- [ ] Failed

## 10. Provider users

### What it does

This manages people who operate the Provider Console. The list shows email, role, status, two-factor status, and date added.

An Owner can invite users, select their role, view details, change roles where permitted, disable users, and reset two-factor setup where permitted.

### Why it is necessary

Provider users have platform-wide access. Correct roles and two-factor security protect every organization.

### Test inviting a user

1. Open **Provider users** and select **Invite**.
2. Enter a test email and select Support.
3. Create the Provider user.
4. Record the temporary password if shown and give it to the tester securely.
5. Sign in as the new user.
6. Confirm the Support user sees allowed menus only and cannot open Owner-only pages.

### Test disabling a user

1. Open a test Provider user.
2. Select **Disable** and confirm.
3. Attempt to sign in as that user.

**Expected result:** The disabled user cannot use the Provider Console. Never disable the only working Owner account.

- [ ] List passed
- [ ] Invite passed
- [ ] Role access passed
- [ ] Disable passed
- [ ] Two-factor status/reset passed
- [ ] Failed

## 11. Settings

Settings is visible to Owners, but the current page is a placeholder for later platform settings.

### Test it

1. Open **Settings**.
2. Confirm that the page opens.
3. Confirm that it clearly indicates settings are not available yet.

Do not expect PayPal or Twilio settings here.

- [ ] Page opens
- [ ] Placeholder is clear
- [ ] Failed

## 12. My phone for codes

This header link manages the Provider user's phone for security codes.

### Test it

1. Open **My phone for codes**.
2. Review the current phone information.
3. Use a test number to follow the available change/verification flow.
4. Confirm that success and error messages are understandable.

Do not change a real user's phone during testing.

- [ ] Passed
- [ ] Failed

## 13. My alerts

This header link controls which Provider alerts the current user receives.

### Test it

1. Open **My alerts**.
2. Review the alert choices.
3. Change one test preference and save it.
4. Leave and return to the page.
5. Confirm that the choice was retained.

- [ ] Passed
- [ ] Failed

## 14. Sign out

1. Select **Sign out**.
2. Press the browser Back button.
3. Refresh the page.

**Expected result:** Private console content is not available until the user signs in again.

- [ ] Passed
- [ ] Failed

---

# First familiarization session

1. Sign in as Owner and complete two-factor verification.
2. Review Dashboard.
3. Open Organizations and a test organization.
4. Review the organization tabs and add a test note.
5. Return to Dashboard and find the recent activity.
6. Review Plans.
7. Review Billing, Invoices, and Reconciliation.
8. Review Notifications, Templates, Opt-outs, and Spend.
9. Review Audit log and find the note action.
10. Review Provider users and role access.
11. Review My phone for codes and My alerts.
12. Open Settings to see the current placeholder.
13. Sign out and confirm that private pages are protected.

# What should be reported

Report the issue if:

- A menu opens the wrong page.
- An organization displays another organization's information.
- A Support user can perform Owner-only actions.
- A disabled Provider user can still sign in.
- A saved note, role, or preference disappears after refreshing.
- Dashboard numbers do not match the related list.
- A completed action is missing from the Audit log.
- A private page remains available after sign-out.
- A page claims PayPal or Twilio is working even though it is not implemented.
- A page fails without a clear explanation.

Do not fix or delete unexpected data before recording the problem.

## Simple issue report

| Item | Details |
|---|---|
| Date and time | |
| Tester | |
| Provider account and role | |
| Menu/page | |
| Test step | |
| Expected result | |
| Actual result | |
| Organization used | |
| Screenshot attached | Yes / No |

Description:

______________________________________________________________________________

______________________________________________________________________________

## Final review

| Area | Passed | Failed | Not tested |
|---|---:|---:|---:|
| Sign-in and two-factor | [ ] | [ ] | [ ] |
| Dashboard | [ ] | [ ] | [ ] |
| Organizations and details | [ ] | [ ] | [ ] |
| Plans | [ ] | [ ] | [ ] |
| Billing | [ ] | [ ] | [ ] |
| Webhook events | [ ] | [ ] | [ ] |
| Notifications | [ ] | [ ] | [ ] |
| Jobs | [ ] | [ ] | [ ] |
| Audit log | [ ] | [ ] | [ ] |
| Provider users | [ ] | [ ] | [ ] |
| My phone for codes | [ ] | [ ] | [ ] |
| My alerts | [ ] | [ ] | [ ] |
| Settings placeholder | [ ] | [ ] | [ ] |
| Sign-out protection | [ ] | [ ] | [ ] |

Overall result:

- [ ] Owner can navigate the Provider Console confidently.
- [ ] Main working features passed.
- [ ] Issues were recorded for the technical team.
- [ ] More work is needed before owner acceptance.

Owner comments:

______________________________________________________________________________

Name: ______________________________________    Date: ______________________
