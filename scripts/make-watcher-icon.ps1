# Draws the watcher's icon, src\Binder.Watcher\watcher.ico (Windows PowerShell 5.1, System.Drawing): the app's
# logo (Assets\logo.svg, the fanned cover stack; Assets\logo-small.svg's two flat covers at 16-24 px) with an eye
# on the front cover instead of the play mark, since the watcher watches the game. Each size is PNG-compressed.
# Run again after changing the logo:  powershell -File scripts\make-watcher-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\src\Binder.Watcher\watcher.ico'))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$supersample = 8

function Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function New-RoundRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# One cover: a rounded rect turned by $angle around ($ox, $oy), as in the SVG's rotate(angle ox oy).
function Draw-Card($g, [double]$x, [double]$y, [double]$w, [double]$h, [double]$r, [double]$angle, [double]$ox, [double]$oy, $from, $to) {
    $state = $g.Save()
    if ($angle -ne 0) {
        $g.TranslateTransform([single]$ox, [single]$oy)
        $g.RotateTransform([single]$angle)
        $g.TranslateTransform([single](-$ox), [single](-$oy))
    }
    $path = New-RoundRect $x $y $w $h $r
    if ($to) {
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF([single]$x, [single]$y)),
            (New-Object System.Drawing.PointF([single]($x + $w), [single]($y + $h))), (Color $from), (Color $to))
    } else {
        $brush = New-Object System.Drawing.SolidBrush((Color $from))
    }
    $g.FillPath($brush, $path)
    $g.Restore($state)
}

# The eye: an almond outline and a round pupil, centred on ($cx, $cy), $w wide.
function Draw-Eye($g, [double]$cx, [double]$cy, [double]$w, [double]$stroke, [double]$pupil) {
    $h = $w * 0.62
    $l = $cx - $w / 2; $r = $cx + $w / 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddBezier([single]$l, [single]$cy, [single]($cx - $w / 4), [single]($cy - $h / 1.4), [single]($cx + $w / 4), [single]($cy - $h / 1.4), [single]$r, [single]$cy)
    $p.AddBezier([single]$r, [single]$cy, [single]($cx + $w / 4), [single]($cy + $h / 1.4), [single]($cx - $w / 4), [single]($cy + $h / 1.4), [single]$l, [single]$cy)
    $p.CloseFigure()
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single]$stroke)
    $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $p)
    $g.FillEllipse([System.Drawing.Brushes]::White, [single]($cx - $pupil), [single]($cy - $pupil), [single](2 * $pupil), [single](2 * $pupil))
}

function Draw-Logo($g, [int]$size) {
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    if ($size -le 24) {
        # logo-small.svg: two flat covers, a bolder mark.
        Draw-Card $g 16 7 22 31 3.5 12 27 41 '#6FC0F0' $null
        Draw-Card $g 10 9 23 32 3.5 0 0 0 '#1688DE' $null
        Draw-Eye $g 21.5 25 16 2.2 3.1
    } else {
        # logo.svg: three fanned covers with gradients.
        Draw-Card $g 11 8 20 28 3 -14 21 40 '#E3F5FF' '#9CCDEF'
        Draw-Card $g 17 8 20 28 3 10 27 40 '#B4E6FF' '#4AA6E6'
        Draw-Card $g 14 10 20 28 3 0 0 0 '#7BD8FF' '#0C78D2'
        Draw-Eye $g 24 24 13 1.5 2.3
    }
}

$images = foreach ($size in $sizes) {
    $big = [Math]::Min($size * $supersample, 2048)
    $hi = New-Object System.Drawing.Bitmap($big, $big, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($hi)
    $g.ScaleTransform([single]($big / 48), [single]($big / 48))
    Draw-Logo $g $size
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
    $dim = if ($img[0] -ge 256) { 0 } else { $img[0] } # 0 means 256 in an icon directory
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$img[1].Length); $w.Write([uint32]$offset)
    $offset += $img[1].Length
}
foreach ($img in $images) { $w.Write([byte[]]$img[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes($Out, $ms.ToArray())
"{0}  {1:N0} bytes" -f (Split-Path $Out -Leaf), $ms.Length

# A preview strip of every size, for checking by eye (not shipped).
if ($env:PREVIEW) {
    $strip = New-Object System.Drawing.Bitmap(700, 280)
    $g = [System.Drawing.Graphics]::FromImage($strip)
    $g.Clear((Color '#202024'))
    $x = 8
    foreach ($img in $images) {
        $bmp = [System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream(, $img[1])))
        $g.DrawImage($bmp, $x, 8, $img[0], $img[0])
        $x += $img[0] + 12
    }
    $strip.Save($env:PREVIEW)
}
