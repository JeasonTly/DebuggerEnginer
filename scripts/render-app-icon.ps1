# 生成 TestEngineer 应用图标。
# 场景：安卓调试工作台。蓝色工作台底 + 白色设备，屏幕里是控制台提示符。
# 不用 Android 机器人商标，不加渐变和投影。
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\src\TestEngineer.Desktop\Assets")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function Add-RoundRect {
    param(
        [System.Drawing.Drawing2D.GraphicsPath]$Path,
        [single]$X, [single]$Y, [single]$W, [single]$H, [single]$R
    )
    $R = [Math]::Min($R, [Math]::Min($W, $H) / 2)
    $d = $R * 2
    if ($d -lt 0.8) {
        $Path.AddRectangle([System.Drawing.RectangleF]::new($X, $Y, $W, $H))
        return
    }
    $Path.AddArc($X, $Y, $d, $d, 180, 90)
    $Path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $Path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $Path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $Path.CloseFigure()
}

function Draw-AppIcon {
    param([int]$Pixels, [int]$Logical)

    $bmp = New-Object System.Drawing.Bitmap $Pixels, $Pixels, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]$Pixels
    $tileColor = [System.Drawing.Color]::FromArgb(255, 0x2F, 0x6F, 0xED)
    $glassColor = [System.Drawing.Color]::FromArgb(255, 0x14, 0x3C, 0x9E)
    $white = [System.Drawing.Color]::White

    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundRect $tile 0 0 $s $s ($s * 0.22)
    $tileBrush = New-Object System.Drawing.SolidBrush $tileColor
    $g.FillPath($tileBrush, $tile)

    $pw = $s * 0.50
    $ph = $s * 0.72
    $px = ($s - $pw) / 2
    $py = ($s - $ph) / 2 - ($s * 0.012)
    $phone = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundRect $phone $px $py $pw $ph ($s * 0.075)
    $whiteBrush = New-Object System.Drawing.SolidBrush $white
    $g.FillPath($whiteBrush, $phone)

    $bezelRatio = if ($Logical -le 20) { 0.078 } else { 0.052 }
    $bezel = $s * $bezelRatio
    $showHome = $Logical -ge 24
    $homeBand = if ($showHome) { $s * 0.10 } else { $bezel }
    $sx = $px + $bezel
    $sy = $py + $bezel
    $sw = $pw - (2 * $bezel)
    $sh = $ph - $bezel - $homeBand
    $glass = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundRect $glass $sx $sy $sw $sh ($s * 0.032)
    $glassBrush = New-Object System.Drawing.SolidBrush $glassColor
    $g.FillPath($glassBrush, $glass)

    if ($showHome) {
        $barW = $pw * 0.26
        $barH = [Math]::Max($s * 0.02, $s * 0.026)
        $barX = $px + ($pw - $barW) / 2
        $barY = $sy + $sh + (($homeBand - $bezel - $barH) / 2)
        $bar = New-Object System.Drawing.Drawing2D.GraphicsPath
        Add-RoundRect $bar $barX $barY $barW $barH ($barH / 2)
        $g.FillPath($glassBrush, $bar)
        $bar.Dispose()
    }

    $strokeRatio = if ($Logical -le 20) { 0.105 } elseif ($Logical -le 32) { 0.078 } else { 0.05 }
    $stroke = $s * $strokeRatio
    $pen = New-Object System.Drawing.Pen $white, $stroke
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $showUnderscore = $Logical -ge 48
    $chevH = $sh * $(if ($showUnderscore) { 0.48 } else { 0.58 })
    $top = $sy + (($sh - $chevH) / 2)
    $bottom = $top + $chevH
    $midY = ($top + $bottom) / 2
    $innerW = $sw * 0.74
    $chevW = if ($showUnderscore) { $innerW * 0.40 } else { $innerW * 0.72 }
    $usW = $innerW * 0.36
    $gap = $innerW * 0.08
    $groupW = if ($showUnderscore) { $chevW + $gap + $usW } else { $chevW }
    $innerLeft = $sx + (($sw - $groupW) / 2)
    $tipX = $innerLeft + $chevW
    $g.DrawLines($pen, @(
        (New-Object System.Drawing.PointF $innerLeft, $top),
        (New-Object System.Drawing.PointF $tipX, $midY),
        (New-Object System.Drawing.PointF $innerLeft, $bottom)
    ))

    if ($showUnderscore) {
        $ux = $tipX + $gap
        $uy = $bottom - ($stroke / 2)
        $mark = New-Object System.Drawing.Drawing2D.GraphicsPath
        Add-RoundRect $mark $ux $uy $usW $stroke ($stroke / 2)
        $g.FillPath($whiteBrush, $mark)
        $mark.Dispose()
    }

    $pen.Dispose()
    $glassBrush.Dispose()
    $glass.Dispose()
    $whiteBrush.Dispose()
    $phone.Dispose()
    $tileBrush.Dispose()
    $tile.Dispose()
    $g.Dispose()
    return $bmp
}

