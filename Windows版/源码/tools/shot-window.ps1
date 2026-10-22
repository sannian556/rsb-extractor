param(
    [string]$Title = 'RSB 提取器',
    [string]$Proc  = 'RSB提取器',
    [string]$Out   = "$env:TEMP\rsb-shot.png",
    [string]$Exe   = '',
    [string[]]$ExeArgs = @(),
    [int]$WaitMs   = 2500
)
# 给窗口截图（**PrintWindow**：不抢焦点、被别的窗口盖住也能拍到）
#
#   · 找窗口用进程的 MainWindowHandle —— 不要用 FindWindow(null, 标题)：
#     PowerShell 把 $null 传给 P/Invoke 的 string 参数会变成**空字符串**，
#     FindWindow 就会去找一个"类名叫空字符串"的窗口，永远找不到（踩过）。
#   · /codepage 无关，但 .ps1 含中文必须 UTF-8 带 BOM，否则 PS 5.1 按 GBK 读会乱码。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace W -Name Api -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@

[void][W.Api]::SetProcessDPIAware()

$launched = $null
if ($Exe -ne '') {
    if (-not (Test-Path $Exe)) { throw "找不到 exe：$Exe" }
    $launched = Start-Process -FilePath $Exe -ArgumentList $ExeArgs -PassThru
    Start-Sleep -Milliseconds $WaitMs
}

# 找窗口：优先用刚启动那个进程，其次找同名进程里标题匹配的
$h = [IntPtr]::Zero
$cands = @()
if ($launched -ne $null) { $cands += $launched }
$cands += @(Get-Process -Name $Proc -ErrorAction SilentlyContinue)
foreach ($p in $cands) {
    $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero -and $p.MainWindowTitle -eq $Title) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) {
    foreach ($p in $cands) { $p.Refresh(); if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $h = $p.MainWindowHandle; break } }
}
if ($h -eq [IntPtr]::Zero) {
    $living = @(Get-Process -Name $Proc -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    throw ("找不到窗口。相关进程：[" + ($living -join ',') + "]（进程在但没窗口 => 可能还在启动）")
}

$r = New-Object W.Api+RECT
[void][W.Api]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
if ($w -le 0 -or $ht -le 0) { throw "窗口尺寸不对：$w x $ht" }

$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [W.Api]::PrintWindow($h, $hdc, 2)      # 2 = PW_RENDERFULLCONTENT
if (-not $ok) { $ok = [W.Api]::PrintWindow($h, $hdc, 0) }
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("PrintWindow=" + $ok + "  " + $w + "x" + $ht + "  -> " + $Out)
