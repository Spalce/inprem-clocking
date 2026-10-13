# Runbook: Provider Owner bootstrap and recovery

Design section 5; decisions D13–D16. Built in Phase 0 step 0.7.

This runbook is for:
- **the operator**: the person with access to the server or its control panel;
- **the approvers**: the people listed in `Security:RecoveryApprovers` (the project owner and the CTO).

The developer rehearses every part of it on a test site (not Claude Code) before go-live. After that, Levels 1–3 are drilled every six months (section 7).

**Rules that never change:**
- No email or password is ever put in code, `appsettings.json`, the repository, or a chat message.
- Codes go in the secret store. The server holds only their **hashes**.
- Every action below is written to `platform.AuditEntries`. The audit log can't be edited, even by a database administrator through the app's account.
- Every action below is emailed to all active Provider Owners and to `Security:Mailbox`.
- The commands and pages never print or show a code, a link or a token. Links only go by email.

---

## 1. Server settings

Set these in the host's configuration: environment variables, or a server-only `appsettings.Production.json` that isn't in the repository. Locally, use `dotnet user-secrets`. In environment variables, `:` becomes `__`, for example `Bootstrap__CodeHash`.

| Setting | What | Example |
|---|---|---|
| `Bootstrap:CodeHash` | SHA-256 (hex) of the one-time **bootstrap code**. Remove it once the first owner is set up. | `9f2c…` (64 hex characters) |
| `Security:BreakGlassCodeHash` | SHA-256 (hex) of the one-time **break-glass code** (Levels 3 and 4). Replaced after every use and every six months. | `41ad…` |
| `Security:RecoveryApprovers` | Who may approve Levels 3 and 4: a JSON array, or one comma-separated value. | `owner@company.com,cto@company.com` |
| `Security:Mailbox` | Security mailbox that receives every notification. | `security@company.com` |
| `Links:AdminBaseUrl` | Base address of the Provider Console, used in emailed links. | `https://admin.inpremclockin.org`, or `https://test.example.org/Provider` on a site with `Site:SiteMode` = `All` |

**Before you start:**
- Platform migration M1b is applied (`scripts/migrations/README.md`, row 5).
- The site can send email. Every link in this runbook arrives by email.

### Creating a code and its hash

Use PowerShell 7 (`pwsh`) on a trusted machine. Don't save the output to a file.

```powershell
$alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
$code = (1..6 | ForEach-Object { -join (1..5 | ForEach-Object { $alphabet[[System.Security.Cryptography.RandomNumberGenerator]::GetInt32(32)] }) }) -join '-'
$hash = -join ([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($code)) | ForEach-Object { $_.ToString('x2') })
"CODE (to the secret store only): $code"
"HASH (to the server setting):     $hash"
Clear-History
```

1. **The code:** the CTO puts it in the secret store, for example Azure Key Vault. The vault logs every read. The break-glass code is readable only by the CTO and the project owner.
2. **The hash:** the operator puts it in the server setting.
3. **Clean up:** close the PowerShell window.

---

## 2. First Provider Owner (bootstrap)

Bootstrap only works while no Provider Owner exists and `Bootstrap:CodeHash` is set. Choose route A or B.

### Route A: server command (hosts with a console)

Run this from the site folder, with the same environment as the site (for example `ASPNETCORE_ENVIRONMENT=Production`):

```powershell
dotnet InpremClockingApp.dll provider bootstrap --email owner@company.com --dry-run   # checks only, changes nothing
dotnet InpremClockingApp.dll provider bootstrap --email owner@company.com             # asks for the code
```

- **The code:** leave out `--code` and the command asks for it, which keeps it out of the shell history. `--code <code>` also works.
- **Output:** `OK: Done…` or `REFUSED: <reason>`.
- **Exit codes:** 0 done, 1 refused, 2 wrong arguments. The web server is not started.

### Route B: one-time setup page (hosts without a console, such as SmarterASP)

