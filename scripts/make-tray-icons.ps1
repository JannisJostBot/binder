# Draws the notification-area icons into src\Binder\Assets (Windows PowerShell 5.1, System.Drawing).
#   tray-dark.ico / tray-light.ico   closed: one card, monochrome, play mark cut out (dark / light taskbars)
#   tray-open-0..6.ico               opening: the back card slides out from behind the front one into the
#                                    coloured small logo (Assets\logo-small.svg); played backwards on close
# Each .ico holds the small-icon sizes for 100-300 % scaling, PNG-compressed. Geometry is in the logo's 48 x 48
# units. Run again after changing the logo:  powershell -File scripts\make-tray-icons.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\src\Binder\Assets'))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$sizes = 16, 20, 24, 28, 32, 40, 48
$frames = 7
$supersample = 8

function New-RoundRect([double]$cx, [double]$cy, [double]$w, [double]$h, [double]$r, [double]$angle) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $x = $cx - $w / 2; $y = $cy - $h / 2; $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    if ($angle -ne 0) {
        $m = New-Object System.Drawing.Drawing2D.Matrix
        $m.RotateAt([single]$angle, (New-Object System.Drawing.PointF([single]$cx, [single]$cy)))
        $p.Transform($m)
    }
    $p
}

function New-PlayMark {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(17.5, 17.5)),
        (New-Object System.Drawing.PointF(17.5, 32.5)),
        (New-Object System.Drawing.PointF(29, 25))))
    $p
}

# The front card (x 10, y 9, 23 x 32) stays put. The back card ends where logo-small.svg has it: x 16, y 7,
# 22 x 31, turned 12 degrees around (27, 41), whose centre lands at (30.85, 22.90).
$front = @{ cx = 21.5; cy = 25; w = 23; h = 32 }
$backEnd = @{ cx = 27 + 18.5 * [Math]::Sin(12 * [Math]::PI / 180); cy = 41 - 18.5 * [Math]::Cos(12 * [Math]::PI / 180); w = 22; h = 31; a = 12 }

function Draw-Frame([System.Drawing.Graphics]$g, [double]$t, [string]$mono) {
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    if (-not $mono) {
        # Ease out: quick start, soft settle (like the panel's slide).
        $e = 1 - [Math]::Pow(1 - $t, 3)
        $lerp = { param($a, $b) $a + ($b - $a) * $e }
        $back = New-RoundRect (& $lerp $front.cx $backEnd.cx) (& $lerp $front.cy $backEnd.cy) (& $lerp $front.w $backEnd.w) (& $lerp $front.h $backEnd.h) 3.5 (& $lerp 0 $backEnd.a)
        $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#6FC0F0'))), $back)
    }
    $card = New-RoundRect $front.cx $front.cy $front.w $front.h 3.5 0
    $cardColor = if ($mono) { [System.Drawing.ColorTranslator]::FromHtml($mono) } else { [System.Drawing.ColorTranslator]::FromHtml('#1688DE') }
    $g.FillPath((New-Object System.Drawing.SolidBrush($cardColor)), $card)
    $play = New-PlayMark
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 1.6)
    $pen.LineJoin = 'Round'
    if ($mono) {
        # Cut the play mark out of the glyph.
        $g.CompositingMode = 'SourceCopy'
        $pen.Color = [System.Drawing.Color]::Transparent
        $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent)), $play)
        $g.DrawPath($pen, $play)
        $g.CompositingMode = 'SourceOver'
    } else {
        $g.FillPath([System.Drawing.Brushes]::White, $play)
        $g.DrawPath($pen, $play)
    }
}

function Write-Ico([string]$path, [double]$t, [string]$mono) {
    $images = foreach ($size in $sizes) {
        $big = $size * $supersample
        $hi = New-Object System.Drawing.Bitmap($big, $big, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($hi)
        $g.ScaleTransform([single]($big / 48), [single]($big / 48))
        Draw-Frame $g $t $mono
        $g.Dispose()
        $small = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($small)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.PixelOffsetMode = 'HighQuality'
        $g.CompositingMode = 'SourceCopy'
        $g.DrawImage($hi, 0, 0, $size, $size)
        $g.Dispose(); $hi.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $small.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $small.Dispose()
        , @($size, $ms.ToArray())
    }
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($img in $images) {
        $w.Write([byte]$img[0]); $w.Write([byte]$img[0]); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$img[1].Length); $w.Write([uint32]$offset)
        $offset += $img[1].Length
    }
    foreach ($img in $images) { $w.Write([byte[]]$img[1]) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    "{0}  {1:N0} bytes" -f (Split-Path $path -Leaf), $ms.Length
}

$Out = (Resolve-Path $Out).Path
Write-Ico (Join-Path $Out 'tray-dark.ico') 0 '#FFFFFF'
Write-Ico (Join-Path $Out 'tray-light.ico') 0 '#1A1A1A'
for ($i = 0; $i -lt $frames; $i++) { Write-Ico (Join-Path $Out "tray-open-$i.ico") ($i / ($frames - 1)) $null }
