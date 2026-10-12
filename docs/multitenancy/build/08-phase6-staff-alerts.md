# 08 — Phase 6: Staff and volunteer alerts

**Entry:** Phase 5 exit checks pass. Decisions C3-8 (behind organization setting, off by default), D32. Before sending to real US numbers: sender registration approved and Twilio's answer on whether per-organization registration is needed recorded in `PROGRESS.md`.
**Branch:** `mt/phase6` from `mt/integration`.
**Design references:** sections 9, 10.3 (C3-8), 13 (Phase 6), Appendix C.

Follow `00-guardrails.md`. Run all required suites after every step; update `PROGRESS.md`.

## Important

This phase changes approved registration forms (C3-8). Make the smallest possible change: the new fields appear **only when the organization has staff alerts switched on** (organization setting, default off). With alerts off, the forms must render and behave exactly as today (behaviour baseline).

## Step 6.1 — Organization settings

- Settings (owner, in the back office via a new settings page — not by editing approved pages): staff alerts on/off, missed clock-out time (local, e.g. 21:00), long-break threshold (minutes), end-of-day admin summary on/off.

## Step 6.2 — Consent capture (C3-8)

- When alerts are on: optional mobile number and an unchecked consent checkbox with the consent wording from design Appendix C (message types, “Msg & data rates may apply”, “Reply STOP to opt out”), on kiosk registration and back-office add/edit forms. Implement through a partial included only when the setting is on; store a `ConsentRecord` with wording version, source and timestamp.

## Step 6.3 — Alerts

- Hangfire recurring job per time zone bucket: find people still clocked in at the organization's missed clock-out time (tenant local time), or on break longer than the threshold; raise notifications through the Phase 5 module (consent, quiet hours and limits apply).
- Optional end-of-day summary to admins: one message per organization, not one per person.

## Tests

- Baseline suite 100% with alerts off.
- Consent required; STOP honoured.
- Alerts fire at the correct local time for organizations in two time zones, including a daylight-saving change day.

## Acceptance criteria

| ID | Criterion | Measure |
|---|---|---|
| P6-1 | Setting | Fields and alerts appear only when the organization switches staff alerts on (test) |
| P6-2 | Behaviour | Forms unchanged with alerts off (baseline suite) |
| P6-3 | Alerts | Missed clock-out alert at the configured local time in two time zones |
