# Volunteer Registration API Deployment Guide

This guide covers the deployment and operational steps for the public volunteer registration API. Frontend integration instructions are documented separately in `docs/API-FRONTEND-INTEGRATION-GUIDE.md`.

## 1. Before deployment

Confirm that the deployment includes:

- `POST /api/public/v1/volunteers`.
- Volunteer approval status support.
- The EF Core migration for volunteer approval columns and tenant contact fields.
- Updated API documentation and OpenAPI contract.
- Volunteer API integration tests.
- Provider Console support for reviewing and approving registrations, if registrations are being enabled for production users.

Review the working tree and changed files:

```powershell
git status
git diff --stat
git diff
```

Do not deploy unrelated changes accidentally included in the branch.

## 2. Build and test

Build the complete solution, not only the web project:

```powershell
dotnet build InpremClockingApp.sln
```

Run the volunteer API tests:

```powershell
dotnet test tests/Inprem.Platform.Tests.Integration/Inprem.Platform.Tests.Integration.csproj --filter FullyQualifiedName~VolunteerRegistrationTests
```

Run the complete test suite before production deployment:

```powershell
dotnet test InpremClockingApp.sln --no-build
```

The volunteer registration tests should cover successful registration, pending status, validation, CAPTCHA, duplicate handling, tenant isolation, and idempotency.

## 3. Verify the migration

List the application migrations:

```powershell
dotnet ef migrations list `
  --project InpremClockingApp `
  --startup-project InpremClockingApp `
  --context ApplicationDbContext
```

Review the migration and its designer/snapshot files. Confirm that it:

- Adds `Phone` and `Email` tenant columns when required.
- Preserves existing tenant contact data during the split.
- Adds `ApprovalStatus` to `Volunteers`.
- Adds `ApprovedAt` and `ApprovedBy` to `Volunteers`.
- Marks existing volunteers as `Approved`.
- Allows new volunteers to default to `Pending`.
- Does not contain duplicate migration classes or duplicate migration timestamps.

Generate an idempotent SQL script for review:

```powershell
dotnet ef migrations script `
  --project InpremClockingApp `
  --startup-project InpremClockingApp `
  --context ApplicationDbContext `
  --idempotent `
  --output volunteer-registration-deploy.sql
```

Review the generated SQL before applying it to a shared or production database.

## 4. Back up the database

Before applying the migration:

1. Confirm the production connection string points to the intended database.
2. Take a verified database backup.
3. Confirm that the backup can be restored or that the restore process is documented.
4. Record the backup time and migration version.

The tenant contact migration may remove the legacy `ContactInfo` column after copying values into separate fields. Do not skip the backup.

## 5. Test against staging first

Apply the reviewed migration to a staging or disposable database:

```powershell
dotnet ef database update `
  --project InpremClockingApp `
  --startup-project InpremClockingApp `
  --context ApplicationDbContext
