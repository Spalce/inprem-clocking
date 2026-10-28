# Test Account Provisioning Guide

This document describes the temporary no-email account provisioning feature used to test the .NET 8 Razor Pages application locally and on SmarterASP.

The feature is intended for staging/live testing only. It creates confirmed accounts with passwords and active memberships without sending invitation or verification email.

## What the feature creates

The page is available at:

`/TestProvision`

It supports three account types:

| Account type | Records created | Result |
|---|---|---|
| Provider user | `dbo.AspNetUsers`, Identity role membership, `platform.ProviderUsers` | Provider Console account, initially without MFA enrollment |
| Organization and owner | Organization, tenant profile, free subscription, `dbo.AspNetUsers`, Admin role, active owner membership | Organization owner can sign in directly |
| Organization administrator | `dbo.AspNetUsers`, Admin role, active Admin membership | Administrator can sign in directly to the selected organization |

The supplied username/email does not need to be a deliverable email address, but it must be unique and satisfy the application Identity validation rules. Use test addresses such as `provider-test@example.invalid` or `owner-a@example.invalid`.

## Security requirements

The page is protected by the existing `AdminOnly` policy. The person using it must already be signed in with an account in the `Admin` Identity role.

It is also protected by a separate provisioning secret. The page is unavailable unless the following settings allow it:

```json
{
  "AccountProvisioning": {
	"Enabled": true,
	"AllowInProduction": true,
	"Secret": "a-long-random-secret"
  }
}
```

`AllowInProduction` is only needed when the deployed application runs in the Production environment. In Development or another non-Production environment, `Enabled` is sufficient.

Do not commit the real secret to the repository. Configure it through local user secrets, an environment variable, or the SmarterASP application configuration facility.

For an environment-variable configuration, use:

```text
AccountProvisioning__Enabled=true
AccountProvisioning__AllowInProduction=true
AccountProvisioning__Secret=<long-random-secret>
```

The secret is compared in constant time and is never displayed after submission.

## Local setup before testing

1. Ensure the database connection points to the intended local database.
2. Apply the existing application and platform migrations if the database is new or behind the current branch.
3. Ensure at least one existing back-office Admin account is available. The provisioning page cannot be used until an Admin can sign in.
4. Configure the feature in local user secrets or `appsettings.Development.json`.
5. Do not place a real password or production provisioning secret in committed configuration.
6. Start the application and sign in to the normal back-office login.
7. Open `/TestProvision`.

A local-only configuration can be placed in user secrets with commands similar to:

```powershell
dotnet user-secrets init --project .\InpremClockingApp\InpremClockingApp.csproj
dotnet user-secrets set "AccountProvisioning:Enabled" "true" --project .\InpremClockingApp\InpremClockingApp.csproj
dotnet user-secrets set "AccountProvisioning:Secret" "replace-with-a-long-random-value" --project .\InpremClockingApp\InpremClockingApp.csproj
```

Do not set `AllowInProduction` for local Development unless you are deliberately testing Production-environment behavior.

## Local manual test sequence

### Test 1: Page protection

1. Browse to `/TestProvision` while signed out.
2. Expected result: redirect to the login page.
3. Sign in as a non-Admin organization user.
4. Browse to `/TestProvision`.
5. Expected result: authorization is denied.
6. Sign in as an Admin.
7. Set an incorrect provisioning secret.
8. Expected result: the page remains displayed and reports that the secret is invalid; no account is created.

### Test 2: Create a Provider Owner

1. On `/TestProvision`, choose `Provider user`.
2. Enter a unique username, for example `provider-owner@example.invalid`.
3. Enter a strong temporary password.
4. Select `Owner` as the provider role.
5. Enter the provisioning secret and submit.
6. Expected result: a success message confirms that the Provider account was created and activated.
7. Sign out from the back office.
8. Open the Provider Console login page.
9. Sign in using the provider username and password.
10. Expected result: the account is directed to two-factor enrollment because the provisioning page does not enroll MFA automatically.
11. Complete authenticator enrollment and save the recovery codes.
12. Sign in again with MFA.
13. Expected result: the Provider Console is accessible.

Provider accounts have no organization tenant membership. They are stored as platform-level provider users and should not be used to test organization data isolation.

### Test 3: Create an organization and owner

1. Sign in again as the back-office Admin.
2. Open `/TestProvision`.
3. Choose `Organization and owner`.
4. Enter a unique organization name, such as `Staging Organization A`.
5. Enter a unique slug, such as `staging-org-a`, or leave it blank to generate one.
6. Enter the owner name.
7. Enter a unique username, such as `owner-a@example.invalid`.
8. Enter the owner's initial password.
9. Enter the provisioning secret and submit.
10. Expected result: the organization and owner account are created without an email or invitation.
11. Record the organization ID if it is displayed in the result or in the Provider Console organization detail page.
12. Sign out and sign in as the owner.
13. Expected result: the owner can select or enter the new organization and access organization-admin pages.
14. Verify that the owner can see the new organization's data but not another organization's data.

The organization is created with a free subscription. PayPal is not required for this test path.

### Test 4: Create an organization administrator

