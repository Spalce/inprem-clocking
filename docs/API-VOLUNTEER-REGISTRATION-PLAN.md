# Volunteer Registration API Plan

## 1. Objective

Build a public API that allows website users to register as volunteers for an organization. The API should follow the same architecture and operational patterns as the existing tenant-registration API.

The first version will use these decisions:

- The API client supplies the organization `tenantId`.
- Registration is public and does not require volunteer authentication.
- New registrations are created with a pending-approval status.
- The request collects the existing volunteer model fields.

No implementation changes are included in this plan.

## 2. Existing API patterns to reuse

Use the existing public API foundation under `/api/public/v1`:

- ASP.NET Core minimal API endpoint contributors.
- RFC 7807 problem responses.
- Stable error codes and field-level validation details.
- CORS configuration.
- Rate limiting with `Retry-After`.
- Required `Idempotency-Key` for creation requests.
- Request-body hashing and replay protection.
- Cloudflare Turnstile CAPTCHA verification.
- OpenAPI/Swagger metadata.
- Public API integration tests and fixture setup.

The volunteer endpoint should be a separate public API feature. It should not be added to the provider/admin-only `PeopleController`.

## 3. Endpoint surface

### Register a volunteer

```http
POST /api/public/v1/volunteers
```

Required headers:

```http
Content-Type: application/json
Idempotency-Key: <unique-key>
```

The endpoint should require a CAPTCHA token using the same convention as the tenant-registration API.

### Optional registration-status endpoint

If the website needs to display progress, add:

```http
GET /api/public/v1/volunteers/registrations/{registrationId}
```

This endpoint must not expose personal information without a secure registration token. It may be omitted from the first version if the website only needs a confirmation response.

## 4. Organization identification

The client will provide the organization tenant ID in the request.

Example:

```json
{
  "tenantId": 123,
  "firstName": "Ama",
  "lastName": "Owusu",
  "email": "ama@example.com"
}
```

The service must validate that:

- The tenant exists.
- The tenant is active.
- The tenant accepts volunteer registrations.
- The caller cannot use the tenant ID to access another tenant's data.
- The tenant ID is not used to expose organization information unnecessarily.

Before implementation, evaluate whether a public slug or registration code would be safer than exposing an internal numeric tenant ID. If `tenantId` remains the contract, apply strict rate limiting and generic error responses.

## 5. Request contract

Use a dedicated request DTO. Do not bind the EF `Volunteer` entity directly from public input.

The request should match the existing `Volunteer` model and kiosk registration flow, including:

### Identity and contact

- `tenantId`
- First name
- Last name
- Email
- Phone
- Address and other existing contact fields

### Volunteer classification

- Volunteer category
- Mandate, when applicable
- Institution, when applicable
- Workplace, when applicable
- Contact person, when applicable

### Consent and protection

- Privacy or terms consent, when required by the existing application.
- Optional communication consent, if already supported.
- CAPTCHA token.

Example conceptual payload:

```json
{
  "tenantId": 123,
  "firstName": "Ama",
  "lastName": "Owusu",
  "email": "ama@example.com",
  "phone": "555-0100",
  "address": "1 Main Street",
  "category": "Community",
  "mandate": null,
  "institution": null,
  "workplace": null,
  "contactPerson": null,
  "captchaToken": "token-from-turnstile",
  "consent": true
}
```

Exact property names and required fields must be aligned with the current `Volunteer` entity and UI conventions during implementation.

## 6. Pending-approval workflow

A successful request should create a pending volunteer registration, not an active volunteer.

The application service should:

1. Validate the tenant.
2. Validate the submitted volunteer fields.
3. Apply the existing category-dependent validation rules.
4. Check for duplicate records within the tenant.
5. Create the volunteer with pending status.
6. Set UTC creation timestamps.
7. Save the volunteer transactionally.
8. Record an audit event.
9. Optionally notify organization administrators.
10. Return a safe confirmation response.

The Provider Console remains responsible for reviewing, approving, rejecting, and editing pending volunteers.

