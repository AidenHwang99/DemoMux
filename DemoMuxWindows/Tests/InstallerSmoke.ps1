param([Parameter(Mandatory)][string]$Payload, [string]$PreviousPayload, [switch]$KeepInstalled)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
$id = 'DemoMux.Windows.Test-' + [guid]::NewGuid().ToString('N')
$root = Join-Path $repo ('outputs/verification/installer-' + $id)
$install = Join-Path $root '中文 安装 App'
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $id + '_is1'
New-Item -ItemType Directory -Force $root | Out-Null
$sentinel = Join-Path $root 'original-project.html'
Set-Content -LiteralPath $sentinel -Value 'untouched original project'
$sentinelHash = (Get-FileHash -LiteralPath $sentinel).Hash
$count = 0
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw ('FAIL: ' + $Name) }
    $script:count++; Write-Output ('PASS: ' + $Name)
}
function Compile([string]$Source, [string]$Name) {
    $output = Join-Path $root $Name
    & (Join-Path (Split-Path $PSScriptRoot) 'build-installer.ps1') -Payload $Source -Output $output -TestIdentity $id -TestDirectory $install *> (Join-Path $root ($Name + '-compile.log'))
    if (!$?) { throw 'Isolated installer compilation failed' }
    $result = Get-ChildItem -LiteralPath $output -Filter '*-Setup.exe'
    if (@($result).Count -ne 1) { throw 'Ambiguous test installer' }
    return $result.FullName
}
function RunSetup([string]$Setup, [string]$LogName, [bool]$Success = $true) {
    $process = Start-Process -FilePath $Setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=desktopicon',('/LOG="' + (Join-Path $root ($LogName + '.log')) + '"')) -WindowStyle Hidden -Wait -PassThru
    Check (($process.ExitCode -eq 0) -eq $Success) ($LogName + ' exit status ' + $process.ExitCode)
}
function RemoveTestInstall {
    $registration = Get-ItemProperty -LiteralPath $registry
    if ([IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\') -ne [IO.Path]::GetFullPath($install).TrimEnd('\')) { throw 'Unexpected uninstall location' }
    if (!$install.StartsWith([IO.Path]::GetFullPath($root) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Uninstall target escaped test root' }
    $exe = Join-Path $install 'unins000.exe'
    if ($registration.UninstallString.Trim('"') -ne $exe) { throw 'Unexpected uninstall command' }
    $process = Start-Process -FilePath $exe -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
    Check ($process.ExitCode -eq 0) 'isolated uninstall returned success'
    # Inno's uninstaller self-removal can outlive the initial launcher briefly.
    for ($attempt=0; $attempt -lt 20 -and (Test-Path -LiteralPath (Join-Path $install 'DemoMux.exe')); $attempt++) { Start-Sleep -Milliseconds 250 }
    Check (!(Test-Path -LiteralPath (Join-Path $install 'DemoMux.exe')) -and !(Test-Path -LiteralPath $registry)) 'owned application and registration removed'
}
$old = $null
if ($PreviousPayload) {
    $old = Compile $PreviousPayload 'previous'
    RunSetup $old 'old-version-install'
}
$setup = Compile $Payload 'current'
RunSetup $setup 'full-install-or-upgrade'
$registration = Get-ItemProperty -LiteralPath $registry
Check ($registration.InstallLocation.TrimEnd('\') -eq $install) 'per-user installation uses isolated directory'
$expectedVersion = (Get-Item -LiteralPath (Join-Path $Payload 'DemoMux.exe')).VersionInfo.FileVersion
Check ((Get-Item -LiteralPath (Join-Path $install 'DemoMux.exe')).VersionInfo.FileVersion -eq $expectedVersion) 'installed executable version matches package'
foreach ($file in (Get-Content -LiteralPath (Join-Path $Payload 'package-manifest.json') -Raw | ConvertFrom-Json)) {
    $target = Join-Path $install $file.Path
    if ((Get-FileHash -LiteralPath $target).Hash -ne $file.Sha256) { throw ('Installed hash mismatch: ' + $file.Path) }
}
Check $true 'all installed payload hashes match manifest'
$shortcutName = 'DemoMux Installation Test.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) $shortcutName
$start = Join-Path ([Environment]::GetFolderPath('Programs')) $shortcutName
$shell = New-Object -ComObject WScript.Shell
Check ((Test-Path -LiteralPath $desktop) -and $shell.CreateShortcut($desktop).TargetPath -eq (Join-Path $install 'DemoMux.exe')) 'desktop shortcut targets this installation'
Check ((Test-Path -LiteralPath $start) -and $shell.CreateShortcut($start).TargetPath -eq (Join-Path $install 'DemoMux.exe')) 'start-menu shortcut targets this installation'
Set-Content -LiteralPath (Join-Path $install 'DemoMux-Guide.html') -Value 'damage for repair test'
RunSetup $setup 'repeat-install-repair'
Check ((Get-FileHash -LiteralPath (Join-Path $install 'DemoMux-Guide.html')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $Payload 'DemoMux-Guide.html')).Hash) 'repair restores damaged program file'
if ($old) { RunSetup $old 'downgrade-blocked' $false }
$guard = [Threading.Mutex]::new($false, 'Local\DemoMux.InstallationGuard')
try { RunSetup $setup 'running-app-blocked' $false } finally { $guard.Dispose() }
Check ((Get-FileHash -LiteralPath $sentinel).Hash -eq $sentinelHash) 'original project sentinel preserved'
RemoveTestInstall
Check (!(Test-Path -LiteralPath $desktop) -and !(Test-Path -LiteralPath $start)) 'only test shortcuts removed'
Check ((Get-FileHash -LiteralPath $sentinel).Hash -eq $sentinelHash) 'uninstall preserves external original project'
RunSetup $setup 'reinstall-after-uninstall'
if (!$KeepInstalled) { RemoveTestInstall }
[ordered]@{ TestId=$id; Root=$root; Install=$install; Setup=$setup; Checks=$count; KeptInstalled=[bool]$KeepInstalled } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'result.json')
Write-Output ("$count installer checks passed; audit: " + $root)
