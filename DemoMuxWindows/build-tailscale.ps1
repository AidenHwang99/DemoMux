param([string]$Go = 'go', [string]$SourceArchive, [string]$Output)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
if (!$Output) { $Output = Join-Path $repo 'outputs/TailscaleWindows' }
New-Item -ItemType Directory -Force $Output | Out-Null
$lock = Get-Content (Join-Path $PSScriptRoot 'Components/source.json') -Raw | ConvertFrom-Json
if ((& $Go version) -notmatch ('go' + [regex]::Escape($lock.goVersion) + ' ')) { throw 'Go SDK version does not match source lock' }
$stage = Join-Path $Output ('build-' + [guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $stage 'source'
$payload = Join-Path $stage 'payload'
New-Item -ItemType Directory -Force $sourceRoot,$payload | Out-Null
if (!$SourceArchive) { $SourceArchive = Join-Path $stage 'source.zip'; Invoke-WebRequest $lock.sourceUrl -OutFile $SourceArchive }
if ((Get-FileHash -LiteralPath $SourceArchive -Algorithm SHA256).Hash -ne $lock.sourceSha256) { throw 'Tailscale source SHA-256 mismatch' }
Expand-Archive -LiteralPath $SourceArchive -DestinationPath $sourceRoot
$source = (Get-ChildItem -LiteralPath $sourceRoot -Directory | Select-Object -First 1).FullName
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Components/atomicfile_windows.go') -Destination (Join-Path $source 'atomicfile/atomicfile_windows.go') -Force
$env:CGO_ENABLED = '0'; $env:GOOS = 'windows'; $env:GOARCH = 'amd64'; $env:GOTOOLCHAIN = 'local'
$flags = '-s -w -X tailscale.com/version.shortStamp=' + $lock.upstreamVersion + ' -X tailscale.com/version.longStamp=' + $lock.version + ' -X tailscale.com/version.gitCommitStamp=' + $lock.commit
foreach ($binary in @('tailscale','tailscaled')) {
    & $Go -C $source build -mod=readonly -buildvcs=false -trimpath -ldflags $flags -o (Join-Path $payload ($binary + '.exe')) ('./cmd/' + $binary)
    if ($LASTEXITCODE) { throw ('Go build failed: ' + $binary) }
}
$licenses = Join-Path $payload 'licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $licenses 'Tailscale.txt')
$goRoot = & $Go env GOROOT
Copy-Item -LiteralPath (Join-Path $goRoot 'LICENSE') -Destination (Join-Path $licenses 'Go.txt')
$modules = & $Go -C $source list -mod=readonly -deps -f '{{with .Module}}{{.Path}}|{{.Version}}|{{.Dir}}{{end}}' ./cmd/tailscale ./cmd/tailscaled
if ($LASTEXITCODE) { throw 'Go module license discovery failed' }
foreach ($module in ($modules | Where-Object { $_ -match '\|' } | Sort-Object -Unique)) {
    $parts = $module.Split('|'); if (!$parts[1]) { continue }
    $target = Join-Path $licenses ($parts[0] + '@' + $parts[1])
    foreach ($file in (Get-ChildItem -LiteralPath $parts[2] -File | Where-Object Name -Match '^(LICENSE|LICENCE|NOTICE|COPYING|COPYRIGHT)([.-]|$)')) {
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Components/source.json') -Destination (Join-Path $payload 'SOURCE.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Components/atomicfile_windows.go') -Destination (Join-Path $payload 'atomicfile_windows.go')
# NoCompression and fixed timestamps make the dependency archive deterministic across .NET hosts.
Add-Type -AssemblyName System.IO.Compression
$archive = Join-Path $Output ('Tailscale-' + $lock.version + '-Windows-x64.zip')
$stream = [IO.File]::Create($archive)
$zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
    $paths = [string[]](Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/') })
    [Array]::Sort($paths,[StringComparer]::Ordinal)
    foreach ($path in $paths) {
        $entry = $zip.CreateEntry($path,[IO.Compression.CompressionLevel]::NoCompression)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
        $entry.ExternalAttributes = 0
        $input = [IO.File]::OpenRead((Join-Path $payload $path)); $destination = $entry.Open()
        try { $input.CopyTo($destination) } finally { $destination.Dispose(); $input.Dispose() }
    }
} finally { $zip.Dispose(); $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archive + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($archive)) -Encoding ascii
Write-Output $archive
Write-Output $hash