1. Use the organization ID from Test 3.
2. Open `/TestProvision` while signed in as the back-office Admin.
3. Choose `Organization administrator`.
4. Enter the organization ID.
5. Enter a new unique username and password.
6. Enter the administrator's first and last names.
7. Enter the provisioning secret and submit.
8. Expected result: a confirmed Identity account and active Admin membership are created.
9. Sign in with the administrator account.
10. Expected result: the administrator can access the selected organization.
11. Verify that the administrator cannot access Provider Console pages.
12. Verify that the administrator cannot access another organization's records.

### Test 5: Validation and duplicate protection

Verify the following:

- Reusing an existing username/email is rejected.
- A weak password is rejected by Identity password rules.
- A nonexistent organization ID is rejected.
- A missing organization name or invalid slug is rejected.
- A missing or incorrect provisioning secret creates no account.
- A provider account cannot be created as an organization member through the owner flow.
- A second account cannot use the same platform-wide email.

### Test 6: Disable the feature

1. Set `AccountProvisioning:Enabled` to `false`.
2. Restart the application.
3. Browse to `/TestProvision`.
4. Expected result: HTTP 404.
5. Confirm that normal login, Provider Console access, and organization administration still work.

## SmarterASP deployment procedure

### Before upload

1. Take a database backup.
2. Confirm that the target database contains the migrations required by the current branch.
3. Confirm that an existing Admin account can sign in, or arrange a separate approved bootstrap process before deployment.
4. Publish the application in the normal Release configuration.
5. Do not include production passwords or the provisioning secret in source control.
6. Prepare a new, long, random provisioning secret for this deployment.

### SmarterASP configuration

Configure these values using the hosting provider's application settings or environment configuration:

```text
AccountProvisioning__Enabled=true
AccountProvisioning__AllowInProduction=true
AccountProvisioning__Secret=<new-long-random-secret>
```

Also confirm the normal production settings independently:

- `ConnectionStrings:DefaultConnection`
- Identity and cookie/data-protection key storage settings
- `Site:SiteMode`
- `Site:AppHost` and `Site:AdminHost`, if host restrictions are enabled
- HTTPS configuration
- PayPal settings only if billing is being tested
- Email/Twilio settings are not required for this provisioning flow
- Hangfire settings, if background jobs are enabled

If SmarterASP does not provide environment-variable configuration, use its application settings facility or a deployment-specific configuration file that is not committed to Git. Protect configuration files containing connection strings and secrets from public download.

### After upload

1. Restart the application pool/application.
2. Browse to `/TestProvision` while signed out.
3. Confirm the page requires login.
4. Sign in as an Admin.
5. Confirm that an incorrect secret fails.
6. Create a uniquely named Provider Owner, organization owner, and organization administrator.
7. Complete Provider MFA enrollment using an authenticator application. This step does not use email or Twilio.
8. Test normal organization login and tenant isolation.
9. Check the application logs and database for errors.
10. Confirm that no email configuration is needed for the accounts created through this page.

### After testing

Immediately disable the feature:

```text
AccountProvisioning__Enabled=false
AccountProvisioning__AllowInProduction=false
```

Restart the application and verify `/TestProvision` returns 404. Rotate or delete the provisioning secret. If the test database will be reused, delete the test accounts and organizations through an approved cleanup procedure or restore the pre-test backup.

Do not leave this feature enabled for customer production use. It bypasses email ownership verification and invitation workflows by design.

## Database and migration impact

No new database migration is required for this implementation.

The feature uses existing tables and services:

- `dbo.AspNetUsers`
- `dbo.AspNetRoles`
- `dbo.AspNetUserRoles`
- `dbo.Tenants`
- `platform.TenantProfiles`
- `platform.Subscriptions`
- `platform.TenantMemberships`
- `platform.ProviderUsers`

The provisioning page and service do not add or alter columns, indexes, foreign keys, or tables. They only insert data into the existing schema. Do not create a migration merely because `TestAccountProvisioner` or `TestProvision` was added.

You still need to apply any migrations that were already pending in the target database from the current `mt/phase7` branch. Check the application and platform migration histories independently because the solution has both the existing application context and the platform context.

A normal migration is required only if a later code change modifies an EF model or schema. The current provisioning implementation does not do that.

## Cleanup checks

After disabling the feature, review the test data:

- Test `AspNetUsers` accounts
- Their `AspNetUserRoles` rows
- `platform.ProviderUsers` rows
- Test `dbo.Tenants` rows
- Test `platform.TenantProfiles` rows
- Test `platform.Subscriptions` rows
- Test `platform.TenantMemberships` rows
- Related audit entries

Prefer restoring a disposable staging database backup when possible. If deleting records manually, preserve foreign-key order and use the application's existing administrative operations where available.

## Acceptance checklist

- [ ] Page is inaccessible while signed out.
- [ ] Page is inaccessible to a non-Admin user.
- [ ] Invalid provisioning secret creates nothing.
- [ ] Provider Owner can be created without email.
- [ ] Provider Owner can enroll MFA through the authenticator app.
- [ ] Provider Console access works after MFA enrollment.
- [ ] Organization owner can be created without email.
- [ ] Organization owner has an active owner membership.
- [ ] Organization administrator can be created without email.
- [ ] Tenant isolation works between at least two organizations.
- [ ] Duplicate usernames are rejected.
- [ ] `/TestProvision` returns 404 after disabling the setting.
- [ ] Production provisioning secret has been rotated or removed.
- [ ] No migration was generated for this feature.