If the current model has no equivalent status, add a status such as:

- `Pending`
- `Approved`
- `Rejected`

Also add approval/rejection metadata only if the existing model does not already provide it.

## 7. Validation

Create or extend an application-level volunteer registration service. Do not put the complete workflow in the endpoint or Razor Page.

Reuse or extract the rules from:

```text
InpremClockingApp/Helpers/VolunteerCategoryValidation.cs
```

Validate:

- Tenant ID.
- Required name fields.
- Email format and length.
- Phone format and length.
- All database field length limits.
- Consent requirements.
- CAPTCHA presence.
- Volunteer category.
- Category-dependent mandate, institution, workplace, and contact-person fields.

Return field-level validation details through the existing problem response format.

## 8. Duplicate handling

Use the current kiosk registration flow as the starting point. Check duplicates within the supplied tenant for:

- Email.
- Phone, if the existing workflow treats it as unique.
- Matching name and contact details, if applicable.

Database uniqueness constraints must remain the final protection against concurrent requests.

Recommended stable error codes include:

- `tenant_not_found`
- `tenant_not_active`
- `volunteer_already_registered`
- `volunteer_duplicate_contact`
- `registration_pending`
- `validation_failed`

Public responses should avoid revealing unnecessary personal-data existence details.

## 9. Security and abuse protection

Because registration is public and unauthenticated, apply the same protections as tenant signup.

### CAPTCHA

Use the existing server-side Turnstile verifier:

- Verify the token server-side.
- Forward the remote client IP where supported.
- Fail closed when configuration is missing or verification fails.
- Do not expose provider-specific CAPTCHA errors.

### Rate limiting

Add a volunteer-specific rate-limit policy based on a combination of:

- Client/API key, where applicable.
- IP address.
- Tenant ID.
- Endpoint.

Do not use tenant ID as the only limiter.

### CORS

Add the endpoint to the existing public API CORS policy. Production should allow only approved website origins.

### Idempotency

Require `Idempotency-Key` on `POST /volunteers`:

- Same key and same body returns the original response.
- Same key and different body returns `422`.
- Concurrent requests create only one volunteer.
- Successful responses can be replayed for the configured retention period.
- Failed server responses do not permanently consume the key.

## 10. Response contract

Return a minimal accepted response:

```http
HTTP/1.1 202 Accepted
```

```json
{
  "registrationId": "guid",
  "status": "pending_approval",
  "message": "Your volunteer registration has been submitted for review."
}
```

Do not return:

- Internal tenant details.
- Other volunteer records.
- Administrative information.
- Passwords or credentials.
- Sensitive audit information.

## 11. Persistence and tenant isolation

Review the current `Volunteer` entity and database configuration before implementation.

Confirm:

- All required fields already exist.
- The tenant foreign-key relationship is correct.
- Global tenant query filters do not block or bypass public registration incorrectly.
- The public service establishes tenant scope safely.
- The caller-controlled tenant ID is validated before persistence.
- Composite keys and uniqueness constraints are preserved.
- Approval status and metadata exist or are added through a migration.

Add a migration only when the model requires new database fields or indexes.

The public registration path must never rely solely on a caller-controlled tenant context.

## 12. Service and storage design

Add dedicated application contracts, for example:

- `VolunteerRegistrationRequest`
- `VolunteerRegistrationResult`
- `VolunteerRegistrationService`

The service should coordinate:

- CAPTCHA verification.
- Tenant lookup.
- Validation.
- Duplicate detection.
- Volunteer creation.
- Audit logging.
- Optional notifications.

Use a tenant-safe store operation such as `RegisterPendingAsync`. It should:

- Apply the tenant ID explicitly.
- Create the pending volunteer.
- Enforce tenant scope.
- Handle uniqueness conflicts.
- Save the registration and audit entry transactionally.

Do not call the existing entity-based `VolunteerService` directly from the endpoint unless its responsibilities are expanded safely.

## 13. Notifications

Decide whether organization administrators should be notified when a registration is submitted.

