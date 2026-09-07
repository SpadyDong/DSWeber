Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class W11 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);

    public struct RECT { public int Left, Top, Right, Bottom; }
    public const uint RIGHTDOWN = 0x0008;
    public const uint RIGHTUP   = 0x0010;

    public static System.Collections.Generic.List<string> Scan(uint target) {
        var list = new System.Collections.Generic.List<string>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            if (!IsWindowVisible(h)) return true;
            RECT r;
            if (!GetWindowRect(h, out r)) return true;
            var sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            list.Add(h.ToInt64() + "|" + sb.ToString() + "|" + r.Left + "|" + r.Top + "|" + (r.Right - r.Left) + "|" + (r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

function Show-All($target, $tag) {
    Write-Host ""
    Write-Host "---- $tag ----"
    foreach ($row in [W11]::Scan($target)) {
        $p = $row -split '\|'
        Write-Host ("  hwnd={0} class={1} rect=({2},{3}) {4}x{5}" -f $p[0], $p[1], $p[2], $p[3], $p[4], $p[5])
    }
}

$proc = Get-Process DshTray -ErrorAction Stop
$target = [uint32]$proc.Id
Write-Host "DshTray PID = $target"

$origin = [System.Windows.Forms.Cursor]::Position

Show-All $target "BEFORE right-click"

$ball = $null
foreach ($row in [W11]::Scan($target)) {
    $p = $row -split '\|'
    if ([int]$p[4] -eq 44 -and [int]$p[5] -eq 44) {
        $ball = [PSCustomObject]@{ Left = [int]$p[2]; Top = [int]$p[3] }
    }
}
if ($null -eq $ball) { Write-Host "ball not found"; exit 1 }
Write-Host ""
Write-Host ("ball at ({0},{1})" -f $ball.Left, $ball.Top)

[W11]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
Start-Sleep -Milliseconds 500
[W11]::mouse_event([W11]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 100
[W11]::mouse_event([W11]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 800

Show-All $target "AFTER right-click"

[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 400
[W11]::SetCursorPos($origin.X, $origin.Y) | Out-Null
