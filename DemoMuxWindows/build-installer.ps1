param([Parameter(Mandatory)][string]$Payload, [string]$Output, [string]$Iscc,
      [string]$TestIdentity, [string]$TestDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
if (!$Output) { $Output = Join-Path $repo 'outputs/DemoMuxWindows' }
$Payload = (Resolve-Path -LiteralPath $Payload).Path
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force $Output | Out-Null
if (!$Iscc) {
    $lock = Get-Content (Join-Path $PSScriptRoot 'installer-tool.json') -Raw | ConvertFrom-Json
    $tool = Join-Path $repo ('outputs/tooling/inno-' + $lock.version)
    $Iscc = Join-Path $tool 'ISCC.exe'
    if (!(Test-Path -LiteralPath $Iscc)) {
        $download = Join-Path $repo ('outputs/tooling/innosetup-' + $lock.version + '.exe')
        New-Item -ItemType Directory -Force (Split-Path $download) | Out-Null
        if (!(Test-Path -LiteralPath $download)) { Invoke-WebRequest $lock.url -OutFile $download -MaximumRetryCount 3 }
        if ((Get-FileHash -LiteralPath $download).Hash -ne $lock.sha256) { throw 'Inno Setup SHA-256 mismatch' }
        $process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS',('/DIR="' + $tool + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw 'Inno Setup compiler installation failed' }
    }
}
$version = ((Get-Item -LiteralPath (Join-Path $Payload 'DemoMux.exe')).VersionInfo.ProductVersion -split '\+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Unexpected application version' }
$arguments = @(('/DPayload=' + $Payload), ('/DOutput=' + $Output), ('/DReleaseVersion=' + $version))
if ($TestIdentity) {
    if ($TestIdentity -notmatch '^DemoMux.Windows.Test-[a-f0-9]+$' -or !$TestDirectory) { throw 'Invalid isolated installer identity' }
    $arguments += @(('/DAppIdentity=' + $TestIdentity), '/DProductName=DemoMux Installation Test', ('/DDefaultDirectory=' + [IO.Path]::GetFullPath($TestDirectory)))
}
& $Iscc @arguments (Join-Path $PSScriptRoot 'Installer/DemoMux.iss')
if ($LASTEXITCODE) { throw 'Installer compilation failed' }
$setup = Join-Path $Output ('DemoMux-Windows-' + $version + '-Setup.exe')
if (!(Test-Path -LiteralPath $setup)) { throw 'Installer missing' }
(Get-FileHash -LiteralPath $setup).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($setup) | Set-Content -LiteralPath ($setup + '.sha256') -Encoding ascii
Write-Output $setup
