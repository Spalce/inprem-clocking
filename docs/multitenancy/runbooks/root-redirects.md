# Old app addresses → app.inpremclockin.org (redirect rules)

**For:** the web team, when `inpremclockin.org` becomes the marketing website (domain cutover, step 5 of `domain-cutover.md`).
**Why:** kiosk devices, bookmarks and old emails point to addresses such as `https://inpremclockin.org/StaffAttendance`. After the switch, those addresses must keep working. Each one is sent permanently (**301**) to the **same path and query** on `https://app.inpremclockin.org`.

Example: `https://inpremclockin.org/Identity/Account/Login?ReturnUrl=%2FBackOffice` → `https://app.inpremclockin.org/Identity/Account/Login?ReturnUrl=%2FBackOffice`.

## The rules

A request is redirected when the **first part of its path** matches one of these rules. Matching ignores upper and lower case. `*` means "starts with": `/Staff*` covers `/Staff`, `/StaffAttendance`, `/staff-clockin/12` and so on. Every other path, including the home page `/`, stays with the website.

<!-- rules:start -->
| Rule | What it covers |
|---|---|
| `/Account` | Choose organization, invitations, texted sign-in codes, support sessions, email verification |
| `/api` | The kiosk's clock-in/out calls, people search, reports, the website API (`/api/public/v1`) |
| `/BackOffice` | The admin dashboard |
| `/Billing` | The organization's Billing page, PayPal return pages, invoices |
| `/Error` | The error page |
| `/HoursWorked` | Hours worked |
| `/Identity` | Sign in, sign out, forgot and reset password, account settings |
| `/Index` | The app's start page by name |
| `/Privacy` | The app's privacy page |
| `/Settings` | Organization settings (staff alerts) |
| `/Staff*` | Manage Staff, staff registration, the staff clock page (`/staff-clockin/…`), staff reports |
| `/User` | Admin accounts |
| `/Volunteer*` | Manage Volunteers, volunteer registration, the volunteer clock page (`/volunteer-clockin/…`), volunteer reports |
| `/webhooks` | PayPal and Twilio notifications (their addresses are changed to app. before the switch; see below) |
<!-- rules:end -->

This list is the same as the app's own (`LegacyPaths` in `InpremClockingApp/Hosting/LegacyRedirect.cs`). A test fails if they differ, and another fails if a page of the app is not covered.

**Website page names to avoid:** a website page whose path starts with one of these rules is redirected to the app. For example, `/volunteer` or `/staffing` would match `/Volunteer*` and `/Staff*`. Pick other names (e.g. `/get-involved`, `/careers`), or tell us and we will narrow the rule.

## Examples for the web team's hosting

Use whichever matches the website's hosting. All of them keep the query string.

### Generic

```
If host is inpremclockin.org or www.inpremclockin.org
and the path matches (case-insensitive):
  ^/(account|api|backoffice|billing|error|hoursworked|identity|index|privacy|settings|staff[^/]*|user|volunteer[^/]*|webhooks)(/.*)?$
then answer 301 with Location: https://app.inpremclockin.org + original path + original query.
```

### nginx

```nginx
# inside the server block for inpremclockin.org and www.inpremclockin.org
location ~* ^/(account|api|backoffice|billing|error|hoursworked|identity|index|privacy|settings|staff[^/]*|user|volunteer[^/]*|webhooks)(/|$) {
    return 301 https://app.inpremclockin.org$request_uri;
}
```

### Apache (`.htaccess` or the virtual host)

```apache
RewriteEngine On
RewriteCond %{HTTP_HOST} ^(www\.)?inpremclockin\.org$ [NC]
RewriteRule ^(account|api|backoffice|billing|error|hoursworked|identity|index|privacy|settings|staff[^/]*|user|volunteer[^/]*|webhooks)(/.*)?$ https://app.inpremclockin.org%{REQUEST_URI} [NC,R=301,L]
```

(Apache adds the original query string itself.)

### IIS (`web.config`, URL Rewrite module)

```xml
<system.webServer>
  <rewrite>
    <rules>
      <rule name="Old app paths to app.inpremclockin.org" stopProcessing="true">
        <match url="^(account|api|backoffice|billing|error|hoursworked|identity|index|privacy|settings|staff[^/]*|user|volunteer[^/]*|webhooks)(/.*)?$" ignoreCase="true" />
        <conditions>
          <add input="{HTTP_HOST}" pattern="^(www\.)?inpremclockin\.org$" />
        </conditions>
        <action type="Redirect" url="https://app.inpremclockin.org/{R:0}" redirectType="Permanent" appendQueryString="true" />
      </rule>
    </rules>
  </rewrite>
</system.webServer>
```

## The app can do it too

While `inpremclockin.org` still points to our App site, the App site can answer these redirects itself. This moves users to `app.` before the website takes over (`domain-cutover.md`, step 4). Set on the App site:

| Setting | Value |
|---|---|
| `LegacyRedirect__Enabled` | `true` |
| `LegacyRedirect__FromHosts__0` | `inpremclockin.org` |
| `LegacyRedirect__FromHosts__1` | `www.inpremclockin.org` |
| `LegacyRedirect__TargetBaseUrl` | `https://app.inpremclockin.org` |

It is off unless `LegacyRedirect__Enabled` is `true`. With wrong values, the app refuses to start and says which setting is wrong.

## Webhooks and forms

A 301 tells a browser to ask again at the new address with **GET**. Most programs that **POST** (PayPal, Twilio, a form being sent) do not follow it. So:
- **Before** the switch, change the webhook addresses at PayPal and Twilio to `https://app.inpremclockin.org/webhooks/...` (`domain-cutover.md`, step 3).
- A form open in someone's browser at the moment of the switch fails once. After the redirect, the page loads from `app.` and works.

## Checking

After the rules are live, every line below must show `301` and the `app.` address with the same path and query:

```powershell
foreach ($p in '/BackOffice', '/StaffAttendance', '/staff-clockin/1', '/VolunteerAttendance', '/volunteer-clockin/1',
              '/Identity/Account/Login?ReturnUrl=%2FBackOffice', '/HoursWorked', '/User', '/Billing', '/api/people/staff') {
    $r = Invoke-WebRequest "https://inpremclockin.org$p" -MaximumRedirection 0 -SkipHttpErrorCheck
    "{0} {1} -> {2}" -f $r.StatusCode, $p, $r.Headers.Location
}
```

And `https://inpremclockin.org/` must show the website, not a redirect.