function New-AppIconBitmap {
    param([int]$Size)
    $factor = if ($Size -le 48) { 8 } elseif ($Size -le 64) { 4 } else { 1 }
    $drawn = Draw-AppIcon -Pixels ($Size * $factor) -Logical $Size
    if ($factor -eq 1) { return $drawn }

    $out = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($drawn, (New-Object System.Drawing.Rectangle 0, 0, $Size, $Size))
    $g.Dispose()
    $drawn.Dispose()
    return $out
}

function Save-PngIco {
    param(
        [System.Drawing.Bitmap[]]$Frames,
        [string]$Path
    )
    $blobs = foreach ($frame in $Frames) {
        $ms = New-Object System.IO.MemoryStream
        $frame.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        $ms.Dispose()
        , $bytes
    }
    $stream = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$blobs.Count)
    $offset = 6 + (16 * $blobs.Count)
    for ($i = 0; $i -lt $blobs.Count; $i++) {
        $side = $Frames[$i].Width
        $dim = if ($side -ge 256) { [byte]0 } else { [byte]$side }
        $writer.Write($dim)
        $writer.Write($dim)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$blobs[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $blobs[$i].Length
    }
    foreach ($blob in $blobs) { $writer.Write($blob) }
    $writer.Flush()
    $writer.Dispose()
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($size in $sizes) { New-AppIconBitmap $size }

$pngPath = Join-Path $OutDir "AppIcon.png"
$frames[-1].Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$icoPath = Join-Path $OutDir "App.ico"
Save-PngIco -Frames $frames -Path $icoPath

$sheetSizes = 16, 24, 32, 48, 256
$pad = 16
$cell = 256
$sheetW = $pad + ($sheetSizes.Count * ($cell + $pad))
$sheetH = $pad + (2 * ($cell + $pad))
$sheet = New-Object System.Drawing.Bitmap $sheetW, $sheetH, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.Clear([System.Drawing.Color]::FromArgb(255, 0xE6, 0xEA, 0xF0))
$sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x1A, 0x1F, 0x28))
for ($row = 0; $row -lt 2; $row++) {
    for ($col = 0; $col -lt $sheetSizes.Count; $col++) {
        $x = $pad + ($col * ($cell + $pad))
        $y = $pad + ($row * ($cell + $pad))
        if ($row -eq 1) { $sg.FillRectangle($dark, $x, $y, $cell, $cell) }
        $src = New-AppIconBitmap $sheetSizes[$col]
        $sg.DrawImage($src, (New-Object System.Drawing.Rectangle $x, $y, $cell, $cell))
        $src.Dispose()
    }
}
$dark.Dispose()
$sg.Dispose()
$preview = Join-Path $env:TEMP "te-app-icon-preview.png"
$sheet.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
foreach ($frame in $frames) { $frame.Dispose() }

Write-Output "png=$pngPath"
Write-Output "ico=$icoPath"
Write-Output "preview=$preview"
