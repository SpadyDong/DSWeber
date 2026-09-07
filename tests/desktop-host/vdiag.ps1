Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class W6 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    public struct RECT { public int Left, Top, Right, Bottom; }
    public const uint LEFTDOWN  = 0x0002;
    public const uint LEFTUP    = 0x0004;
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
            var cn = new StringBuilder(256);
            GetClassName(h, cn, 256);
            var tt = new StringBuilder(256);
            GetWindowTextW(h, tt, 256);
            list.Add(h.ToInt64() + "|" + cn.ToString() + "|" + tt.ToString() + "|"
                     + r.Left + "|" + r.Top + "|" + (r.Right - r.Left) + "|" + (r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

$proc = Get-Process DshTray -ErrorAction Stop
$target = [uint32]$proc.Id
Write-Host "DshTray PID = $target"

function Dump($label) {
    Write-Host ""
    Write-Host "--- $label ---"
    $rows = [W6]::Scan($target)
    if ($rows.Count -eq 0) { Write-Host "  (no visible windows)" }
    foreach ($row in $rows) {
        $p = $row -split '\|'
        Write-Host ("  hwnd={0}  class={1}  text='{2}'  rect=({3},{4}) {5}x{6}" -f $p[0],$p[1],$p[2],$p[3],$p[4],$p[5],$p[6])
    }
    Write-Host ("  foreground hwnd = {0}" -f [W6]::GetForegroundWindow().ToInt64())
}

Dump "baseline"

$ballLeft = 0; $ballTop = 0
foreach ($row in [W6]::Scan($target)) {
    $p = $row -split '\|'
    if ([int]$p[5] -eq 44 -and [int]$p[6] -eq 44) { $ballLeft = [int]$p[3]; $ballTop = [int]$p[4] }
}
Write-Host ""
Write-Host "Ball at ($ballLeft,$ballTop)"

[W6]::SetCursorPos(($ballLeft + 22), ($ballTop + 22)) | Out-Null
Start-Sleep -Milliseconds 250
[W6]::mouse_event([W6]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[W6]::mouse_event([W6]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 900

Dump "after right click"

$scr = [System.Windows.Forms.Screen]::FromPoint([System.Drawing.Point]::new($ballLeft, $ballTop))
$bx = $scr.WorkingArea.Left + 400
$by = $scr.WorkingArea.Top + 300
Write-Host ""
Write-Host "clicking blank at ($bx,$by)"
[W6]::SetCursorPos($bx, $by) | Out-Null
Start-Sleep -Milliseconds 250
[W6]::mouse_event([W6]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[W6]::mouse_event([W6]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 900

Dump "after blank left click"

[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 600
Dump "after ESC"