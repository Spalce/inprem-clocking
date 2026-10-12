# Claude Code build pack: InpremClockingApp multi-tenancy

This pack lets you and Claude Code build the complete multi-tenancy layer in phases, with tests at every step. There are no approval gates: a phase starts when the previous phase's **exit checks** pass. Work at whatever pace suits you; `PROGRESS.md` lets any session pick up where the last one stopped.

## Files

| File | Purpose |
|---|---|
| `00-guardrails.md` | Rules for every session (read first, every time) |
| `DECISIONS.md` | Decisions the design left open, set to the recommended defaults. Edit it if you want something different; Claude Code follows it |
| `TESTING.md` | Test strategy, suites, tools and the definition of "well tested" |
| `PROGRESS.md` | Running log Claude Code updates after every step: current phase, step, what passed, what is next |
| `design-v0.5.md` | Full design (text version for Claude Code). Prompts list which sections to read |
| `01-g0-preparation.md` | Phase G0: baseline tests, test projects, CI, database inspection, API draft |
| `02-phase0-foundation.md` | Phase 0: structure, memberships, tenancy hardening, bootstrap and recovery, two-site setup, jobs |
| `03-phase1-provider-console.md` | Phase 1: Provider Console |
| `04-phase2-paypal-core.md` | Phase 2: PayPal subscriptions, webhooks, Billing page, enforcement |
| `05-phase3-website-api.md` | Phase 3: Website API for registration and subscriptions |
| `06-phase4-invoices.md` | Phase 4: PayPal invoices, offline payments, refunds |
| `07-phase5-twilio.md` | Phase 5: Twilio login codes, billing and provider alerts |
| `08-phase6-staff-alerts.md` | Phase 6: Staff and volunteer alerts |
| `09-domain-cutover.md` | Hostnames `app.` / `admin.`, redirects from the root domain |
| `10-phase7-hardening.md` | Phase 7: remaining security, cleanup, contract migrations, final verification |
| `DEMO-SCRIPT.md` | What to show once Phases 0–3 and 5 are done |

## Set up once

1. Create branch `mt/integration` from `fix/login-admin-only`.
2. Copy this folder to `docs/multitenancy/build/` and move `design-v0.5.md` to `docs/multitenancy/design-v0.5.md`. Commit.
3. Keep secrets out of the repository: use `dotnet user-secrets` locally and server-only configuration on the host.

## Every session

Open Claude Code in the repository root and say:

> Read `docs/multitenancy/build/00-guardrails.md`, `DECISIONS.md`, `TESTING.md` and `PROGRESS.md`. Continue with the next unfinished step of the current phase file. Show me a short plan, wait for my "go", then do only that step, run all required tests, update `PROGRESS.md`, and report.

To start a new phase, say: "Start `<phase file>`", and Claude Code creates the phase branch from `mt/integration`.

## Finishing a phase

1. Claude Code runs the phase's exit checks and the full regression (all suites in `TESTING.md`), and writes the results in `PROGRESS.md`.
2. You review the diff, run the app yourself through the phase's manual checks, then merge the phase branch into `mt/integration`.
3. Move to the next phase.

## Order and what you can show after each

| Phase | Builds | Demo-able after this phase |
|---|---|---|
| G0 | Baseline tests, CI, inspection | Tests protect the approved app |
| 0 | Foundation | Credential-free first provider login; two organizations isolated |
| 1 | Provider Console | Create and manage organizations, plans, provider users |
| 2 | PayPal core | Subscribe in PayPal sandbox; suspension makes an organization read-only |
| 3 | Website API | Signup and subscription through the API (and a demo website page) |
| 4 | Invoices | PayPal invoices, offline payments, refunds |
| 5 | Twilio | Login codes and billing alerts by SMS |
| 6 | Staff alerts | Missed clock-out SMS |
| Cutover | Hostnames | Live on `app.` and `admin.` |
| 7 | Hardening | Production-ready |

Phases 4–6 can be reordered if you prefer (for example Twilio before invoices); each file states what it depends on.
