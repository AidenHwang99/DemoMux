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
Write-Output ('Windows ICO generated: ' + ($frames.Size -join ', ') + ' px')
