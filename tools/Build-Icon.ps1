# Run with Windows PowerShell (Framework WPF). Preserves the supplied SVG paths,
# stroke color, rounded joins, transparency and aspect ratio without external tools.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase, System.Drawing
$assetDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
[xml]$svg = [IO.File]::ReadAllText((Join-Path $assetDirectory 'WindowTabManager.svg'))
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $renderSize = $size * 4
    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $scale = $renderSize * 0.94 / 71.0
    $context.PushTransform((New-Object Windows.Media.TranslateTransform(($renderSize - 71 * $scale) / 2), (($renderSize - 61 * $scale) / 2)))
    $context.PushTransform((New-Object Windows.Media.ScaleTransform($scale, $scale)))
    foreach ($path in $svg.DocumentElement.ChildNodes) {
        if ($path.LocalName -ne 'path') { continue }
        $geometry = [Windows.Media.Geometry]::Parse($path.GetAttribute('d'))
        $brush = New-Object Windows.Media.SolidColorBrush([Windows.Media.ColorConverter]::ConvertFromString($path.GetAttribute('stroke')))
        $pen = New-Object Windows.Media.Pen($brush, [double]::Parse($path.GetAttribute('stroke-width'), [Globalization.CultureInfo]::InvariantCulture))
        $pen.StartLineCap = [Windows.Media.PenLineCap]::Round
        $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
        $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
        $context.DrawGeometry($null, $pen, $geometry)
    }
    $context.Pop(); $context.Pop(); $context.Close()
    $render = New-Object Windows.Media.Imaging.RenderTargetBitmap($renderSize, $renderSize, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $render.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($render))
    $highStream = New-Object IO.MemoryStream
    $encoder.Save($highStream); $highStream.Position = 0
    $highBitmap = [Drawing.Bitmap]::FromStream($highStream)
    $bitmap = New-Object Drawing.Bitmap($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($highBitmap, (New-Object Drawing.Rectangle(0, 0, $size, $size)))
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($stream.ToArray())
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetDirectory 'WindowTabManager.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $highBitmap.Dispose(); $highStream.Dispose()
}
$iconPath = Join-Path $assetDirectory 'WindowTabManager.ico'
$file = [IO.File]::Create($iconPath)
$writer = New-Object IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output "Generated $iconPath ($($sizes -join ', ') px)"
