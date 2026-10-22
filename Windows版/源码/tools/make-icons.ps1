param(
    [string]$OutDir = '',
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)
# 生成 10 个候选图标（多尺寸 .ico）+ 一张预览拼图
# 设计基准 64x64，实际绘制时按 size/64 缩放
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot '..\..\RSB提取器\图标候选' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

# ---------------------------------------------------------------- 公共绘制
function New-RoundPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$rad) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $rad, $rad, 180, 90)
    $p.AddArc($x + $w - $rad, $y, $rad, $rad, 270, 90)
    $p.AddArc($x + $w - $rad, $y + $h - $rad, $rad, $rad, 0, 90)
    $p.AddArc($x, $y + $h - $rad, $rad, $rad, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Backdrop([System.Drawing.Graphics]$g, [int]$size, [string]$c1, [string]$c2) {
    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $path = New-RoundPath 0 0 $size $size ($size * 0.22)
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, [System.Drawing.ColorTranslator]::FromHtml($c1), [System.Drawing.ColorTranslator]::FromHtml($c2), 65.0)
    $g.FillPath($br, $path)
    $br.Dispose(); $path.Dispose()
}

function New-Pen([System.Drawing.Color]$c, [float]$w) {
    $p = New-Object System.Drawing.Pen($c, $w)
    $p.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $p.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $p.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $p
}

$WHITE = [System.Drawing.Color]::White

# ---------------------------------------------------------------- 01 安卓机器人（绿）
function Draw-Icon01($g, [int]$size) {
    $k = $size / 64.0
    New-Backdrop $g $size '#4BD37B' '#0E8A48'
    $head = New-Object System.Drawing.SolidBrush($WHITE)
    $g.FillPie($head, (17*$k), (25*$k), (30*$k), (30*$k), 180, 180)          # 半圆头
    $eye = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#0E8A48'))
    $g.FillEllipse($eye, (25*$k), (31*$k), (4*$k), (4*$k))                   # 左眼
    $g.FillEllipse($eye, (35*$k), (31*$k), (4*$k), (4*$k))                   # 右眼
    $pen = New-Pen $WHITE ([Math]::Max(1.0, 2.6*$k))
    $g.DrawLine($pen, (23*$k), (21*$k), (19*$k), (13*$k))                    # 天线
    $g.DrawLine($pen, (41*$k), (21*$k), (45*$k), (13*$k))
    $g.FillRectangle($head, (17*$k), (42*$k), (30*$k), (8*$k))               # 身体
    $pen.Dispose(); $head.Dispose(); $eye.Dispose()
}

# ---------------------------------------------------------------- 02 打开的纸箱（橙）
function Draw-Icon02($g, [int]$size) {
    $k = $size / 64.0
    New-Backdrop $g $size '#FFB24D' '#DC6A00'
    $b = New-Object System.Drawing.SolidBrush($WHITE)
    $body = New-RoundPath (15*$k) (28*$k) (34*$k) (24*$k) (3*$k)
    $g.FillPath($b, $body)
    $pen = New-Pen $WHITE ([Math]::Max(1.0, 3.2*$k))
    $g.DrawLine($pen, (17*$k), (28*$k), (10*$k), (17*$k))                    # 左盖
    $g.DrawLine($pen, (47*$k), (28*$k), (54*$k), (17*$k))                    # 右盖
    $carve = New-Pen ([System.Drawing.ColorTranslator]::FromHtml('#DC6A00')) ([Math]::Max(1.0, 2.4*$k))
    $g.DrawLine($carve, (32*$k), (28*$k), (32*$k), (50*$k))                  # 箱缝
    $pen.Dispose(); $carve.Dispose(); $b.Dispose(); $body.Dispose()
}

# ---------------------------------------------------------------- 03 放大镜看文件（深蓝）
function Draw-Icon03($g, [int]$size) {
    $k = $size / 64.0
    New-Backdrop $g $size '#5B84F0' '#1B3E9E'
    $pen = New-Pen $WHITE ([Math]::Max(1.4, 5.0*$k))
    $g.DrawEllipse($pen, (14*$k), (12*$k), (30*$k), (30*$k))                 # 镜圈
    $g.DrawLine($pen, (41*$k), (41*$k), (52*$k), (52*$k))                    # 手柄
    $b = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#1B3E9E'))
    $g.FillRectangle($b, (24*$k), (22*$k), (10*$k), (2.6*$k))                # 镜片里的"文字"
    $g.FillRectangle($b, (24*$k), (28*$k), (10*$k), (2.6*$k))
    $g.FillRectangle($b, (24*$k), (34*$k), (6*$k), (2.6*$k))
    $pen.Dispose(); $b.Dispose()
}