1. Open `https://<admin host>/setup`, or `https://<site>/Provider/setup` when `Site:SiteMode` is `All`.
   - **"Page not found"** means bootstrap is already done, or `Bootstrap:CodeHash` isn't set.
2. Enter the owner's email and the bootstrap code.
3. **Rate limit:** after 5 wrong codes in an hour, the page refuses everything for an hour. Every attempt is audited with the visitor's address.

### Both routes, then

1. The owner receives "Set up your provider account". The link works once and is valid for **24 hours**.
2. The owner opens the link and sets a password.
3. The owner adds the key shown to an authenticator app (Microsoft Authenticator, Google Authenticator or similar) and enters the 6-digit code.
4. **Ten recovery codes** are shown **once**. The owner stores them offline: printed and locked away, or in a personal password manager.
5. The console opens. Until a second owner exists it shows a red "Not ready for production" warning, and `/health` answers `Degraded`.
6. **The CTO deletes the bootstrap code from the vault, and the operator removes `Bootstrap:CodeHash`.** The code is already marked as used and can't work again.

**If the 24-hour link expired unused:**
1. Create a new code and hash (section 1) and set the new hash.
2. Run bootstrap again for the same email. That person is invited again; no second account is created.
3. The old code is refused for good.

**Second owner (D16, required before production):** inviting provider users is a Provider Console screen in Phase 1. Until then the platform runs with one owner, and the warning stays.

---

## 3. Which recovery level?

| Situation | Level |
|---|---|
| Lost the authenticator, still has recovery codes | **1**, self-service |
| Lost authenticator and codes; another owner can act | **2**, peer reset (console, Phase 1) |
| No other owner can act; the owner still controls their mailbox | **3**, break-glass reissue |
| Owner left, or lost the mailbox | **4**, break-glass replace |

At most **one recovery per account per 24 hours** (Levels 2–4).

---

## 4. Level 1: self-service (the owner alone)

1. Sign in with email and password.
2. On the code page, choose **Use a recovery code** and enter one of the saved codes.
3. You can now only reach the setup page. Your old authenticator is already disabled.
4. Add the new key to your authenticator app and enter a code.
5. Store the **ten new recovery codes**. The old ones no longer work.

You, the other owners and the security mailbox get an email.

---

## 5. Level 3: break-glass reissue

**Who:**
- the operator, with server access;
- the CTO or the project owner, who provides the break-glass code;
- one approver from `Security:RecoveryApprovers`. The approver can't be the owner being recovered.

**Operator:**

```powershell
dotnet InpremClockingApp.dll provider recover --user owner@company.com --approver cto@company.com --dry-run
dotnet InpremClockingApp.dll provider recover --user owner@company.com --approver cto@company.com    # asks for the break-glass code
```

- **On success,** the output names the **recovery id**, and the break-glass code is now used.
- **Refusals** that change nothing:
  - **Not an owner:** the email doesn't belong to a Provider Owner.
  - **Email never verified:** the owner's email was never verified. Use Level 4.
  - **Already recovered:** the account was recovered in the last 24 hours.
  - **Unknown approver:** the approver isn't listed in `Security:RecoveryApprovers`.
- **Lock:** a wrong code or an unlisted approver counts as a failure. **Three failures in 24 hours lock the command for 24 hours** and alert all owners and the security mailbox.

**Approver:**
1. You receive "Approve provider recovery (Level 3)". The link is valid for **1 hour**.
2. Opening it only **shows** the request: who, which owner, and what will happen.
3. Press **Approve** only if you know about the request and agree.
4. If the hour has passed, nothing happens. The operator starts again with a **new** break-glass code.

**What happens after approval:**
- The owner's password and two-factor are removed, and **every session is signed out at once**.
- Any pending links for that account are withdrawn.
- A link valid for **1 hour** goes to the owner's **existing, verified email only**. The owner sets a new password and enrols a new authenticator (section 2, steps 2–4).

