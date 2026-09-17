#Requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$CertificatePath)
$ErrorActionPreference = 'Stop'
# Run on each cashier's Windows account, with the public .cer from the pharmacy server.
if ([IO.Path]::GetExtension($CertificatePath) -ne '.cer') { throw 'Select only the public pharmacy-server.cer file, never the private .pfx.' }
$cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2((Resolve-Path $CertificatePath).Path)
if ($cert.HasPrivateKey) { throw 'This must be a public certificate.' }
Write-Host "Certificate: $($cert.Subject)"
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host "Expires: $($cert.NotAfter)"
if ((Read-Host 'Verify this thumbprint with the pharmacy server administrator. Type TRUST to install') -cne 'TRUST') { throw 'Certificate was not installed.' }
Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
Write-Host 'Certificate trusted for this Windows user. Open the pharmacy server URL.'