```

Verify the database:

- `Volunteers.ApprovalStatus` exists.
- `Volunteers.ApprovedAt` exists.
- `Volunteers.ApprovedBy` exists.
- Existing volunteers have status `Approved`.
- A new API registration is saved with status `Pending`.
- Tenant phone and email values are preserved separately.
- Existing kiosk and Provider Console volunteer operations still work.

For production, prefer applying the reviewed SQL script through the normal database deployment process rather than allowing the application startup to perform schema changes automatically.

## 6. Configure the API environment

The deployment owner must configure:

- The correct database connection string.
- The Cloudflare Turnstile secret for the target environment.
- The frontend Turnstile site key for the target environment.
- Allowed frontend origins under the public website CORS configuration.
- Public API rate limits.
- Any API key configuration used by the website server.
- Email configuration if volunteer notifications are enabled.

Never provide the Turnstile secret, database password, SMTP password, or bearer tokens to frontend developers.

## 7. Manual API smoke test

Use the frontend or an HTTP client with a real CAPTCHA token from the same environment.

Request:

```http
POST /api/public/v1/volunteers
Content-Type: application/json
Idempotency-Key: a-new-unique-key
```

Example body:

```json
{
  "tenantId": 123,
  "emailAddress": "volunteer@example.com",
  "firstName": "Ama",
  "lastName": "Owusu",
  "zipCode": "43229",
  "gender": "F",
  "phoneNumber": "555-0100001",
  "address": "123 Main Street",
  "volunteerCategory": "Personal or Individual Volunteering",
  "consent": true,
  "captchaToken": "FRESH-TURNSTILE-TOKEN"
}
```

Expected response:

```http
202 Accepted
```

```json
{
  "registrationId": 123,
  "status": "pending_approval",
  "message": "Your volunteer registration has been submitted for review."
}
```

Also verify:

- Repeating the exact request with the same `Idempotency-Key` returns the original response.
- Reusing the key with a different body returns `422` and `idempotency_key_reused`.
- Invalid CAPTCHA returns `400` and `captcha_failed`.
- An unknown tenant returns `404` and `tenant_not_found`.
- Duplicate email returns `409` and `volunteer_email_already_registered`.
- Duplicate phone returns `409` and `volunteer_phone_already_registered`.
- Rate limiting returns `429` with `Retry-After` when the configured limit is exceeded.

Do not reuse an old production CAPTCHA token. CAPTCHA tokens are short-lived and environment-specific.

## 8. Provider Console verification

After a successful API registration:

1. Sign in to the Provider Console or organization administration area.
2. Locate the new volunteer registration.
3. Confirm it is marked `Pending`.
4. Confirm the volunteer belongs to the intended organization.
5. Approve the volunteer.
6. Confirm `ApprovedAt` and `ApprovedBy` are populated.
7. Confirm the volunteer is available to the organization only after approval.
8. Test rejection if the workflow supports rejection.
9. Confirm the relevant audit entry is recorded.

Do not enable production volunteer registration if administrators have no way to review or approve pending registrations.

## 9. Deployment order

Use this order for production:

1. Build and test the application.
2. Back up the production database.
3. Apply the reviewed database migration.
4. Verify the migration completed successfully.
5. Deploy the application version containing the API.
6. Verify the health endpoint.
7. Run the volunteer API smoke test.
8. Verify Provider Console approval.
9. Monitor application logs, database errors, rate limits, CAPTCHA failures, and registration counts.

Deploy the database migration and application together. The application expects the approval columns to exist.

## 10. Rollback considerations

If the application deployment must be rolled back:

- Do not automatically roll back the database migration unless the rollback has been tested.
- Preserve volunteer registrations created after deployment.
- Confirm the previous application version can tolerate the new columns.
- If the migration removes or renames columns, use the verified database backup or a tested down migration procedure.
- Record any registrations that require reconciliation after rollback.

Database rollback should be performed by the database owner using the approved recovery process.

## 11. Frontend handoff

Give frontend developers:

- The environment-specific API base URL.
- The Turnstile site key only.
- The volunteer endpoint documentation.
- The required request fields.
- The pending-approval response behavior.
- The idempotency-key requirement.
- The documented error codes.

Refer them to:

- `docs/API-FRONTEND-INTEGRATION-GUIDE.md`
- `docs/API-ENDPOINTS.md`

Do not give frontend developers database credentials, CAPTCHA secrets, or internal Provider Console credentials.

## 12. Production readiness checklist

- [ ] Full solution build passes.
- [ ] Full test suite passes.
- [ ] Volunteer API tests pass.
- [ ] Migration reviewed.
- [ ] Production database backup completed and verified.
- [ ] Migration tested on staging.
- [ ] Turnstile production configuration verified.
- [ ] CORS origin verified.
- [ ] Rate limits verified.
- [ ] API smoke test completed.
- [ ] Pending registration appears in Provider Console.
- [ ] Approval workflow tested.
- [ ] Audit event verified.
- [ ] Monitoring and rollback contacts confirmed.
