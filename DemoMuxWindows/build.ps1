param([string]$Dotnet = 'dotnet', [string]$RuntimeCab)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$output = Join-Path $repo 'outputs/DemoMuxWindows'
$stage = Join-Path $output ('build-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'payload'
New-Item -ItemType Directory -Force $payload | Out-Null
$lock = Get-Content (Join-Path $PSScriptRoot 'webview2-runtime.json') -Raw | ConvertFrom-Json
if (!$RuntimeCab) {
    $RuntimeCab = Join-Path $stage 'webview2.cab'
    Invoke-WebRequest $lock.url -OutFile $RuntimeCab
}
if ((Get-FileHash -LiteralPath $RuntimeCab -Algorithm SHA256).Hash -ne $lock.sha256) { throw 'WebView2 CAB SHA-256 校验失败' }
& $Dotnet publish (Join-Path $PSScriptRoot 'DemoMux.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $payload
if ($LASTEXITCODE) { throw 'dotnet publish 失败' }
$expanded = Join-Path $stage 'expanded'
New-Item -ItemType Directory $expanded | Out-Null
& expand.exe $RuntimeCab '-F:*' $expanded | Out-Null
if ($LASTEXITCODE) { throw 'WebView2 解压失败' }
$runtime = Get-ChildItem -LiteralPath $expanded -Directory | Select-Object -First 1
Move-Item -LiteralPath $runtime.FullName -Destination (Join-Path $payload 'WebView2Runtime')
# Fixed Runtime on Windows 10 requires AppContainer read/execute access (Microsoft distribution guidance).
foreach ($sid in @('S-1-15-2-1','S-1-15-2-2')) {
    & icacls.exe (Join-Path $payload 'WebView2Runtime') /grant ($sid.Insert(0,'*') + ':(OI)(CI)(RX)') /T /Q | Out-Null
    if ($LASTEXITCODE) { throw 'WebView2 权限设置失败' }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets/Guide.html') -Destination (Join-Path $payload 'DemoMux-Guide.html')
New-Item -ItemType Directory -Force (Join-Path $payload 'Assets') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets/GuideIcon.png') -Destination (Join-Path $payload 'Assets/GuideIcon.png')
foreach ($name in @('首次使用.md', 'CHANGELOG.md', 'webview2-runtime.json')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $payload }
$release = Get-Content (Join-Path $PSScriptRoot 'release.json') -Raw | ConvertFrom-Json
Copy-Item -LiteralPath (Join-Path $repo $release.acceptance) -Destination (Join-Path $payload '验收记录.md')
$regression = Join-Path $repo 'outputs/verification/regression.log'
if (Test-Path -LiteralPath $regression) {
    $text = Get-Content -LiteralPath $regression -Raw
    if ($text -notmatch '(?m)^\d+ checks passed on ' -or $text -match '(?m)^FAIL:') { throw 'Regression log is incomplete or failed' }
    New-Item -ItemType Directory -Force (Join-Path $payload 'Verification') | Out-Null
    Copy-Item -LiteralPath $regression -Destination (Join-Path $payload 'Verification/regression.log')
}
$archive = Join-Path $output 'DemoMux-Windows-x64.zip'
$manifest = Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path = [IO.Path]::GetRelativePath($payload, $_.FullName); Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
ConvertTo-Json -InputObject @($manifest) -Depth 4 | Set-Content -LiteralPath (Join-Path $payload 'package-manifest.json') -Encoding utf8
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archive + '.sha256') -Value ($hash + '  DemoMux-Windows-x64.zip') -Encoding ascii
& (Join-Path $PSScriptRoot 'export-docs.ps1') -Destination $output | Out-Null
Write-Output $archive
Write-Output $hash
