# Phase 0 manual test (local)

For the developer. It covers what Phase 0 makes visible, and exit check P0-6 (bootstrap and recovery Levels 1–3 executed by the developer). Write each result in the last column. Report anything unexpected with the step number.

## Already done for you (2026-10-13)

- **Backup:** `DB_A65635_inpremdb` on `localhost`, backed up first to `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\DB_A65635_inpremdb_before_phase0_20261013.bak` (verified).
- **Schema:** `scripts/migrations/phase0-up.sql` applied; all six verify scripts gave the expected values.
- **Your duplicate account was merged.** `b2kbans@gmail.com` had two admin accounts (AKIM ASAFO SHS, AKIM ASAFO). It is now one account, the one from AKIM ASAFO SHS with its password, and Owner of both organizations.
- **Data unchanged:** staff, volunteers, sessions and hour totals are exactly as before.
- **user-secrets:** `Site:SiteMode = All` (App and Provider pages on one local site) and `Jobs:RunServer = true`.
- **To undo all of it:** restore the backup above.

## Before you start

1. **Bootstrap code and hash:** create them with PowerShell 7 (the runbook snippet, `docs/multitenancy/runbooks/bootstrap-and-recovery.md` section 1). Keep the code somewhere private for this test, and store the hash:
   ```powershell
   cd InpremClockingApp
   dotnet user-secrets set "Bootstrap:CodeHash" "<the 64-character hash>"
   ```
2. **Optional, to see the notification emails:** `dotnet user-secrets set "Security:Mailbox" "<your email>"`.
3. **Start the app:** `dotnet run --project InpremClockingApp` and use **https://localhost:7078**. Sign-in cookies are secure-only, so plain http won't keep you signed in.

**About emails:** emails really go out, through the app's current mailbox (the `EmailService` account still waiting on its owner's confirmation). Use only your own addresses. For the provider owner, use a **different address** from your admin account; a Gmail `+` address works, for example `b2kbans+provider@gmail.com`.

## A. Organization admins (App pages)

| # | Do | Expect | Result |
|---|---|---|---|
| A1 | Sign in at `/Identity/Account/Login` as `b2kbans@gmail.com` | The **Choose organization** page, listing AKIM ASAFO SHS and AKIM ASAFO | |
| A2 | Pick one; open the back office, staff list and a report | That organization's data only, as before | |
| A3 | Sign out; sign in and pick the other one | The other organization's data only | |
| A4 | In a private window, enter a wrong password 5 times for an admin | Locked for 15 minutes (lockout page); the right password is refused until then | |
| A5 | Admins page (`/User`): add an admin with an email that already has an account in another organization | Message that an invitation was sent; no second account | |
| A6 | Sign in as that invited person; open `/Account/Invitations` | The invitation; **Accept** adds the organization (Choose organization at next sign-in) | |
| A7 | Forgot password from the login page | Reset email arrives; the link sets a new password | |

## B. Provider (P0-6): first owner, two-factor, Level 1

| # | Do | Expect | Result |
|---|---|---|---|
| B1 | Open `/Provider/setup` | The setup page. If 404, the hash isn't set or the app wasn't restarted. | |
| B2 | Enter your provider email and a **wrong** code | "The bootstrap code is not valid." | |
| B3 | Enter the right code | "Done. A setup link was emailed…"; reloading `/Provider/setup` now gives **404** | |
| B4 | Open the emailed link "Set up your provider account" | Page for your provider email; set a password | |
| B5 | Next page: add the key to an authenticator app, enter the code | Ten recovery codes, shown once. **Write two of them down** for B9. | |
| B6 | Continue | Provider Console with a red "Not ready for production: 1 of the required 2…" warning | |
| B7 | `/health` | `Degraded` | |
| B8 | Sign out; sign in at `/Provider/Login` | Password, then the authenticator code, then the console | |
| B9 | Sign out; sign in with password, choose **Use a recovery code**, enter one | Only the two-factor setup page opens; the console redirects there. An email says a recovery code was used. | |
| B10 | Set up the authenticator again (new key) | Ten new codes; the old app entry and the other old codes no longer work | |
| B11 | `/Provider/jobs` while signed in to the console | The read-only Hangfire dashboard (1 server); no delete or requeue buttons | |
| B12 | Sign in at `/Provider/Login` with `b2kbans@gmail.com` (the organization admin) | "Invalid sign-in attempt." | |

## C. Provider recovery Levels 2 and 3 (P0-6)

- **Level 2:** it has no screen until Phase 1. Its safeguards are covered by the automated tests, so there's nothing to click.
- **Level 3:** it uses the server command and needs extra setup. Do it on the **test site** when you deploy, following the runbook section 5, and record it here.

| # | Do | Expect | Result |
|---|---|---|---|
| C1 | Level 3 on the test site (runbook section 5), approver = your second address | The approval email; after approval, signed out at once; a 1-hour link restores access | |

## When you're done

1. **Record:** add the date and a one-line summary under "Manual / sandbox test records" in `docs/multitenancy/build/PROGRESS.md`, or tell me the results and I'll record them.
2. **Optional cleanup:** your user-secrets still hold the old `SeedAdmin:Email` and `SeedAdmin:Password`. The app no longer reads them since step 0.6. Remove them with:
   ```powershell
   dotnet user-secrets remove "SeedAdmin:Email"
   dotnet user-secrets remove "SeedAdmin:Password"
   ```
