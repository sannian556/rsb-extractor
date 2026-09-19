param(
  [int]$TargetPid = 0,
  [string]$ButtonText = '',
  [string]$Shot = '',
  [int]$WaitMs = 1500,
  [switch]$Foreground,
  [switch]$NoClick
)
# 按文本找按钮并用 PostMessage 发 BM_CLICK（不能用 SendMessage：模态对话框会把自己挂死）
# -Foreground：操作当前前台窗口（用来处理独立弹出的 MessageBox）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class UiClick {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    static EnumProc keep1, keep2;
    public static List<long> TopWindows(uint pid) {
        var list = new List<long>();
        EnumProc cb = delegate(IntPtr h, IntPtr p) {
            uint wp; GetWindowThreadProcessId(h, out wp);
            if (wp != pid) return true;
            var sb = new StringBuilder(256); GetClassNameW(h, sb, 256);
            string c = sb.ToString();
            if (c.IndexOf("tooltips_class32") >= 0) return true;
            if (c.IndexOf("GDI+ Hook") >= 0) return true;
            if (c.IndexOf("BroadcastEventWindow") >= 0) return true;
            if (c == "IME" || c.IndexOf("MSCTFIME") >= 0) return true;
            list.Add(h.ToInt64());
            return true;
        };
        keep1 = cb; EnumWindows(cb, IntPtr.Zero);
        return list;
    }
    public static long Best(uint pid) {
        long best = 0; int bestArea = 0;
        foreach (long l in TopWindows(pid)) {
            RECT r; GetWindowRect(new IntPtr(l), out r);
            int a = (r.Right - r.Left) * (r.Bottom - r.Top);
            if (a > bestArea) { bestArea = a; best = l; }
        }
        return best;
    }
    static string Txt(IntPtr h) { var sb = new StringBuilder(512); GetWindowTextW(h, sb, 512); return sb.ToString(); }
    public static List<long> FindButtons(IntPtr root, string text) {
        var found = new List<long>();
        EnumProc cb = delegate(IntPtr h, IntPtr p) {
            string t = Txt(h);
            if (t.Length > 0 && t.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) found.Add(h.ToInt64());
            return true;
        };
        keep2 = cb; EnumChildWindows(root, cb, IntPtr.Zero);
        return found;
    }
    public static string DumpTexts(IntPtr root) {
        var sb = new StringBuilder();
        EnumProc cb = delegate(IntPtr h, IntPtr p) {
            string t = Txt(h);
            if (t.Length > 0) sb.AppendLine(t);
            return true;
        };
        keep2 = cb; EnumChildWindows(root, cb, IntPtr.Zero);
        return sb.ToString();
    }
}
'@
[UiClick]::SetProcessDPIAware() | Out-Null

if ($Foreground) {
    $hwnd = [UiClick]::GetForegroundWindow().ToInt64()
    Write-Output ('foreground hwnd = ' + $hwnd)
} else {
    if ($TargetPid -eq 0) { Write-Output 'NEED_TARGET_PID'; exit 1 }
    foreach ($l in [UiClick]::TopWindows([uint32]$TargetPid)) { [UiClick]::ShowWindow([IntPtr]$l, 9) | Out-Null }
    Start-Sleep -Milliseconds 700
    $hwnd = [UiClick]::Best([uint32]$TargetPid)
}
if ($hwnd -eq 0) { Write-Output 'WINDOW_NOT_FOUND'; exit 1 }

Write-Output '--- 目标窗口上的文字 ---'
Write-Output ([UiClick]::DumpTexts([IntPtr]$hwnd))

if (-not $NoClick -and $ButtonText -ne '') {
    $btns = [UiClick]::FindButtons([IntPtr]$hwnd, $ButtonText)
    Write-Output ("matched buttons for '" + $ButtonText + "': " + $btns.Count)
    if ($btns.Count -eq 0) { Write-Output 'BUTTON_NOT_FOUND'; exit 2 }
    [UiClick]::PostMessage([IntPtr]$btns[0], 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null   # BM_CLICK
    Write-Output 'BM_CLICK posted'
    Start-Sleep -Milliseconds $WaitMs
}

if ($Shot) {
    $r = New-Object UiClick+RECT
    [UiClick]::GetWindowRect([IntPtr]$hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { Write-Output 'BAD_RECT'; exit 3 }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [UiClick]::PrintWindow([IntPtr]$hwnd, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $dir = Split-Path -Parent $Shot
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ('saved ' + $Shot + '  (' + $w + 'x' + $h + ')')
}
