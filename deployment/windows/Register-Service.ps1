#Requires -Version 5.1
#Requires -RunAsAdministrator
# Also completes an interrupted setup after Migrate and CreateAdmin have succeeded.
. (Join-Path $PSScriptRoot 'Common.ps1')
Require-Administrator
if (Get-Service $ServiceName -ErrorAction SilentlyContinue) { throw 'Service already exists. Use Maintain.ps1 -Action Start.' }
$state = Get-Content $StatePath -Raw | ConvertFrom-Json
$exe = Installed-Exe
$appPath = $state.appPath
$uri = [Uri]$state.url
$ServerName = $uri.Host
$Port = $uri.Port
$publicCert = Join-Path $DataRoot 'pharmacy-server.cer'
$serviceCredential = New-Object Management.Automation.PSCredential("NT SERVICE\$ServiceName", (New-Object Security.SecureString))
New-Service -Name $ServiceName -BinaryPathName "`"$exe`" --contentRoot `"$appPath`"" -StartupType Automatic -Credential $serviceCredential -DisplayName 'Pharmacy POS' | Out-Null
New-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name Environment -PropertyType MultiString -Value @("PHARMACY_CONFIG=$ConfigPath", 'ASPNETCORE_ENVIRONMENT=Production', 'DOTNET_ENVIRONMENT=Production') -Force | Out-Null
Run-Native icacls.exe @($DataRoot, '/grant', "NT SERVICE\${ServiceName}:(OI)(CI)RX")
foreach ($folder in @('keys','backups')) { Run-Native icacls.exe @((Join-Path $DataRoot $folder), '/grant', "NT SERVICE\${ServiceName}:(OI)(CI)M") }
Run-Native sc.exe @('failure', $ServiceName, 'reset=', '86400', 'actions=', 'restart/10000/restart/30000/restart/60000')
New-NetFirewallRule -DisplayName 'Pharmacy POS HTTPS (private LAN)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Private -RemoteAddress LocalSubnet | Out-Null
if (-not [Diagnostics.EventLog]::SourceExists('PharmacyPos.Api')) { New-EventLog -LogName Application -Source 'PharmacyPos.Api' }
Start-Service $ServiceName
(Get-Service $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
Wait-Endpoint $Port
Write-Host "Installed: https://${ServerName}:$Port"
Write-Host "For other pharmacy PCs, copy ONLY this public certificate and import it using Trust-Server.ps1: $publicCert"
Write-Host 'Keep this server powered on. The service starts automatically after restart.'
