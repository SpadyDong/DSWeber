Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class W7 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);

    public struct RECT { public int Left, Top, Right, Bottom; }
    public struct POINT { public int X, Y; }
    public const uint LEFTDOWN  = 0x0002;
    public const uint LEFTUP    = 0x0004;
    public const uint RIGHTDOWN = 0x0008;
    public const uint RIGHTUP   = 0x0010;

    public static System.Collections.Generic.List<string> Scan(uint target, bool onlyVisible) {
        var list = new System.Collections.Generic.List<string>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            if (onlyVisible && !IsWindowVisible(h)) return true;
            RECT r;
            if (!GetWindowRect(h, out r)) return true;
            var sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            string vis = IsWindowVisible(h) ? "vis" : "hid";
            list.Add(h.ToInt64() + "|" + sb.ToString() + "|" + r.Left + "|" + r.Top + "|" + (r.Right - r.Left) + "|" + (r.Bottom - r.Top) + "|" + vis);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string HwndAt(int x, int y) {
        POINT p; p.X = x; p.Y = y;
        IntPtr h = WindowFromPoint(p);
        var sb = new StringBuilder(256);
        GetClassName(h, sb, 256);
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        return h.ToInt64() + "|" + sb.ToString() + "|pid=" + pid;
    }
}
"@

$proc = Get-Process DshTray -ErrorAction Stop
$target = [uint32]$proc.Id
Write-Host "DshTray PID = $target"

function Dump($label, $onlyVisible) {
    Write-Host ""
    Write-Host "--- $label ---"
    foreach ($row in [W7]::Scan($target, $onlyVisible)) {
        $p = $row -split '\|'
        Write-Host ("  hwnd={0} class={1} rect=({2},{3}) {4}x{5} {6}" -f $p[0],$p[1],$p[2],$p[3],$p[4],$p[5],$p[6])
    }
    Write-Host ("  foreground hwnd = {0}" -f [W7]::GetForegroundWindow().ToInt64())
}

# screen geometry
Write-Host ""
Write-Host "=== screens ==="
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    Write-Host ("  {0} primary={1} bounds={2} working={3}" -f $s.DeviceName, $s.Primary, $s.Bounds, $s.WorkingArea)
}

Dump "baseline (all windows incl hidden)" $false

# locate ball
$ball = $null
foreach ($row in [W7]::Scan($target, $true)) {
    $p = $row -split '\|'
    if ([int]$p[4] -eq 44 -and [int]$p[5] -eq 44) {
        $ball = [PSCustomObject]@{ Left=[int]$p[2]; Top=[int]$p[3] }
    }
}
if ($null -eq $ball) { Write-Host "no ball"; exit 1 }

$cx = $ball.Left + 22
$cy = $ball.Top + 22
Write-Host ""
Write-Host "Ball center = ($cx,$cy)"
Write-Host ("Window under that point BEFORE moving cursor: {0}" -f [W7]::HwndAt($cx, $cy))

[W7]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 400
Write-Host ("Cursor now at {0}" -f [System.Windows.Forms.Cursor]::Position)
Write-Host ("Window under cursor: {0}" -f [W7]::HwndAt($cx, $cy))

Write-Host ""
Write-Host "=== RIGHT CLICK ==="
[W7]::mouse_event([W7]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[W7]::mouse_event([W7]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 1200

Dump "after right click (all windows incl hidden)" $false

# dismiss
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 600

Write-Host ""
Write-Host "=== LEFT CLICK (control: does input reach the ball at all?) ==="
[W7]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 400
[W7]::mouse_event([W7]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[W7]::mouse_event([W7]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 1500

Dump "after left click" $true
