#Requires -Version 5.1
#Requires -RunAsAdministrator
. (Join-Path $PSScriptRoot 'Common.ps1')
Require-Administrator
$version = Verify-Release $PSScriptRoot
$old = Get-Content $StatePath -Raw | ConvertFrom-Json
$appPath = Join-Path $env:ProgramFiles "PharmacyPos\releases\$version"
if (Test-Path $appPath) { throw 'Release already exists. No update was made.' }
$oldExe = Installed-Exe
New-Item $appPath -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'app\*') $appPath -Recurse
Stop-Service $ServiceName
(Get-Service $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
try {
    With-Installation { Run-Native $oldExe @('--backup-database','--contentRoot',(Split-Path $oldExe)) }
    $exe = Join-Path $appPath 'PharmacyPos.Api.exe'
    With-Installation { Run-Native $exe @('--migrate-database','--contentRoot',$appPath) }
    $service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    $changed = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments @{ PathName = "`"$exe`" --contentRoot `"$appPath`"" }
    if ($changed.ReturnValue -ne 0) { throw "Could not switch the service executable (code $($changed.ReturnValue))." }
    Write-Json $StatePath @{ version=$version; appPath=$appPath; url=$old.url; certificateExpires=$old.certificateExpires; previousAppPath=$old.appPath }
    Start-Service $ServiceName
    (Get-Service $ServiceName).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    Wait-Endpoint ([Uri]$old.url).Port
    Write-Host "Updated. Open $($old.url) and check sign-in, stock and a receipt. Old files and database backup were retained."
} catch {
    Write-Warning 'Update did not complete. The service may be stopped. Keep the backup and both releases; do not downgrade a migrated database blindly.'
    throw
}