**Then:** section 8 (after every recovery).

---

## 6. Level 4: break-glass replace

**Who:**
- all of Level 3, with **two** approvers (the project owner and the CTO);
- their **written approval**, signed and stored. Its reference goes into the audit entry.

**Operator:**

```powershell
dotnet InpremClockingApp.dll provider recover --replace --old leaving@company.com --new-email new.owner@company.com `
  --approver owner@company.com --approver cto@company.com --approval-ref "Letter of 2026-10-13, ref SEC-12" --dry-run
# then the same without --dry-run (asks for the break-glass code)
```

- **New owner's email:** it must not have an account yet. Use a dedicated address.

**Each approver:**
1. Open the link (1 hour).
2. Approve.
3. Tick **Waive the 24-hour delay** only if you both agree it's urgent.

**The delay:**
- **Both approvers waived it:** the replacement runs as soon as the second one approves.
- **Otherwise,** after 24 hours, the operator completes it:

  ```powershell
  dotnet InpremClockingApp.dll provider recover --complete --id <recovery id> --dry-run
  dotnet InpremClockingApp.dll provider recover --complete --id <recovery id>
  ```

**What happens:**
- The old owner is **disabled**: they can't sign in, and every session is signed out.
- The new owner receives a 24-hour setup link (section 2, steps 2–4).

**Then:** section 8.

---

## 7. Level 2: peer reset

The use case exists and is tested. The console screen for it comes in Phase 1.

**How it works:**
- Another **active** Provider Owner re-enters their own authenticator code and gives a reason.
- The person's two-factor is reset, and every session is signed out.
- At next sign-in, the person enrols a new authenticator.
- Owners can't reset themselves.

---

## 8. After every recovery (Levels 2–4)

1. **Codes:** for Levels 3 and 4, create a **new break-glass code** (section 1). Put it in the vault and its hash on the server; the used one never works again.
2. **Audit log:** check `platform.AuditEntries` for the recovery (actions `provider.recovery.*`).
3. **Report:** within **24 hours**, write the recovery report: who, why, what changed, and the times. The CTO signs it.
4. **Notifications:** confirm every Provider Owner and the security mailbox received them.

---

## 9. Drill (every six months, and before go-live)

Run on **staging**, never on production. Record each drill in the table below.

| Step | What to check |
|---|---|
| Bootstrap (staging only, on a fresh database) | Route A or B works; the link arrives; the console warns about the second owner. |
| Level 1 | A recovery code signs in; the old authenticator stops working; ten new codes are issued; the emails arrive. |
| Level 3 dry run | `--dry-run` shows the plan and changes nothing. |
| Level 3 | The approval email arrives; the owner is signed out at once on approval; the 1-hour link works; a second attempt within 24 hours is refused. |
| Break-glass code | A new code is created and set (section 8). |

| Date | Who (operator / approver) | Levels drilled | Time taken | Result / issues |
|---|---|---|---|---|
| | | | | |

---

## 10. Refusal messages

| Message | Meaning |
|---|---|
| Bootstrap is not configured on this server | `Bootstrap:CodeHash` isn't set. |
| A Provider Owner already exists; bootstrap is disabled | Use the console (Phase 1) or a recovery level instead. |
| The bootstrap code is not valid / has already been used | Wrong code, or a used one; create a new code if needed. |
| An account with this email already exists | Use a dedicated address for the provider account. |
| Links:AdminBaseUrl is not configured | Set it (section 1). The setup page uses its own address instead. |
| Too many failed attempts | Setup page: wait an hour. |
| The recover command is locked for 24 hours | Three failed break-glass attempts. Investigate before trying again. |
| This account was already recovered in the last 24 hours | The rate limit; wait. |
| That owner's email was never verified | Level 3 only sends to a verified email; use Level 4. |
| This approval link has expired | Start again with a new break-glass code. |
