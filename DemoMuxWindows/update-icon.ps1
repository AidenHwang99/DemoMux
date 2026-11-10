param(
    [string]$SourcePng = (Join-Path $PSScriptRoot 'Assets/AppIcon.png'),
    [string]$DestinationIco = (Join-Path $PSScriptRoot 'Assets/DemoMux.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$inputStream = [IO.File]::OpenRead($SourcePng)
try {
    $decoder = [Windows.Media.Imaging.BitmapDecoder]::Create($inputStream,
        [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $source = $decoder.Frames[0]
    $source.Freeze()
} finally { $inputStream.Dispose() }
if ($source.PixelWidth -ne $source.PixelHeight) { throw 'Application icon source must be square.' }
$frames = foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    $visual = [Windows.Media.DrawingVisual]::new()
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [Windows.Media.BitmapScalingMode]::HighQuality)
    $drawing = $visual.RenderOpen()
    try { $drawing.DrawImage($source, [Windows.Rect]::new(0, 0, $size, $size)) }
    finally { $drawing.Close() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [IO.MemoryStream]::new()
    try { $encoder.Save($memory); [pscustomobject]@{ Size = $size; Bytes = $memory.ToArray() } }
    finally { $memory.Dispose() }
}
$outputStream = [IO.File]::Create($DestinationIco)
$writer = [IO.BinaryWriter]::new($outputStream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }
$assets = Join-Path $PSScriptRoot 'Assets'
$logo = $frames | Where-Object Size -eq 128
$favicon = $frames | Where-Object Size -eq 32
[IO.File]::WriteAllBytes((Join-Path $assets 'GuideIcon.png'), $logo.Bytes)
$guidePath = Join-Path $assets 'Guide.html'
$guide = [IO.File]::ReadAllText($guidePath)
foreach ($entry in @(
    @{ Pattern = '(?<=data-demomux-icon="logo" src=")[^"]*'; Bytes = $logo.Bytes },
    @{ Pattern = '(?<=data-demomux-icon="favicon" href=")[^"]*'; Bytes = $favicon.Bytes }
)) {
    if ([regex]::Matches($guide, $entry.Pattern).Count -ne 1) { throw 'Guide icon marker missing or duplicated.' }
    $guide = [regex]::Replace($guide, $entry.Pattern, ('data:image/png;base64,' + [Convert]::ToBase64String($entry.Bytes)))
}
[IO.File]::WriteAllText($guidePath, $guide, [Text.UTF8Encoding]::new($false))
Write-Output ('Windows ICO generated: ' + ($frames.Size -join ', ') + ' px')
Write-Output 'Offline guide logo, favicon and Markdown icon updated from the same frames.'
