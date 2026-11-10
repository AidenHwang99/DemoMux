$ErrorActionPreference = 'Stop'
$source = Split-Path $PSScriptRoot
$repo = Split-Path $source
$payload = Join-Path $repo ('outputs/installer-shell-' + [guid]::NewGuid().ToString('N'))
# Quick installer plumbing checks before downloading/building the complete release payload.
& dotnet publish (Join-Path $source 'DemoMux.csproj') -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o $payload
if ($LASTEXITCODE) { throw 'Installer shell fixture failed' }
Copy-Item -LiteralPath (Join-Path $source 'Assets/Guide.html') -Destination (Join-Path $payload 'DemoMux-Guide.html')
$manifest = @(Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object {
    @{Path=[IO.Path]::GetRelativePath($payload,$_.FullName); Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
})
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $payload 'package-manifest.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'InstallerSmoke.ps1') -Payload $payload
