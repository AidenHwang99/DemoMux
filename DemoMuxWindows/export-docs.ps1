param([string]$Destination = (Join-Path (Split-Path $PSScriptRoot) 'outputs/DemoMuxWindows'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$release = Get-Content (Join-Path $PSScriptRoot 'release.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force $Destination | Out-Null
$guide = Join-Path $Destination 'DemoMux-Guide.html'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets/Guide.html') -Destination $guide
$firstRun = Join-Path $Destination 'First-Run.zh-CN.md'
# Release attachments share one directory; the ZIP keeps its original Assets/ layout.
$text = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '首次使用.md')).Replace('src="Assets/GuideIcon.png"', 'src="GuideIcon.png"')
[IO.File]::WriteAllText($firstRun, $text, [Text.UTF8Encoding]::new($false))
$icon = Join-Path $Destination 'GuideIcon.png'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets/GuideIcon.png') -Destination $icon
$acceptance = Join-Path $Destination 'Acceptance.zh-CN.md'
Copy-Item -LiteralPath (Join-Path $repo $release.acceptance) -Destination $acceptance
$changelog = Join-Path $Destination 'CHANGELOG.md'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination $changelog
Write-Output $guide, $firstRun, $icon, $acceptance, $changelog
