#Requires -Version 5.1
#Requires -RunAsAdministrator
param([Parameter(Mandatory=$true)][ValidateSet('Status','Backup','CreateAdmin','ResetAdminPassword','Migrate','Start','Stop')][string]$Action)
. (Join-Path $PSScriptRoot 'Common.ps1')
Require-Administrator
if ($Action -eq 'Status') { Get-Service $ServiceName; Get-Content $StatePath; exit }
if ($Action -eq 'Start') { Start-Service $ServiceName; exit }
if ($Action -eq 'Stop') { Stop-Service $ServiceName; exit }
$service = Get-Service $ServiceName -ErrorAction SilentlyContinue
if ($Action -eq 'Migrate' -and $null -ne $service -and $service.Status -eq 'Running') { throw 'Stop the service before migrations.' }
$exe = Installed-Exe
$command = @{ Backup='--backup-database'; CreateAdmin='--create-admin'; ResetAdminPassword='--reset-admin-password'; Migrate='--migrate-database' }[$Action]
With-Installation { Run-Native $exe @($command, '--contentRoot', (Split-Path $exe)) }