Recommended behavior:

- Persist the pending registration even if notification delivery fails.
- Log notification failures.
- Do not convert a successful database write into an API failure solely because email delivery failed.
- Use an existing Hangfire background job pattern if asynchronous delivery is needed.

Potential notifications:

- Organization administrators receive a new pending-registration notification.
- The volunteer receives a confirmation email when email collection and consent requirements allow it.

## 14. Audit events

Add an audit event such as:

```text
volunteer.registration.submitted
```

The event should include:

- Tenant ID.
- Volunteer ID.
- Registration status.
- API/request source.
- Timestamp.
- A non-sensitive summary.

Do not store CAPTCHA tokens or unnecessary personal data in audit descriptions.

Use separate events for later workflow actions if needed:

- `volunteer.registration.approved`
- `volunteer.registration.rejected`

## 15. OpenAPI and documentation

Update:

- Public OpenAPI output.
- `docs/API-ENDPOINTS.md`.
- `docs/API-TESTING.md`.
- `docs/API-FRONTEND-INTEGRATION-GUIDE.md`.
- `docs/multitenancy/api/public-v1.draft.yaml`, if it remains an API contract source.

Document:

- URL and method.
- Tenant ID requirement.
- Required headers.
- CAPTCHA token acquisition.
- Idempotency behavior.
- Request payload.
- `202 Accepted` response.
- Pending-approval behavior.
- Validation and duplicate responses.
- Rate-limit responses.
- CORS requirements.
- Production security expectations.

Include a JavaScript `fetch` example following the existing tenant-registration documentation style.

## 16. Testing plan

Add a dedicated integration test file:

```text
tests/Inprem.Platform.Tests.Integration/PublicApi/VolunteerRegistrationTests.cs
```

Use the existing `PublicApiFixture`.

### Successful registration

- Valid input returns `202 Accepted`.
- Response contains a registration ID.
- Volunteer is persisted for the requested tenant.
- Status is pending approval.
- Tenant information is not exposed.

### Tenant validation

- Unknown tenant returns the documented problem response.
- Inactive tenant cannot receive registrations.
- Tenant A cannot create or read data belonging to tenant B.

### Field validation

- Missing required name fields.
- Invalid email.
- Excessively long values.
- Invalid category.
- Missing category-dependent values.
- Invalid consent.
- Missing or invalid CAPTCHA.

### Duplicate handling

- Duplicate email in the same tenant.
- Same email in different tenants.
- Duplicate phone behavior.
- Same-name conflict behavior.
- Database uniqueness race behavior.

### Security

- Public registration does not require bearer authentication.
- Other tenant-protected API endpoints remain protected.
- Tenant IDs cannot be used to read another tenant's volunteers.
- Responses do not leak unnecessary personal-data existence.
- CORS behavior matches the public API.
- Rate limiting returns `429` and `Retry-After`.

### Idempotency

- Repeating the same request and key returns the original result.
- Reusing a key with a different body returns `422`.
- Concurrent requests create only one volunteer.
- Failed requests do not create an unusable idempotency record.

### Regression

- Existing kiosk volunteer registration still works.
- Provider Console volunteer approval still works.
- Existing admin volunteer search still works.
- Existing tenant-registration API tests remain unchanged and passing.
- The full solution build passes.

## 17. Recommended implementation order

1. Confirm the current `Volunteer` entity, approval status, indexes, and database constraints.
2. Confirm tenant context and global query-filter behavior for public writes.
3. Define request and response DTOs.
4. Reuse or extract volunteer validation rules.
5. Add the volunteer registration application service.
6. Add tenant validation and duplicate handling.
7. Add pending-approval persistence and audit logging.
8. Add the public endpoint under `/api/public/v1`.
9. Apply CAPTCHA, idempotency, rate limiting, CORS, and problem responses.
10. Add OpenAPI metadata.
11. Add database migration only if required.
12. Add integration tests.
13. Update API documentation.
14. Run a full solution build and relevant tests.
15. Perform a manual frontend registration test.
