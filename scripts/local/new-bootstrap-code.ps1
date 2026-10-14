<#
.SYNOPSIS
    LOCAL TESTING ONLY. Creates a one-time bootstrap code for the Provider setup page, stores its
    hash in this machine's user-secrets (Bootstrap:CodeHash) and writes the code itself to a text
    file on your Desktop. The code is never printed. Delete the file when you are done.
#>
$ErrorActionPreference = 'Stop'
$alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
$code = (1..6 | ForEach-Object { -join (1..5 | ForEach-Object { $alphabet[[System.Security.Cryptography.RandomNumberGenerator]::GetInt32(32)] }) }) -join '-'
$hash = -join ([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($code)) | ForEach-Object { $_.ToString('x2') })

$project = Resolve-Path (Join-Path $PSScriptRoot '../../InpremClockingApp')
& dotnet user-secrets set 'Bootstrap:CodeHash' $hash --project $project | Out-Null
if ($LASTEXITCODE) { throw 'Could not save the hash in user-secrets.' }

$file = Join-Path ([Environment]::GetFolderPath('Desktop')) 'inprem-setup-code.txt'
Set-Content -Path $file -Value "Inprem local setup code (testing only - delete this file after use):`r`n`r`n$code"
Write-Output "Done. The setup code is in: $file"
