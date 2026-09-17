Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ServiceName = 'PharmacyPos'
$DataRoot = Join-Path $env:ProgramData 'PharmacyPos'
$ConfigPath = Join-Path $DataRoot 'installation.json'
$StatePath = Join-Path $DataRoot 'release.json'

function Require-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Open PowerShell using Run as administrator.'
    }
}
function Run-Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed: $File (exit $LASTEXITCODE)." }
}
function Write-Json([string]$Path, $Value) {
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
        if (Test-Path $Path) { [IO.File]::Replace($temporary, $Path, $null) } else { [IO.File]::Move($temporary, $Path) }
    } finally { if (Test-Path $temporary) { Remove-Item $temporary } }
}
function Verify-Release([string]$Root) {
    $manifest = Get-Content (Join-Path $Root 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.runtime -ne 'win-x64' -or $manifest.version -notmatch '^\d{8}-\d{6}$') { throw 'Not a Windows x64 pharmacy release.' }
    foreach ($entry in $manifest.files.PSObject.Properties) {
        $file = [IO.Path]::GetFullPath((Join-Path $Root $entry.Name))
        if (-not $file.StartsWith(([IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid release path.' }
        if (-not (Test-Path $file -PathType Leaf) -or (Get-FileHash $file -Algorithm SHA256).Hash -ne $entry.Value) { throw "Release verification failed: $($entry.Name)" }
    }
    return $manifest.version
}
function With-Installation([scriptblock]$Action) {
    $previous = $env:PHARMACY_CONFIG
    $previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $previousDotnetEnvironment = $env:DOTNET_ENVIRONMENT
    try {
        $env:PHARMACY_CONFIG = $ConfigPath
        $env:ASPNETCORE_ENVIRONMENT = 'Production'
        $env:DOTNET_ENVIRONMENT = 'Production'
        & $Action
    } finally {
        $env:PHARMACY_CONFIG = $previous
        $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
        $env:DOTNET_ENVIRONMENT = $previousDotnetEnvironment
    }
}
function Installed-Exe {
    if (-not (Test-Path $StatePath)) { throw 'No installed release was found.' }
    return (Join-Path ((Get-Content $StatePath -Raw | ConvertFrom-Json).appPath) 'PharmacyPos.Api.exe')
}

function Wait-Endpoint([int]$Port) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $response = Invoke-RestMethod "https://localhost:$Port/api/status" -TimeoutSec 2
            if ($response.status -eq 'ok') { return }
        } catch { Start-Sleep -Seconds 1 }
    }
    throw 'The service did not pass its HTTPS startup check. Inspect Windows Event Viewer; do not begin live trading.'
}
