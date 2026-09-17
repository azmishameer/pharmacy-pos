#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$ServerName = $env:COMPUTERNAME,
    [ValidateRange(1024,65535)][int]$Port = 8443,
    [string]$DatabaseName = 'pharmacy_pos',
    [string]$DatabaseUser = 'pharmacy_app',
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
Require-Administrator
if ([Uri]::CheckHostName($ServerName) -ne [UriHostNameType]::Dns) { throw 'Use a DNS hostname, not a raw IP address.' }
if ($ServerName -notmatch '^[a-zA-Z0-9][a-zA-Z0-9.-]{0,252}$') { throw 'Use the Windows computer name or a DNS hostname.' }
if ($DatabaseName -notmatch '^[a-z_][a-z0-9_]{0,62}$' -or $DatabaseUser -notmatch '^[a-z_][a-z0-9_]{0,62}$') { throw 'Use lowercase database/user names with letters, numbers and underscores.' }
if (-not [Environment]::Is64BitOperatingSystem) { throw '64-bit Windows is required.' }
if (Get-Service $ServiceName -ErrorAction SilentlyContinue) { throw 'Pharmacy POS is already installed. Use Update.ps1.' }
if (Test-Path $ConfigPath) { throw 'Installation settings already exist. Preserve them and follow the interrupted-install instructions in README.md.' }
if (-not (Test-Path (Join-Path $PostgresBin 'pg_dump.exe'))) { throw 'Install PostgreSQL 18 first, or supply -PostgresBin.' }
$version = Verify-Release $PSScriptRoot
$appPath = Join-Path $env:ProgramFiles "PharmacyPos\releases\$version"
if (Test-Path $appPath) { throw 'This release directory already exists; preserve it and follow the recovery instructions.' }
$databasePassword = Read-Host 'Password for the pharmacy_app database account (not the postgres administrator)' -AsSecureString
if ($databasePassword.Length -eq 0) { throw 'Database password is required.' }
New-Item $DataRoot -ItemType Directory -Force | Out-Null
Run-Native icacls.exe @($DataRoot, '/inheritance:r', '/grant:r', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F')
foreach ($folder in @('keys','backups','certificates')) { New-Item (Join-Path $DataRoot $folder) -ItemType Directory -Force | Out-Null }
New-Item $appPath -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'app\*') $appPath -Recurse
$certificate = New-SelfSignedCertificate -DnsName @($ServerName, 'localhost') -CertStoreLocation 'Cert:\LocalMachine\My' -NotAfter (Get-Date).AddYears(2) -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable -FriendlyName 'Pharmacy POS local HTTPS'
$certPassword = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$certPath = Join-Path $DataRoot 'certificates\server.pfx'
Export-PfxCertificate -Cert $certificate -FilePath $certPath -Password (ConvertTo-SecureString $certPassword -AsPlainText -Force) | Out-Null
$publicCert = Join-Path $DataRoot 'pharmacy-server.cer'
Export-Certificate -Cert $certificate -FilePath $publicCert | Out-Null
Import-Certificate -FilePath $publicCert -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($databasePassword)
try {
    $config = @{
        ConnectionStrings = @{ Pharmacy = "Host=localhost;Port=5432;Database=$DatabaseName;Username=$DatabaseUser" }
        Database = @{ Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
        AllowedHosts = "$ServerName;localhost"
        DataProtection = @{ Directory = (Join-Path $DataRoot 'keys') }
        Backup = @{ Directory = (Join-Path $DataRoot 'backups'); PostgresBin = $PostgresBin; AutomaticEnabled = $true }
        Kestrel = @{ Endpoints = @{ Https = @{ Url = "https://0.0.0.0:$Port"; Certificate = @{ Path = $certPath; Password = $certPassword } } } }
        Logging = @{ LogLevel = @{ Default = 'Warning'; 'Microsoft.Hosting.Lifetime' = 'Information' } }
    }
    Write-Json $ConfigPath $config
} finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $config = $null; $certPassword = $null }
Write-Json $StatePath @{ version = $version; appPath = $appPath; url = "https://${ServerName}:$Port"; certificateExpires = $certificate.NotAfter.ToString('o') }
$exe = Join-Path $appPath 'PharmacyPos.Api.exe'
With-Installation {
    Run-Native $exe @('--migrate-database', '--contentRoot', $appPath)
    Run-Native $exe @('--create-admin', '--contentRoot', $appPath)
}
& (Join-Path $PSScriptRoot 'Register-Service.ps1')
