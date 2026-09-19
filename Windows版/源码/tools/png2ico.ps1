param(
    [Parameter(Mandatory=$true)][string]$Src,
    [string]$OutIco = '',
    [string]$PreviewPng = '',
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256),
    [switch]$KeepSquare        # 加这个开关就保留原来的方形背景，不做圆角透明
)
# 把一张方形 PNG 转成多尺寸 .ico
#   · 自动探测圆角半径，把圆角外面的底色抠成透明（Windows 图标都是这么做的）
#   · 2048 -> 256 先做一次高质量预缩放，再从 256 缩到各尺寸，小图更清楚
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$rad) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $rad, $rad, 180, 90)
    $p.AddArc($x + $w - $rad, $y, $rad, $rad, 270, 90)
    $p.AddArc($x + $w - $rad, $y + $h - $rad, $rad, $rad, 0, 90)
    $p.AddArc($x, $y + $h - $rad, $rad, $rad, 90, 90)
    $p.CloseFigure()
    return $p
}

function ConvertTo-IcoImageBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($w * $h * 4)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $buf = New-Object byte[] ($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
        for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($buf, $y * $stride, $w * 4) }
    } finally { $bmp.UnlockBits($data) }
    $rowBytes = [int]([Math]::Ceiling($w / 32.0) * 4)
    $zero = New-Object byte[] $rowBytes
    for ($y = 0; $y -lt $h; $y++) { $bw.Write($zero, 0, $rowBytes) }
    $bw.Flush()
    return $ms.ToArray()
}

# ---------------------------------------------------------------- 读原图
$img = [System.Drawing.Image]::FromFile($Src)
$bmp0 = New-Object System.Drawing.Bitmap($img)
$img.Dispose()
$N = $bmp0.Width
Write-Output ("源图: {0}x{1}" -f $bmp0.Width, $bmp0.Height)

$c00 = $bmp0.GetPixel(0, 0)
$cMid = $bmp0.GetPixel([int]($N / 2), [int]($N * 0.03))
Write-Output ("四角颜色 : R{0} G{1} B{2}" -f $c00.R, $c00.G, $c00.B)
Write-Output ("顶部中间 : R{0} G{1} B{2}" -f $cMid.R, $cMid.G, $cMid.B)

function Get-ColorDist($a, $b) { return [Math]::Abs($a.R - $b.R) + [Math]::Abs($a.G - $b.G) + [Math]::Abs($a.B - $b.B) }

# 沿第 0 行找第一个"不再是角落底色"的像素 —— 对圆角矩形来说那就是圆角半径
$rad = 0
$limit = [int]($N * 0.35)
for ($x = 0; $x -lt $limit; $x++) {
    if ((Get-ColorDist ($bmp0.GetPixel($x, 0)) $c00) -gt 24) { $rad = $x; break }
}
if ($rad -lt 4) { $rad = [int]($N * 0.16) }     # 没探到就按 16% 估
Write-Output ("探测到的圆角半径: {0} px ({1:P1})" -f $rad, ($rad / $N))

# ---------------------------------------------------------------- 抠圆角
$flat = New-Object System.Drawing.Bitmap($N, $N, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($flat)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::Transparent)
if (-not $KeepSquare) {
    $rp = New-RoundPath 0 0 $N $N $rad
    $g.SetClip($rp)
    $rp.Dispose()
}
$g.DrawImage($bmp0, 0, 0, $N, $N)
$g.Dispose()
$bmp0.Dispose()

# ---------------------------------------------------------------- 先缩到 256 备用
$master = New-Object System.Drawing.Bitmap(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($master)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)
$g.DrawImage($flat, 0, 0, 256, 256)
$g.Dispose()
$flat.Dispose()

# ---------------------------------------------------------------- 生成各尺寸 + 打包
$images = @()
$thumbs = @{}
foreach ($s in $Sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($master, 0, 0, $s, $s)
    $g.Dispose()
    $images += , @{ Size = $s; Bytes = (ConvertTo-IcoImageBytes $bmp) }
    $thumbs[$s] = $bmp
}
$master.Dispose()

if ($OutIco) {
    $dir = Split-Path -Parent $OutIco
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $fs = [System.IO.File]::Create($OutIco)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($im in $images) {
        $s = $im.Size
        $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
        $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([int16]1); $bw.Write([int16]32)
        $bw.Write([int]$im.Bytes.Length); $bw.Write([int]$offset)
        $offset += $im.Bytes.Length
    }
    foreach ($im in $images) { $bw.Write($im.Bytes, 0, $im.Bytes.Length) }
    $bw.Flush(); $bw.Close(); $fs.Close()
    Write-Output ("写出 ICO: " + $OutIco + "  (" + $images.Count + " sizes, " + (Get-Item $OutIco).Length + " bytes)")
}

# ---------------------------------------------------------------- 预览（棋盘格底，能看出透明区）
if ($PreviewPng) {
    $W = 640; $H = 320
    $sheet = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($sheet)
    $sg.Clear([System.Drawing.Color]::White)
    # 棋盘格
    $cell = 8
    $cb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 233, 236, 240))
    for ($yy = 0; $yy -lt $H; $yy += $cell) {
        for ($xx = 0; $xx -lt $W; $xx += $cell) {
            if ((([int]($xx / $cell)) + ([int]($yy / $cell))) % 2 -eq 0) { $sg.FillRectangle($cb, $xx, $yy, $cell, $cell) }
        }
    }
    $cb.Dispose()
    $titleFont = New-Object System.Drawing.Font('Microsoft YaHei UI', 13, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $labFont = New-Object System.Drawing.Font('Microsoft YaHei UI', 12, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $dark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 34, 42))
    $sg.DrawString('棋盘格 = 透明区域   左边 256 原样，右边是任务栏/标题栏上的真实大小', $titleFont, $dark, 12, 10)
    $sg.DrawImage($thumbs[256], 14, 44, 256, 256)
    $x = 290
    foreach ($s in @(64, 48, 32, 24, 16)) {
        $sg.DrawImage($thumbs[$s], $x, 60, $s, $s)
        $sg.DrawString(("$s"), $labFont, $dark, $x, 60 + $s + 6)
        $x += $s + 30
    }
    $sg.Dispose()
    $dir = Split-Path -Parent $PreviewPng
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $sheet.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    Write-Output ("预览图: " + $PreviewPng)
}
foreach ($k in $thumbs.Keys) { $thumbs[$k].Dispose() }
Write-Output "完成"
