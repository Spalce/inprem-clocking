# Site settings per site and environment

**For:** whoever deploys (domain cutover step C.1). Each site needs:
- the **settings in its template** (no secrets; safe to keep in the repository);
- the **secrets**, set only in that site's host configuration.

The templates are in `scripts/deploy/templates/`:

| Template | Use it for |
|---|---|
| `app-site.production.json` | The App site at `app.inpremclockin.org`: kiosk, back office, Billing, website API, webhooks, background jobs |
| `admin-site.production.json` | The Admin site at `admin.inpremclockin.org`: the Provider Console |
| `test-site.single-host.json` | One site on one address that serves both, with the console under `/Provider` (D9). This is the SmarterASP test site today. Replace `test-site.example` with the test site's real address |

**Staging:** use the production templates with the staging addresses in place of `inpremclockin.org` (e.g. `app.staging.inpremclockin.org`, `admin.staging.inpremclockin.org`, and the web team's staging website origin). Change every address in the file, not only some.

A test (`SiteTemplateTests`) starts the app with each template. So a template in the repository always passes the app's startup checks, and it contains no secrets.

## How to set them on the host

Pick one way per site:
- **Environment variables or app settings** in the host's control panel. The name uses `__` (two underscores) in place of `:`, and `__0`, `__1` for list items. Example: `Site:PublicWebsiteOrigins` with two origins becomes `Site__PublicWebsiteOrigins__0` and `Site__PublicWebsiteOrigins__1`.
- **A server-only `appsettings.Production.json`**: copy the template's content into it on the server, then add the secrets below. Never commit that file. If you upload with Web Deploy, don't tick "remove additional files at destination", or the file is deleted (see `deployment-phase0.md`).

`scripts/deploy/prepare-site.ps1` already writes `ASPNETCORE_ENVIRONMENT`, `Site__SiteMode`, `Jobs__RunServer` and the two host names into the package's `web.config`. Set the rest as above.

## Every setting

"App", "Admin" and "Test" are the three templates. **Bold** values are the ones to check for your environment.

### Which site this is

| Setting | App | Admin | Test | Notes |
|---|---|---|---|---|
| `Site:SiteMode` | `App` | `Admin` | `All` | Required. `All` only for local work or a single-host test site |
| `Site:AppHost` | **empty** at first | `app.inpremclockin.org` | empty | On the App site, leave it **empty while `inpremclockin.org` still serves the app** (cutover steps 2 to 4). With a host set, the app answers 404 on any other address. Set it to `app.inpremclockin.org` only after the root has moved to the website (step 6) |
| `Site:AdminHost` | `admin.inpremclockin.org` | `admin.inpremclockin.org` | empty | The console answers only on this host |
| `Site:PublicWebsiteOrigins` | `https://inpremclockin.org`, `https://www.inpremclockin.org` | – | – | The website that may call the website API (CORS). Add the staging website's origin on staging |
| `Jobs:RunServer` | `true` | `false` | `true` | Background jobs (texts, staff alerts, PayPal checks) run on the App site only. The Admin site refuses `true` |
| `AdminSite:AllowedNetworks` | – | **must be set before production** | empty | The addresses allowed to open the console, e.g. `203.0.113.7` or `203.0.113.0/24`. Empty means everyone (testing only, D17) |

### Links in emails and texts

| Setting | App | Admin | Test | Notes |
|---|---|---|---|---|
| `Links:AppBaseUrl` | `https://app.inpremclockin.org` | same | the test site's address | Owner verification, password, invitation links; Billing links in alerts |
| `Links:AdminBaseUrl` | `https://admin.inpremclockin.org` | same | test site + `/Provider` | Provider invitations and recovery links |
| `Links:WebsiteBaseUrl` | `https://inpremclockin.org` | same | test site + `/demo-site` | Billing-access links sent from the website's form |

### Old addresses (cutover step C.2)

| Setting | App | Admin | Test | Notes |
|---|---|---|---|---|
| `LegacyRedirect:Enabled` | `false` (turn on at step 4) | – | `false` | When on, old app links on the root send people to `app.` (301). See `root-redirects.md` |
| `LegacyRedirect:FromHosts` | `inpremclockin.org`, `www.inpremclockin.org` | – | – | |
| `LegacyRedirect:TargetBaseUrl` | `https://app.inpremclockin.org` | – | – | |

### PayPal and Twilio

| Setting | App | Admin | Test | Notes |
|---|---|---|---|---|
| `PayPal:Mode` | `Sandbox` | `Sandbox` | `Sandbox` | Live payments need `PayPal:AllowLive`; no step in this pack turns it on |
| `PayPal:ReturnUrlAllowList` | `https://app.inpremclockin.org`, `https://inpremclockin.org`, `https://www.inpremclockin.org` | – | the test site | Where PayPal may send people back to |
| `PayPal:PlanReturnUrl` | `https://app.inpremclockin.org/Billing/PayPalReturn` | – | test site + same path | Stored on every PayPal plan |
| `PayPal:PlanCancelUrl` | `https://app.inpremclockin.org/Billing` | – | test site + same path | |
| `Twilio:Mode` | `StandIn` until Twilio is set up | same | `StandIn` | `StandIn` sends nothing |
| `Twilio:WebhookBaseUrl` | `https://app.inpremclockin.org` | same | the test site | Twilio's reports come here, and their signatures are checked against it |

**Addresses to enter at PayPal and Twilio** (cutover step 3):
- PayPal webhook: `https://app.inpremclockin.org/webhooks/paypal`
- Twilio status callback: `https://app.inpremclockin.org/webhooks/twilio/status`
- Twilio incoming messages (STOP, START, HELP): `https://app.inpremclockin.org/webhooks/twilio/inbound`

### Email and others

| Setting | App | Admin | Test | Notes |
|---|---|---|---|---|
| `Email:SmtpPort` | `587` | `587` | `587` | Port 465 is not supported |
| `Email:EnableSsl` | `true` | `true` | `true` | |
| `PublicApi:EnableDocs` | `false` | – | – | `true` shows the API description at `/api/public/docs` |
| `Demo:EnableDemoSite` | `false` | `false` | `true` | The pretend website for testing (`/demo-site`) |

## Secrets: set on the host only, never in a template

| Setting | App | Admin | Notes |
|---|---|---|---|
| `ConnectionStrings:DefaultConnection` | yes | yes | The same database for both sites |
| `Email:SmtpHost`, `Email:UserName`, `Email:Password`, `Email:From` | yes | yes | Without them nothing is emailed, and the app says so |
| `PayPal:ClientId`, `PayPal:Secret`, `PayPal:WebhookId` | yes | yes | Sandbox keys from the project owner |
| `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:MessagingServiceSid`, `Twilio:VerifyServiceSid` | yes | yes | Test credentials from the project owner; then `Twilio:Mode` = `Twilio` |
| `Captcha:Secret` | yes | – | The website form's CAPTCHA (Turnstile) |
| `PublicApi:ApiKeyHashes` | if used | – | Only for server-to-server calls from the website |
| `Bootstrap:CodeHash` | – | until the first Provider Owner is set up | `bootstrap-and-recovery.md` |
| `Security:BreakGlassCodeHash`, `Security:RecoveryApprovers`, `Security:Mailbox` | – | yes | `bootstrap-and-recovery.md` |
