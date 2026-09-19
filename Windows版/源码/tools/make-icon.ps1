param(
    [string]$Out = '',
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)
# 生成多尺寸 .ico：蓝色圆角方块 + 白色向下箭头 + 托盘（"把东西导出来"的意思）
# 纯 GDI+ 手绘 + 手工拼 ICO 容器，不依赖任何第三方库
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $Out) { $Out = Join-Path $PSScriptRoot '..\assets\app.ico' }
$OutDir = Split-Path -Parent $Out
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # 圆角方块
    $r = [float]($size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r, $r, 180, 90)
    $path.AddArc($size - $r, 0, $r, $r, 270, 90)
    $path.AddArc($size - $r, $size - $r, $r, $r, 0, 90)
    $path.AddArc(0, $size - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 74, 144, 255),
        [System.Drawing.Color]::FromArgb(255, 22, 82, 190),
        60.0)
    $g.FillPath($brush, $path)

    # 白色元素
    $penW = [float]([Math]::Max(1.0, $size * 0.085))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $cx = [float]($size * 0.5)
    $g.DrawLine($pen, $cx, [float]($size * 0.20), $cx, [float]($size * 0.55))          # 竖杆
    $g.DrawLine($pen, [float]($size * 0.33), [float]($size * 0.41), $cx, [float]($size * 0.585))  # 左斜
    $g.DrawLine($pen, [float]($size * 0.67), [float]($size * 0.41), $cx, [float]($size * 0.585))  # 右斜
    $g.DrawLine($pen, [float]($size * 0.26), [float]($size * 0.76), [float]($size * 0.74), [float]($size * 0.76)) # 托盘

    $pen.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# 一张位图 -> ICO 里的 BMP 数据块（BITMAPINFOHEADER + 自下而上 BGRA + AND 掩码）
function ConvertTo-IcoImageBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($w * $h * 4)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $buf = New-Object byte[] ($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
        for ($y = $h - 1; $y -ge 0; $y--) {
            $bw.Write($buf, $y * $stride, $w * 4)
        }
    } finally {
        $bmp.UnlockBits($data)
    }

    $rowBytes = [int]([Math]::Ceiling($w / 32.0) * 4)      # AND 掩码：1bpp，行按 4 字节对齐
    $zero = New-Object byte[] $rowBytes
    for ($y = 0; $y -lt $h; $y++) { $bw.Write($zero, 0, $rowBytes) }

    $bw.Flush()
    return $ms.ToArray()
}

$images = @()
foreach ($s in $Sizes) {
    $bmp = New-IconBitmap $s
    $bytes = ConvertTo-IcoImageBytes $bmp
    $bmp.Dispose()
    $images += , @{ Size = $s; Bytes = $bytes }
    Write-Output ("  {0,4}x{0,-4}  {1,8} bytes" -f $s, $bytes.Length)
}

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img.Size
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]$img.Bytes.Length)
    $bw.Write([int]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $bw.Write($img.Bytes, 0, $img.Bytes.Length) }
$bw.Flush(); $bw.Close(); $fs.Close()

Write-Output ("written: " + $Out + "  (" + (Get-Item $Out).Length + " bytes, " + $images.Count + " sizes)")
