# 07 — Phase 5: Twilio login codes, billing and provider alerts

**Entry:** Phase 2 exit checks pass (Phases 3–4 not required). Twilio account with test credentials, a Verify service and a Messaging Service (trial accounts send only to verified numbers). For production sending to US numbers, sender registration (D31) must be approved. Decisions D14, D31.
**Branch:** `mt/phase5` from `mt/integration`.
**Design references:** sections 9, 13 (Phase 5), Appendix C (alert catalogue and consent rules).

Follow `00-guardrails.md`. Run all required suites after every step; update `PROGRESS.md`. **Test/trial credentials only.**

## Step 5.1 — Notification module

- Domain: `Notification` (outbox: Id, TenantId nullable, RecipientType, RecipientRef, Channel Sms/Email, TemplateKey, Payload, Status Queued/Sent/Delivered/Failed/Skipped, SkipReason, ExternalId, Cost, CreatedUtc, SentUtc), `ConsentRecord` (person/phone, granted/revoked, source, wording version, timestamp), `NotificationTemplate`.
- `INotificationService.Raise(event)`: picks template and recipients; checks consent/opt-out, preferences, quiet hours in the tenant's time zone (non-urgent waits until 08:00 local), per-tenant monthly SMS limit, platform daily spend cap; writes outbox rows or Skipped with reason.
- Hangfire job sends queued SMS via `ISmsSender` (Twilio Messaging Service SID), retries transient failures; email via `IEmailSender`.
- Twilio status callback webhook `/webhooks/twilio/status` and inbound STOP/START/HELP handling `/webhooks/twilio/inbound`, both validating Twilio's request signature; opt-outs recorded and honoured immediately.
- Phone numbers stored in E.164.

## Step 5.2 — Login codes

- `IOtpService` using Twilio Verify for SMS codes: send, check, rate limits; code reuse and expiry rejected.
- Provider users: authenticator app remains the primary factor; SMS code as an option. Organization admins: optional SMS two-factor per organization setting (no change for organizations that do not enable it).

## Step 5.3 — Billing and provider alerts

- Owner: trial ending (7 and 1 days), payment failed, suspended, invoice issued/overdue.
- Provider: new signup, payment failed/suspended (immediate or daily summary per user preference), system problems (webhook verification failures, failed jobs, SMS spend above threshold; urgent, ignores quiet hours).
- Console: Notifications → templates (edit on a separate page), delivery log, opt-outs, spend per tenant; per-tenant SMS on/off and monthly limit.

## Tests

- Opted-out or non-consenting numbers never receive messages (test using STOP flow).
- Quiet hours by tenant time zone; per-tenant and platform limits.
- Signature validation on both Twilio webhooks.
- Delivery status recorded for every sent message (test credentials + simulated callbacks).
- OTP: reuse and expiry rejected.

## Acceptance criteria

| ID | Criterion | Measure |
|---|---|---|
| P5-1 | Login codes | Verify codes work; reuse/expiry rejected |
| P5-2 | Consent | 0 messages to opted-out or non-consenting numbers |
| P5-3 | Limits | Quiet hours and spend limits enforced |
| P5-4 | Delivery | Status recorded for 100% of sent messages on staging |

## Out of scope

Staff and volunteer alerts (Phase 6), any change to registration forms.
