# Demo website (D29)

A stand-in for the marketing website, until the web team's site exists: pricing, sign-up (trial or
subscribe), PayPal, the return and cancel pages, and "Manage my subscription". Plain HTML and
`fetch`, no build step; it uses only the public website API (`/api/public/v1`).

**Off by default.** Every `/demo-site` address answers 404 unless `Demo:EnableDemoSite` is `true`
(and the site serves the App). Never switch it on in production.

## Running it locally

1. User-secrets (or `appsettings.Development.json` on your machine only):
   - `Demo:EnableDemoSite` = `true`
   - CAPTCHA: either Cloudflare's test keys, `Captcha:Secret` = `1x0000000000000000000000000000000AA` and
     `Demo:CaptchaSiteKey` = `1x00000000000000000000AA` (both always pass), or for a quick look without a
     widget `Demo:CaptchaTestToken` = `XXXX.DUMMY.TOKEN.XXXX` with the test secret.
   - `Links:WebsiteBaseUrl` = `https://localhost:<port>/demo-site` (the billing links go to `/demo-site/billing`).
   - PayPal sandbox keys and `PayPal:ReturnUrlAllowList:0` = `https://localhost:<port>` for the subscribe journeys.
2. Start the app and open `https://localhost:<port>/demo-site/`.

Tokens are kept in the tab's `sessionStorage` only, as the integration guide asks the website to do.
