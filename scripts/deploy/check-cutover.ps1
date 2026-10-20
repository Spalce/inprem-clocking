<#
.SYNOPSIS
    Checks the live addresses after each domain-cutover step (docs/multitenancy/runbooks/domain-cutover.md).
    Read-only: it only sends GET requests and never follows redirects. Prints PASS/FAIL per check and
    exits with 1 when anything failed.

.PARAMETER Stage
    Which step you are at:
      Interim  - app. and admin. are live; the root still serves the app (steps 2 to 3).
      Redirect - the App site redirects old links on the root to app. (step 4).
      Website  - the root is the marketing website, with the web team's redirect rules (step 5).

.EXAMPLE
    pwsh scripts/deploy/check-cutover.ps1 -Stage Interim
    pwsh scripts/deploy/check-cutover.ps1 -Stage Website -AppHost app.staging.inpremclockin.org -AdminHost admin.staging.inpremclockin.org -RootHost staging.inpremclockin.org
#>
param(
    [Parameter(Mandatory)] [ValidateSet('Interim', 'Redirect', 'Website')] [string]$Stage,
    [string]$AppHost = 'app.inpremclockin.org',
    [string]$AdminHost = 'admin.inpremclockin.org',
    [string]$RootHost = 'inpremclockin.org'
)
$ErrorActionPreference = 'Stop'

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$http = [System.Net.Http.HttpClient]::new($handler)
$http.Timeout = [TimeSpan]::FromSeconds(30)
$failures = 0

function Get([string]$url) {
    try { return $http.GetAsync($url).GetAwaiter().GetResult() }
    catch { return $null }
}

function Check([string]$name, [string]$url, [int[]]$expected, [string]$location = $null) {
    $response = Get $url
    $code = if ($response) { [int]$response.StatusCode } else { 0 }
    $where = if ($response -and $response.Headers.Location) { $response.Headers.Location.ToString() } else { '' }
    $ok = $expected -contains $code -and (-not $location -or $where -eq $location)
    if (-not $ok) { $script:failures++ }
    $shown = if ($code -eq 0) { 'no answer (DNS, certificate or site down?)' } else { "$code $where".Trim() }
    Write-Host ("{0}  {1,-58} {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $name, $shown)
}

$app = "https://$AppHost"
$admin = "https://$AdminHost"
$root = "https://$RootHost"
$oldLinks = '/BackOffice', '/StaffAttendance', '/staff-clockin/1', '/VolunteerAttendance', '/volunteer-clockin/1',
            '/Identity/Account/Login?ReturnUrl=%2FBackOffice', '/HoursWorked', '/User', '/Billing', '/api/people/staff'

Write-Host "App site ($AppHost)"
Check 'sign-in page answers' "$app/Identity/Account/Login" 200
Check 'kiosk page asks to sign in' "$app/StaffAttendance" 302
Check 'no Provider Console here' "$app/Provider" 404
Check 'PayPal webhook address exists (POST only)' "$app/webhooks/paypal" 405
Check 'Twilio status address exists (POST only)' "$app/webhooks/twilio/status" 405

Write-Host "Admin site ($AdminHost)"
Check 'console sign-in answers (403: your address is not allowed)' "$admin/Login" 200, 403
Check 'no back office here' "$admin/Identity/Account/Login" 404, 403

Write-Host "Root ($RootHost), stage $Stage"
switch ($Stage) {
    'Interim' {
        Check 'still serves the app' "$root/Identity/Account/Login" 200
    }
    { $_ -in 'Redirect', 'Website' } {
        foreach ($path in $oldLinks) { Check "old link $path" "$root$path" 301 "$app$path" }
    }
    'Website' {
        Check 'home page is the website (not a redirect)' "$root/" 200
    }
}

if ($failures -gt 0) {
    Write-Host "`n$failures check(s) failed. See domain-cutover.md for what each one means."
    exit 1
}
Write-Host "`nAll checks passed."
