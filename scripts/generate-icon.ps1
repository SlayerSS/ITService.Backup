# Генерация app.ico (дизайн как app-icon.svg)
$ErrorActionPreference = "Stop"
$assets = Join-Path $PSScriptRoot "..\src\ITService.Backup\Assets"
$icoPath = Join-Path $assets "app.ico"

Add-Type -AssemblyName System.Drawing

function Add-RoundedRect([System.Drawing.Drawing2D.GraphicsPath]$path, [int]$x, [int]$y, [int]$w, [int]$h, [int]$r) {
    $path.AddArc($x, $y, $r * 2, $r * 2, 180, 90)
    $path.AddArc($x + $w - $r * 2, $y, $r * 2, $r * 2, 270, 90)
    $path.AddArc($x + $w - $r * 2, $y + $h - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc($x, $y + $h - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [int]($size * 0.06)
    $rect = New-Object System.Drawing.Rectangle $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $r = [int]($size * 0.2)

    $bgPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRect $bgPath $rect.X $rect.Y $rect.Width $rect.Height $r
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 21, 101, 192),
        [System.Drawing.Color]::FromArgb(255, 46, 125, 50),
        45.0)
    $g.FillPath($brush, $bgPath)
    $brush.Dispose()
    $bgPath.Dispose()

    $white = [System.Drawing.Brushes]::White
    $cx = $size / 2.0
    $arrowTop = $size * 0.22
    $arrowMid = $size * 0.41
    $arrowWing = $size * 0.19
    $arrowStemW = $size * 0.19
    $arrowStemBottom = $size * 0.53

    $pts = @(
        [System.Drawing.PointF]::new($cx, $arrowTop),
        [System.Drawing.PointF]::new($cx + $arrowWing, $arrowMid),
        [System.Drawing.PointF]::new($cx + $arrowStemW / 2, $arrowMid),
        [System.Drawing.PointF]::new($cx + $arrowStemW / 2, $arrowStemBottom),
        [System.Drawing.PointF]::new($cx - $arrowStemW / 2, $arrowStemBottom),
        [System.Drawing.PointF]::new($cx - $arrowStemW / 2, $arrowMid),
        [System.Drawing.PointF]::new($cx - $arrowWing, $arrowMid)
    )
    $g.FillPolygon($white, $pts)

    $barY = $size * 0.58
    $barH = $size * 0.08
    $barW = $size * 0.44
    $g.FillRectangle($white, [int]($cx - $barW / 2), [int]$barY, [int]$barW, [int]$barH)

    $boxY = $size * 0.69
    $boxH = $size * 0.16
    $boxW = $size * 0.34
    $g.FillRectangle($white, [int]($cx - $boxW / 2), [int]$boxY, [int]$boxW, [int]$boxH)

    $slotW = $size * 0.16
    $slotH = $size * 0.03
    $slotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 21, 101, 192))
    $g.FillRectangle($slotBrush, [int]($cx - $slotW / 2), [int]($boxY + $boxH * 0.3), [int]$slotW, [int]$slotH)
    $slotBrush.Dispose()

    $g.Dispose()
    return $bmp
}

function Save-IconFile([System.Drawing.Bitmap[]]$bitmaps, [string]$path) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$bitmaps.Count)
    $offset = 6 + (16 * $bitmaps.Count)
    foreach ($bmp in $bitmaps) {
        $w = $bmp.Width
        $h = $bmp.Height
        $iconW = if ($w -ge 256) { 0 } else { $w }
        $iconH = if ($h -ge 256) { 0 } else { $h }
        $bw.Write([byte]$iconW)
        $bw.Write([byte]$iconH)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $imgMs = New-Object System.IO.MemoryStream
        $bmp.Save($imgMs, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $imgMs.ToArray()
        $imgMs.Dispose()
        $bw.Write([uint32]$bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($bmp in $bitmaps) {
        $imgMs = New-Object System.IO.MemoryStream
        $bmp.Save($imgMs, [System.Drawing.Imaging.ImageFormat]::Png)
        $bw.Write($imgMs.ToArray())
        $imgMs.Dispose()
    }
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    $bw.Dispose()
    $ms.Dispose()
}

$sizes = @(16, 32, 48, 64, 128, 256)
$bitmaps = foreach ($s in $sizes) { New-IconBitmap $s }
Save-IconFile $bitmaps $icoPath
foreach ($b in $bitmaps) { $b.Dispose() }

Write-Host "Created: $icoPath" -ForegroundColor Green