# ---------------------------------------------------------------- 04 文件夹 + 下箭头（琥珀）
function Draw-Icon04($g, [int]$size) {
    $k = $size / 64.0
    New-Backdrop $g $size '#FFCB47' '#E08A00'
    $b = New-Object System.Drawing.SolidBrush($WHITE)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddLines(@(
        (New-Object System.Drawing.PointF((12*$k), (18*$k))),
        (New-Object System.Drawing.PointF((27*$k), (18*$k))),
        (New-Object System.Drawing.PointF((31*$k), (23*$k))),
        (New-Object System.Drawing.PointF((52*$k), (23*$k))),
        (New-Object System.Drawing.PointF((52*$k), (47*$k))),
        (New-Object System.Drawing.PointF((12*$k), (47*$k)))
    ))
    $p.CloseFigure()
    $g.FillPath($b, $p)
    $carve = New-Pen ([System.Drawing.ColorTranslator]::FromHtml('#E08A00')) ([Math]::Max(1.0, 2.6*$k))
    $g.DrawLine($carve, (32*$k), (29*$k), (32*$k), (41*$k))                  # 箭头杆
    $g.DrawLine($carve, (27*$k), (36*$k), (32*$k), (42*$k))                  # 箭头
    $g.DrawLine($carve, (37*$k), (36*$k), (32*$k), (42*$k))
    $carve.Dispose(); $b.Dispose(); $p.Dispose()
}

# ---------------------------------------------------------------- 05 豌豆射手（草绿）
function Draw-Icon05($g, [int]$size) {
    $k = $size / 64.0
    New-Backdrop $g $size '#8BE05C' '#2F8E14'
    $b = New-Object System.Drawing.SolidBrush($WHITE)
    $g.FillEllipse($b, (12*$k), (16*$k), (30*$k), (30*$k))                   # 头
    $g.FillRectangle($b, (28*$k), (25*$k), (18*$k), (13*$k))                 # 炮管
    $carve = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#2F8E14'))
    $g.FillEllipse($carve, (44*$k), (27*$k), (5*$k), (9*$k))                 # 管口
    $g.FillEllipse($carve, (22*$k), (26*$k), (5*$k), (5*$k))                 # 眼
    $pen = New-Pen $WHITE ([Math]::Max(1.0, 2.6*$k))
    $g.DrawLine($pen, (27*$k), (46*$k), (27*$k), (54*$k))                    # 茎
    $pen.Dispose(); $b.Dispose(); $carve.Dispose()
}

# ---------------------------------------------------------------- 06 僵尸头（紫灰）
function Draw-Icon06($g, [int]$size) {
    $k = $size / 64.0
    New-Backdrop $g $size '#A695B2' '#4E3D5C'
    $b = New-Object System.Drawing.SolidBrush($WHITE)
    $head = New-RoundPath (14*$k) (13*$k) (36*$k) (38*$k) (6*$k)
    $g.FillPath($b, $head)
    $carve = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#4E3D5C'))
    $g.FillRectangle($carve, (21*$k), (24*$k), (7*$k), (7*$k))               # 左眼
    $g.FillRectangle($carve, (36*$k), (24*$k), (7*$k), (7*$k))               # 右眼
    $g.FillRectangle($carve, (22*$k), (38*$k), (20*$k), (2.6*$k))            # 嘴
    $g.FillRectangle($carve, (26*$k), (40*$k), (2.4*$k), (4*$k))             # 牙
    $g.FillRectangle($carve, (32*$k), (40*$k), (2.4*$k), (4*$k))
    $b.Dispose(); $head.Dispose(); $carve.Dispose()
}

