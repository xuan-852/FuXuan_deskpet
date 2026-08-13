# 枚举窗口找 QQ 主窗口并输出精确尺寸
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WinEnum2 {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int maxCount);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$callback = {
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder 256
    [WinEnum2]::GetWindowText($h, $sb, 256) | Out-Null
    $t = $sb.ToString()
    if ($t.Length -gt 0 -and [WinEnum2]::IsWindowVisible($h)) {
        $rc = New-Object WinEnum2+RECT
        [WinEnum2]::GetWindowRect($h, [ref]$rc) | Out-Null
        $w = $rc.Right - $rc.Left
        $hgt = $rc.Bottom - $rc.Top
        # 过滤明显是窗口的大小（宽>100 高>100）
        if ($w -gt 100 -and $hgt -gt 100) {
            Write-Host ("{0} | rect=({1},{2})-({3},{4}) | w={5} h={6}" -f $t, $rc.Left, $rc.Top, $rc.Right, $rc.Bottom, $w, $hgt)
        }
    }
    return $true
}

$delegate = [WinEnum2+EnumWindowsProc]$callback
[WinEnum2]::EnumWindows($delegate, [IntPtr]::Zero) | Out-Null
