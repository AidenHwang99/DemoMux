param([Parameter(Mandatory)][string]$Payload, [string]$Cache = $env:DEMOMUX_COMPONENT_CACHE)
$ErrorActionPreference = 'Stop'
$bundle = Join-Path $Payload 'BundledComponents'
New-Item -ItemType Directory -Force $bundle | Out-Null
$components = Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
foreach ($component in $components) {
    $name = [IO.Path]::GetFileName(([Uri]$component.url).AbsolutePath)
    $target = Join-Path $bundle $name
    $cached = if ($Cache) { Join-Path $Cache $name } else { $null }
    if ($cached -and (Test-Path -LiteralPath $cached)) { Copy-Item -LiteralPath $cached -Destination $target }
    else { Invoke-WebRequest $component.url -OutFile $target -MaximumRetryCount 3 -TimeoutSec 180 }
    if ((Get-FileHash -LiteralPath $target).Hash -ne $component.sha256) { throw ($component.name + ' bundle SHA-256 mismatch') }
    $zip = [IO.Compression.ZipFile]::OpenRead($target)
    try {
        foreach ($file in $component.files.PSObject.Properties) {
            $entry = $zip.GetEntry($file.Name)
            if (!$entry) { throw ('Missing component binary: ' + $file.Name) }
            $stream = $entry.Open()
            try { $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($sha -ne $file.Value) { throw ('Invalid component binary: ' + $file.Name) }
        }
    } finally { $zip.Dispose() }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Destination (Join-Path $bundle 'complete.json')
$notices = Join-Path $Payload 'ThirdParty'
New-Item -ItemType Directory -Force $notices | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Components/README.md') -Destination (Join-Path $notices 'Tailscale-DemoMux.md')