# ---------------------------------------------------------------- 07 包裹（棕）
function Draw-Icon07($g, [int]$size) {
    $k = $size / 64.0
    New-Backdrop $g $size '#D79A63' '#8A5628'
    $b = New-Object System.Drawing.SolidBrush($WHITE)
    $box = New-RoundPath (12*$k) (18*$k) (40*$k) (30*$k) (3*$k)
    $g.FillPath($b, $box)
    $carve = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#8A5628'))
    $g.FillRectangle($carve, (29*$k), (18*$k), (6*$k), (30*$k))              # 竖带
    $g.FillRectangle($carve, (12*$k), (30*$k), (40*$k), (6*$k))              # 横带
    $pen = New-Pen ([System.Drawing.ColorTranslator]::FromHtml('#8A5628')) ([Math]::Max(1.0, 2.4*$k))
    $g.DrawLine($pen, (32*$k), (18*$k), (26*$k), (11*$k))                    # 蝴蝶结
    $g.DrawLine($pen, (32*$k), (18*$k), (38*$k), (11*$k))
    $pen.Dispose(); $b.Dispose(); $box.Dispose(); $carve.Dispose()
}

# ---------------------------------------------------------------- 08 文件堆（蓝）
function Draw-Icon08($g, [int]$size) {
    $k = $size / 64.0
    New-Backdrop $g $size '#6BB6F5' '#1567B5'
    $dim = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 205, 228, 250))
    $g.FillRectangle($dim, (20*$k), (12*$k), (28*$k), (36*$k))               # 后面两张
    $g.FillRectangle($dim, (16*$k), (16*$k), (28*$k), (36*$k))
    $b = New-Object System.Drawing.SolidBrush($WHITE)
    $g.FillRectangle($b, (12*$k), (20*$k), (30*$k), (32*$k))                 # 最前面一张
    $carve = New-Pen ([System.Drawing.ColorTranslator]::FromHtml('#1567B5')) ([Math]::Max(1.0, 2.4*$k))
    $g.DrawLine($carve, (18*$k), (28*$k), (36*$k), (28*$k))
    $g.DrawLine($carve, (18*$k), (34*$k), (36*$k), (34*$k))
    $g.DrawLine($carve, (18*$k), (40*$k), (30*$k), (40*$k))
    $carve.Dispose(); $b.Dispose(); $dim.Dispose()
}

