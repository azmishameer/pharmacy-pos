# Run from an extracted Windows preview package. Uses an already-created demo database.
param([string]$PostgresHost = 'localhost', [int]$PostgresPort = 5432)
$ErrorActionPreference = 'Stop'
Write-Host 'DEMO ONLY. Create database pharmacy_pos_demo owned by pharmacy_demo before starting.'
Write-Host 'This never uses the installed pharmacy configuration. Visit http://localhost:8080.'
$demoPassword = Read-Host 'Password for the separate pharmacy_demo database account' -AsSecureString
$demoPlain = [System.Net.NetworkCredential]::new('', $demoPassword).Password
$names = @('PHARMACY_CONFIG','ASPNETCORE_ENVIRONMENT','ConnectionStrings__Pharmacy','Database__Password','Backup__AutomaticEnabled','ASPNETCORE_URLS')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:PHARMACY_CONFIG = ''
    $env:ASPNETCORE_ENVIRONMENT = 'Demo'
    $env:ConnectionStrings__Pharmacy = "Host=$PostgresHost;Port=$PostgresPort;Database=pharmacy_pos_demo;Username=pharmacy_demo"
    $env:Database__Password = $demoPlain
    $env:Backup__AutomaticEnabled = 'false'
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:8080'
    & (Join-Path $PSScriptRoot 'app\PharmacyPos.Api.exe') --contentRoot (Join-Path $PSScriptRoot 'app')
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    $demoPlain = $null
}
