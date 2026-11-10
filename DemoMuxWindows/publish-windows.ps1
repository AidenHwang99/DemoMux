$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = Split-Path $PSScriptRoot
$releasePath = Join-Path $PSScriptRoot 'release.json'
if (!(Test-Path -LiteralPath $releasePath)) { throw 'Release declaration missing; publication is not enabled' }
$release = Get-Content -LiteralPath $releasePath -Raw | ConvertFrom-Json
$version = ([xml](Get-Content (Join-Path $PSScriptRoot 'DemoMux.csproj') -Raw)).Project.PropertyGroup.Version
if (!$release.publish -or $release.tag -ne ('windows-v' + $version)) { throw 'Release declaration does not match the application version' }
$title = if ($release.title) { $release.title } else { 'DemoMux Windows ' + $version }
$prereleaseFlag = '--prerelease=' + ([bool]$release.prerelease).ToString().ToLowerInvariant()
& (Join-Path $PSScriptRoot 'build-tailscale.ps1')
$componentLock = Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json | Where-Object name -eq 'Tailscale'
$componentArchive = Join-Path $repo ('outputs/TailscaleWindows/' + [IO.Path]::GetFileName($componentLock.url))
if ((Get-FileHash -LiteralPath $componentArchive -Algorithm SHA256).Hash -ne $componentLock.sha256) { throw 'Rebuilt component archive differs from the reviewed lock' }
$env:DEMOMUX_COMPONENT_CACHE = Split-Path $componentArchive
$caddyLock = Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json | Where-Object name -eq 'Caddy'
$caddyArchive = Join-Path $env:DEMOMUX_COMPONENT_CACHE ([IO.Path]::GetFileName(([Uri]$caddyLock.url).AbsolutePath))
Invoke-WebRequest $caddyLock.url -OutFile $caddyArchive -MaximumRetryCount 3 -RetryIntervalSec 3 -TimeoutSec 180
if ((Get-FileHash -LiteralPath $caddyArchive -Algorithm SHA256).Hash -ne $caddyLock.sha256) { throw 'Caddy archive differs from the reviewed lock' }
$verification = Join-Path $repo 'outputs/verification'
New-Item -ItemType Directory -Force $verification | Out-Null
& dotnet run --project (Join-Path $PSScriptRoot 'Tests/DemoMux.Tests.csproj') -c Release 2>&1 | Tee-Object -FilePath (Join-Path $verification 'regression.log')
if ($LASTEXITCODE) { throw 'Windows regression failed' }
& python (Join-Path $repo 'Shared/Brand/test-build-icons.py') 2>&1 | Tee-Object -FilePath (Join-Path $verification 'brand-boundaries.log')
if ($LASTEXITCODE) { throw 'Platform icon boundary tests failed' }
& (Join-Path $PSScriptRoot 'build.ps1')
$componentTag = 'windows-components-v' + $componentLock.version
& gh release view $componentTag --repo AidenHwang99/DemoMux *> $null
if ($LASTEXITCODE -ne 0) {
    & gh release create $componentTag $componentArchive ($componentArchive + '.sha256') --repo AidenHwang99/DemoMux --target $env:GITHUB_SHA --prerelease --title 'Windows Tailscale 1.104.5 · DemoMux EFS compatibility' --notes-file (Join-Path $PSScriptRoot 'Components/README.md')
    if ($LASTEXITCODE) { throw 'Component release publication failed' }
}
$output = Join-Path $repo 'outputs/DemoMuxWindows'
$documentation = @(& (Join-Path $PSScriptRoot 'export-docs.ps1') -Destination $output)
$files = @((Join-Path $output 'DemoMux-Windows-x64.zip'),(Join-Path $output 'DemoMux-Windows-x64.zip.sha256')) + $documentation
$sums = @($files | Where-Object { !($_.EndsWith('.sha256')) } | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) })
$sumsPath = Join-Path $output 'SHA256SUMS.txt'; Set-Content -LiteralPath $sumsPath -Value $sums -Encoding ascii; $files += $sumsPath
$existingText = & gh release view $release.tag --repo AidenHwang99/DemoMux --json isDraft 2>$null
if ($LASTEXITCODE -eq 0) {
    $existing = ($existingText -join "`n") | ConvertFrom-Json
    if (!$existing.isDraft) { throw 'Application release is already public; refusing to replace reviewed assets' }
    & gh release edit $release.tag --repo AidenHwang99/DemoMux --target $env:GITHUB_SHA --draft $prereleaseFlag --title $title --notes-file (Join-Path $PSScriptRoot 'CHANGELOG.md')
} else {
    & gh release create $release.tag --repo AidenHwang99/DemoMux --target $env:GITHUB_SHA --draft $prereleaseFlag --title $title --notes-file (Join-Path $PSScriptRoot 'CHANGELOG.md')
}
if ($LASTEXITCODE) { throw 'Windows release draft preparation failed' }
foreach ($file in $files) {
    for ($attempt=0; $attempt -lt 3; $attempt++) {
        & gh release upload $release.tag $file --repo AidenHwang99/DemoMux --clobber
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq 2) { throw ('Draft asset upload failed: ' + [IO.Path]::GetFileName($file)) }
        Start-Sleep -Seconds 3
    }
}
& gh release edit $release.tag --repo AidenHwang99/DemoMux --draft=false $prereleaseFlag --latest=false
if ($LASTEXITCODE) { throw 'Windows release publication failed' }
$download = Join-Path $output 'public-download-check'
New-Item -ItemType Directory -Force $download | Out-Null
& gh release download $release.tag --repo AidenHwang99/DemoMux --dir $download
if ($LASTEXITCODE) { throw 'Public application download verification failed' }
foreach ($file in $files) {
    $remote = Join-Path $download ([IO.Path]::GetFileName($file))
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $remote -Algorithm SHA256).Hash) { throw ('Public attachment differs: ' + [IO.Path]::GetFileName($file)) }
}
$notes = Join-Path $output 'published-notes.md'
$text = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CHANGELOG.md')) + "`n`n公开附件已由发布流程重新下载并逐项比对 SHA-256。完整校验值见附件 SHA256SUMS.txt。`n"
[IO.File]::WriteAllText($notes,$text)
$latestFlag = '--latest=' + (-not [bool]$release.prerelease).ToString().ToLowerInvariant()
& gh release edit $release.tag --repo AidenHwang99/DemoMux --notes-file $notes $latestFlag
if ($LASTEXITCODE) { throw 'Release verification note update failed' }