# ---------------------------------------------------------------- 09 字母 A + 箭头（靛）
function Draw-Icon09($g, [int]$size) {
    $k = $size / 64.0
    New-Backdrop $g $size '#8C8CF0' '#3434A8'
    $font = New-Object System.Drawing.Font('Arial', (40*$k), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $b = New-Object System.Drawing.SolidBrush($WHITE)
    $rect = New-Object System.Drawing.RectangleF(0, (-3*$k), $size, $size)
    $g.DrawString('A', $font, $b, $rect, $sf)
    $pen = New-Pen $WHITE ([Math]::Max(1.2, 3.0*$k))
    $g.DrawLine($pen, (44*$k), (36*$k), (44*$k), (50*$k))                    # 右下角小箭头
    $g.DrawLine($pen, (39*$k), (45*$k), (44*$k), (51*$k))
    $g.DrawLine($pen, (49*$k), (45*$k), (44*$k), (51*$k))
    $pen.Dispose(); $b.Dispose(); $font.Dispose(); $sf.Dispose()
}

# ---------------------------------------------------------------- 10 抽屉（青）
function Draw-Icon10($g, [int]$size) {
    $k = $size / 64.0
    New-Backdrop $g $size '#55D6D6' '#0F7E7E'
    $b = New-Object System.Drawing.SolidBrush($WHITE)
    $box = New-RoundPath (11*$k) (13*$k) (42*$k) (38*$k) (4*$k)
    $g.FillPath($b, $box)
    $carve = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#0F7E7E'))
    for ($i = 0; $i -lt 3; $i++) {
        $y = (17 + $i * 12) * $k
        $g.FillRectangle($carve, (16*$k), $y, (32*$k), (9*$k))               # 每格挖空
        $g.FillRectangle($b, (28*$k), ($y + 3.6*$k), (8*$k), (2.2*$k))       # 把手
    }
    $b.Dispose(); $box.Dispose(); $carve.Dispose()
}

# ---------------------------------------------------------------- ICO 打包
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

function Save-Ico([string]$path, $images) {
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($img in $images) {
        $s = $img.Size
        $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
        $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([int16]1); $bw.Write([int16]32)
        $bw.Write([int]$img.Bytes.Length); $bw.Write([int]$offset)
        $offset += $img.Bytes.Length
    }
    foreach ($img in $images) { $bw.Write($img.Bytes, 0, $img.Bytes.Length) }
    $bw.Flush(); $bw.Close(); $fs.Close()
}

# ---------------------------------------------------------------- 主流程
$icons = @(
    @{ N = 1;  File = '01-安卓机器人';  Key = 'Draw-Icon01' },
    @{ N = 2;  File = '02-打开的纸箱';  Key = 'Draw-Icon02' },
    @{ N = 3;  File = '03-放大镜看文件'; Key = 'Draw-Icon03' },
    @{ N = 4;  File = '04-文件夹加箭头'; Key = 'Draw-Icon04' },
    @{ N = 5;  File = '05-豌豆射手';    Key = 'Draw-Icon05' },
    @{ N = 6;  File = '06-僵尸头';      Key = 'Draw-Icon06' },
    @{ N = 7;  File = '07-包裹';        Key = 'Draw-Icon07' },
    @{ N = 8;  File = '08-文件堆';      Key = 'Draw-Icon08' },
    @{ N = 9;  File = '09-字母A加箭头'; Key = 'Draw-Icon09' },
    @{ N = 10; File = '10-抽屉';        Key = 'Draw-Icon10' }
)

Write-Output ("输出目录: " + $OutDir)
$previews = @()

foreach ($ic in $icons) {
    $images = @()
    foreach ($s in $Sizes) {
        $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.Clear([System.Drawing.Color]::Transparent)
        & $ic.Key $g $s
        $g.Dispose()
        $images += , @{ Size = $s; Bytes = (ConvertTo-IcoImageBytes $bmp) }
        if ($s -eq 128) { $previews += , @{ N = $ic.N; Name = $ic.File; Bmp = $bmp } } else { $bmp.Dispose() }
    }
    $path = Join-Path $OutDir ($ic.File + '.ico')
    Save-Ico $path $images
    Write-Output ("  {0,-16} -> {1}  ({2} sizes, {3:N0} bytes)" -f $ic.File, (Split-Path $path -Leaf), $images.Count, (Get-Item $path).Length)
}

# ---- 预览拼图：每行 [128 图标] [48][32][16 实际尺寸] [编号+名字]
$rowH = 150
$W = 780
$H = 40 + $rowH * $previews.Count
$sheet = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$sg.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$sg.Clear([System.Drawing.Color]::White)
$titleFont = New-Object System.Drawing.Font('Microsoft YaHei UI', 13, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$nameFont = New-Object System.Drawing.Font('Microsoft YaHei UI', 15, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$dark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 34, 42))
$gray = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 120, 126, 136))
$sg.DrawString('点击编号告诉我选哪个（小图是它们在 48 / 32 / 16 像素下的真实样子）', $titleFont, $dark, 14, 12)

$y = 40
foreach ($p in $previews) {
    $sg.DrawImage($p.Bmp, 14, $y + 8, 128, 128)
    $x = 168
    foreach ($sz in @(48, 32, 16)) {
        $scaled = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g2 = [System.Drawing.Graphics]::FromImage($scaled)
        $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g2.Clear([System.Drawing.Color]::Transparent)
        $g2.DrawImage($p.Bmp, 0, 0, $sz, $sz)
        $g2.Dispose()
        $sg.DrawImage($scaled, $x, $y + 8 + (48 - $sz), $sz, $sz)
        $scaled.Dispose()
        $x += $sz + 14
    }
    $sg.DrawString($p.Name, $nameFont, $dark, 300, $y + 56)
    $y += $rowH
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 232, 235, 240), 1)
    $sg.DrawLine($pen, 10, $y, $W - 10, $y)
    $pen.Dispose()
}
$sg.Dispose()

# 预览图放工作区里（vision 工具只能读工作区内的文件），同时拷一份到输出目录
$sheetPath = Join-Path $PSScriptRoot '..\界面截图\图标候选预览.png'
$sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Save((Join-Path $OutDir '预览-10个候选.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output ("预览图: " + (Resolve-Path $sheetPath).Path)
foreach ($p in $previews) { $p.Bmp.Dispose() }
$sheet.Dispose()
Write-Output "完成"
