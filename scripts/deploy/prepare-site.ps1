<#
.SYNOPSIS
    Turns the one build artifact into a deployment package for one site (Phase 0 step 0.10,
    decision D8: one build, deployed as the App site and the Admin site). It does NOT deploy:
    whoever deploys uploads the package (Web Deploy or FTP), following
    docs/multitenancy/deployment-phase0.md.

.DESCRIPTION
    - Copies the published app (folder or .zip) to <OutDir>/<SiteMode>/.
    - Sets the site's non-secret settings as environment variables in web.config:
      ASPNETCORE_ENVIRONMENT, Site__SiteMode, Jobs__RunServer (true only for App), and the
      host names when given. Secrets (connection string, bootstrap and break-glass hashes,
      email settings) are never in the package: they stay in the host's configuration.
    - Removes appsettings.Development.json and appsettings.Production.json (a server keeps its own).
    - Refuses a package that contains anything credential-shaped in its .json/.config files.
    - Writes <OutDir>/<SiteMode>.zip and prints its SHA-256.

.EXAMPLE
    pwsh scripts/deploy/prepare-site.ps1 -Artifact out/app -SiteMode App -OutDir out/sites -AppHost app.inpremclockin.org -AdminHost admin.inpremclockin.org
    pwsh scripts/deploy/prepare-site.ps1 -Artifact out/app -SiteMode Admin -OutDir out/sites -AppHost app.inpremclockin.org -AdminHost admin.inpremclockin.org
#>
param(
    [Parameter(Mandatory)] [string]$Artifact,
    [Parameter(Mandatory)] [ValidateSet('App', 'Admin', 'All')] [string]$SiteMode,
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$AppHost = '',
    [string]$AdminHost = '',
    [string]$Environment = 'Production'
)
$ErrorActionPreference = 'Stop'

$target = Join-Path $OutDir $SiteMode
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Path $target -Force | Out-Null

if ((Get-Item $Artifact).PSIsContainer) { Copy-Item (Join-Path $Artifact '*') $target -Recurse }
else { Expand-Archive -Path $Artifact -DestinationPath $target }

if (-not (Test-Path (Join-Path $target 'InpremClockingApp.dll'))) { throw "No InpremClockingApp.dll in ${Artifact}: not a published build." }

# Per-environment settings files never travel with the build.
foreach ($name in 'appsettings.Development.json', 'appsettings.Production.json') {
    $file = Join-Path $target $name
    if (Test-Path $file) { Remove-Item $file; Write-Output "Removed $name from the package (the server keeps its own)." }
}

# Site settings as environment variables of the ASP.NET Core module.
$webConfig = Join-Path $target 'web.config'
if (-not (Test-Path $webConfig)) { throw 'web.config missing - publish with the Web SDK (dotnet publish).' }
[xml]$xml = Get-Content $webConfig -Raw
$module = $xml.SelectSingleNode('//aspNetCore')
if ($null -eq $module) { throw 'No <aspNetCore> element in web.config.' }
$variables = $module.SelectSingleNode('environmentVariables')
if ($null -eq $variables) { $variables = $module.AppendChild($xml.CreateElement('environmentVariables')) }

$settings = [ordered]@{
    'ASPNETCORE_ENVIRONMENT' = $Environment
    'Site__SiteMode'         = $SiteMode
    'Jobs__RunServer'        = $(if ($SiteMode -eq 'Admin') { 'false' } else { 'true' })
}
if ($AppHost) { $settings['Site__AppHost'] = $AppHost }
if ($AdminHost) { $settings['Site__AdminHost'] = $AdminHost }

foreach ($name in $settings.Keys) {
    $existing = $variables.SelectSingleNode("environmentVariable[@name='$name']")
    if ($null -eq $existing) {
        $existing = $variables.AppendChild($xml.CreateElement('environmentVariable'))
        $existing.SetAttribute('name', $name)
    }
    $existing.SetAttribute('value', $settings[$name])
}
$xml.Save($webConfig)

# Nothing credential-shaped may ship.
$suspicious = Get-ChildItem $target -Recurse -Include *.json, *.config |
    Select-String -Pattern '(?i)(password|pwd)\s*=\s*[^;"\s]{3,}', '(?i)"(password|secret|apikey|codehash)"\s*:\s*"[^"]{3,}"' |
    ForEach-Object { "$($_.Path | Split-Path -Leaf):$($_.LineNumber)" }
if ($suspicious) { throw "Credential-shaped values in the package: $($suspicious -join ', '). Move them to the host's configuration." }

$zip = Join-Path $OutDir "$SiteMode.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $target '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Output "Package for the $SiteMode site: $zip"
Write-Output "SHA-256: $hash"
Write-Output ("Settings in web.config: " + (($settings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '))
Write-Output 'Not set here (host configuration): ConnectionStrings__DefaultConnection, Bootstrap__CodeHash, Security__*, Links__AdminBaseUrl.'
