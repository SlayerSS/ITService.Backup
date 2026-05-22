# PNG banner for README (GitHub does not render SVG in <img>)
$ErrorActionPreference = "Stop"
$outDir = Join-Path $PSScriptRoot "..\docs"
$pngPath = Join-Path $outDir "readme-banner.png"

Add-Type -AssemblyName System.Drawing

# Cyrillic via code points — avoids .ps1 encoding issues on Windows PowerShell
function U([int[]]$c) { -join ($c | ForEach-Object { [char]$_ }) }
$dot = [char]0x00B7
# "Ручной бэкап 1С на USB · одна кнопка"
$subLine = (U @(0x0420,0x0443,0x0447,0x043D,0x043E,0x0439,0x0020,0x0431,0x044D,0x043A,0x0430,0x043F,0x0020,0x0031,0x0421,0x0020,0x043D,0x0430,0x0020,0x0055,0x0053,0x0042)) + " $dot " + (U @(0x043E,0x0434,0x043D,0x0430,0x0020,0x043A,0x043D,0x043E,0x043F,0x043A,0x0430))
$metaLine = "IT Service $dot " + (U @(0x041D,0x043E,0x044F,0x0431,0x0440,0x044C,0x0441,0x043A)) + " $dot it.nojabrsk.info"

function Add-RoundedRect([System.Drawing.Drawing2D.GraphicsPath]$path, [int]$x, [int]$y, [int]$w, [int]$h, [int]$r) {
    $path.AddArc($x, $y, $r * 2, $r * 2, 180, 90)
    $path.AddArc($x + $w - $r * 2, $y, $r * 2, $r * 2, 270, 90)
    $path.AddArc($x + $w - $r * 2, $y + $h - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc($x, $y + $h - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
}

$w = 920
$h = 200
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

$bgRect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $bgRect,
    [System.Drawing.Color]::FromArgb(255, 13, 71, 161),
    [System.Drawing.Color]::FromArgb(255, 27, 94, 32),
    0.0)
$bgPath = New-Object System.Drawing.Drawing2D.GraphicsPath
Add-RoundedRect $bgPath 0 0 $w $h 16
$g.FillPath($bgBrush, $bgPath)
$bgBrush.Dispose()
$bgPath.Dispose()

$iconRect = New-Object System.Drawing.Rectangle 40, 40, 120, 120
$iconBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $iconRect,
    [System.Drawing.Color]::FromArgb(255, 21, 101, 192),
    [System.Drawing.Color]::FromArgb(255, 46, 125, 50),
    45.0)
$iconPath = New-Object System.Drawing.Drawing2D.GraphicsPath
Add-RoundedRect $iconPath 40 40 120 120 28
$g.FillPath($iconBrush, $iconPath)
$iconBrush.Dispose()
$iconPath.Dispose()

$white = [System.Drawing.Brushes]::White
$g.FillPolygon($white, @(
    [System.Drawing.PointF]::new(100, 72),
    [System.Drawing.PointF]::new(124, 96),
    [System.Drawing.PointF]::new(112, 96),
    [System.Drawing.PointF]::new(112, 112),
    [System.Drawing.PointF]::new(88, 112),
    [System.Drawing.PointF]::new(88, 96),
    [System.Drawing.PointF]::new(76, 96)
))
$g.FillRectangle($white, 82, 118, 36, 8)
$g.FillRectangle($white, 86, 132, 28, 22)

$titleFont = [System.Drawing.Font]::new("Segoe UI", 36, [System.Drawing.FontStyle]::Bold)
$subFont = [System.Drawing.Font]::new("Segoe UI", 14, [System.Drawing.FontStyle]::Regular)
$metaFont = [System.Drawing.Font]::new("Segoe UI", 12, [System.Drawing.FontStyle]::Regular)

$g.DrawString("ITBackup", $titleFont, $white, 190, 48)
$g.DrawString($subLine, $subFont,
    (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 227, 242, 253))), 190, 98)
$g.DrawString($metaLine, $metaFont,
    (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 200, 230, 201))), 190, 132)

$titleFont.Dispose()
$subFont.Dispose()
$metaFont.Dispose()
$g.Dispose()

$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Host "Created: $pngPath" -ForegroundColor Green
