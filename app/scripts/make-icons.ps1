# Renders the Avocado mark to the raster icons Electron and Windows need.
#
# The mark is « Jeton » from the design system: a brand-green rounded square with an avocado
# cross-section knocked out of it, built from one rounded rect and three circles so it survives 16px.
# Source of truth is ds/README.md — this script is the raster derivation, not a second design.
#
#   pwsh app/scripts/make-icons.ps1
#
# Committed output: app/public/icon.png (256) and app/build/icon.ico (multi-size).

Add-Type -AssemblyName System.Drawing

$brand = [System.Drawing.ColorTranslator]::FromHtml('#2C4A38')
$flesh = [System.Drawing.Color]::White

function New-MarkBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # Everything below is the 64-unit viewBox scaled to the requested size.
    $s = $size / 64.0

    # Rounded square, rx = 16.
    $r = 16.0 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($size - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($size - 2 * $r, $size - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc(0, $size - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.SolidBrush($brand)
    $graphics.FillPath($brush, $path)

    function Fill-Circle($colour, [double]$cx, [double]$cy, [double]$radius) {
        $b = New-Object System.Drawing.SolidBrush($colour)
        $graphics.FillEllipse($b, ($cx - $radius) * $s, ($cy - $radius) * $s, 2 * $radius * $s, 2 * $radius * $s)
        $b.Dispose()
    }

    # The two tangent circles form the avocado half; the small one is the pit and returns the green.
    Fill-Circle $flesh 32 25 10
    Fill-Circle $flesh 32 37 15.5

    # Below 20px the cut widens half a point so the pit stays legible.
    $pit = if ($size -le 20) { 7.4 } else { 6.4 }
    Fill-Circle $brand 32 37 $pit

    $brush.Dispose(); $path.Dispose(); $graphics.Dispose()
    return $bitmap
}

function Get-PngBytes($bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return $stream.ToArray()
}

$root = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force -Path "$root\public", "$root\build" | Out-Null

# A single 256px PNG for the window icon, the favicon, and Linux/macOS packaging.
$large = New-MarkBitmap 256
$large.Save("$root\public\icon.png", [System.Drawing.Imaging.ImageFormat]::Png)

# Windows wants an .ico. PNG-compressed entries are understood from Vista onwards, so each size is
# embedded as a PNG rather than as a BMP with its own mask.
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = $sizes | ForEach-Object { Get-PngBytes (New-MarkBitmap $_) }

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ico)

$writer.Write([uint16]0)               # reserved
$writer.Write([uint16]1)               # type: icon
$writer.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = $sizes[$i]
    $writer.Write([byte]($(if ($dimension -ge 256) { 0 } else { $dimension })))  # 0 means 256
    $writer.Write([byte]($(if ($dimension -ge 256) { 0 } else { $dimension })))
    $writer.Write([byte]0)             # palette
    $writer.Write([byte]0)             # reserved
    $writer.Write([uint16]1)           # colour planes
    $writer.Write([uint16]32)          # bits per pixel
    $writer.Write([uint32]$images[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}

$images | ForEach-Object { $writer.Write($_) }
$writer.Flush()
[System.IO.File]::WriteAllBytes("$root\build\icon.ico", $ico.ToArray())
$writer.Dispose()

Write-Output "public\icon.png  $((Get-Item "$root\public\icon.png").Length) bytes"
Write-Output "build\icon.ico   $((Get-Item "$root\build\icon.ico").Length) bytes ($($sizes -join ', '))"
