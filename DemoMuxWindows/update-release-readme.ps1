param([Parameter(Mandatory)][string]$Version, [switch]$Check)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version' }
$repo = Split-Path $PSScriptRoot
$readme = Join-Path $repo 'README.md'
$text = [IO.File]::ReadAllText($readme)
$tag = 'windows-v' + $Version
$asset = 'DemoMux-Windows-' + $Version + '-Setup.exe'
$base = 'https://github.com/AidenHwang99/DemoMux/releases/download/' + $tag
if ($Check) {
    if (!$text.Contains($base + '/' + $asset) -or !$text.Contains('/releases/tag/' + $tag)) { throw 'README must link the published installer and matching release' }
    Write-Output 'README installer and release links match'; return
}
# Called only after every public attachment has been downloaded and SHA-256 verified.
$json = & gh release view $tag --repo AidenHwang99/DemoMux --json isDraft,isPrerelease,assets
if ($LASTEXITCODE) { throw 'Cannot verify published release' }
$release = ($json -join "`n") | ConvertFrom-Json
if ($release.isDraft -or $release.isPrerelease -or !($release.assets | Where-Object name -eq $asset)) { throw 'Full stable installer is not public' }
$block = @"
<!-- windows-release:start -->
Windows **$Version** 正式版：**[下载安装包（推荐）]($base/$asset)** · [发行说明](https://github.com/AidenHwang99/DemoMux/releases/tag/$tag) · [SHA-256]($base/$asset.sha256)。

下载一个 EXE，双击安装即可；自动管理安装位置、开始菜单和桌面快捷方式。内置 .NET、WebView2、Caddy 和专用 Tailscale，准备组件无需另行下载，登录和公网分享仍需联网。升级先从托盘退出旧版，然后运行新安装包；账号、项目列表和偏好保留。

可选：[便携 ZIP]($base/DemoMux-Windows-x64.zip)。便携版需完整解压；安装版可在设置或 Windows「已安装的应用」中卸载，默认保留用户数据和原始项目。说明与校验附件无需单独下载即可安装使用。

附件已公开下载并逐项校验。覆盖与限制见 [Windows $Version 验收记录](docs/verification/windows-v$Version.md)。
<!-- windows-release:end -->
"@
$pattern = '(?s)<!-- windows-release:start -->.*?<!-- windows-release:end -->'
if ([regex]::Matches($text, $pattern).Count -ne 1) { throw 'README release block missing or ambiguous' }
$text = [regex]::Replace($text, $pattern, [Text.RegularExpressions.MatchEvaluator]{param($match) $block})
[IO.File]::WriteAllText($readme, $text)
& $PSCommandPath -Version $Version -Check
