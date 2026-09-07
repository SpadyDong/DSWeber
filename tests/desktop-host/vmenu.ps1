Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class W5 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);
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
            var sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            list.Add(sb.ToString() + "|" + r.Left + "|" + r.Top + "|" + (r.Right - r.Left) + "|" + (r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

function Get-Windows($target) {
    $out = New-Object System.Collections.ArrayList
    foreach ($row in [W5]::Scan($target)) {
        $p = $row -split '\|'
        [void]$out.Add([PSCustomObject]@{
            Class  = $p[0]
            Left   = [int]$p[1]
            Top    = [int]$p[2]
            Width  = [int]$p[3]
            Height = [int]$p[4]
        })
    }
    return $out.ToArray()
}

function Find-Menu($target) {
    foreach ($w in Get-Windows $target) {
        if ($w.Class -notlike "WindowsForms10.Window.*") { continue }
        if ($w.Width -eq 44 -and $w.Height -eq 44) { continue }
        if ($w.Width -eq 248) { continue }
        return $w
    }
    return $null
}

function Find-Ball($target) {
    foreach ($w in Get-Windows $target) {
        if ($w.Width -eq 44 -and $w.Height -eq 44) { return $w }
    }
    return $null
}

$proc = Get-Process DshTray -ErrorAction Stop
$target = [uint32]$proc.Id
Write-Host "DshTray PID = $target"

$origin = [System.Windows.Forms.Cursor]::Position

$ball = Find-Ball $target
if ($null -eq $ball) { Write-Host "FAIL: ball not found"; exit 1 }
Write-Host "Ball at ($($ball.Left),$($ball.Top))"

$pass = $true

# ---------- CASE 1: right click opens menu ----------
Write-Host ""
Write-Host "== CASE 1: right click opens the menu =="
[W5]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
Start-Sleep -Milliseconds 400
[W5]::mouse_event([W5]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[W5]::mouse_event([W5]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 900

$menu = Find-Menu $target
if ($null -eq $menu) {
    Write-Host "  FAIL: menu did not appear"
    Write-Host "  windows seen right now:"
    foreach ($w in Get-Windows $target) {
        Write-Host ("    class={0} rect=({1},{2}) {3}x{4}" -f $w.Class,$w.Left,$w.Top,$w.Width,$w.Height)
    }
    $pass = $false
} else {
    Write-Host "  menu visible: class=$($menu.Class) at ($($menu.Left),$($menu.Top)) size $($menu.Width)x$($menu.Height)"
    $fg = [W5]::GetForegroundWindow()
    Write-Host "  foreground hwnd = $fg"
}

# ---------- CASE 2: left click on empty area closes it ----------
Write-Host ""
Write-Host "== CASE 2: left click on empty desktop area closes the menu =="

$scr = [System.Windows.Forms.Screen]::FromPoint([System.Drawing.Point]::new($ball.Left, $ball.Top))
$blankX = $scr.WorkingArea.Left + 400
$blankY = $scr.WorkingArea.Top + 300
Write-Host "  clicking blank spot at ($blankX,$blankY)"

[W5]::SetCursorPos($blankX, $blankY) | Out-Null
Start-Sleep -Milliseconds 400
[W5]::mouse_event([W5]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[W5]::mouse_event([W5]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 800

$menu2 = Find-Menu $target
if ($null -eq $menu2) {
    Write-Host "  OK: menu closed"
} else {
    Write-Host "  FAIL: menu STILL OPEN at ($($menu2.Left),$($menu2.Top))"
    $pass = $false
    # force close so the desktop is not left in a weird state
    [W5]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
    [W5]::mouse_event([W5]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
    [W5]::mouse_event([W5]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}

# ---------- CASE 3: menu reopens after being dismissed ----------
Write-Host ""
Write-Host "== CASE 3: menu can be reopened after dismissal =="
[W5]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
Start-Sleep -Milliseconds 400
[W5]::mouse_event([W5]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[W5]::mouse_event([W5]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 900

$menu3 = Find-Menu $target
if ($null -eq $menu3) {
    Write-Host "  FAIL: menu did not reopen"
    $pass = $false
} else {
    Write-Host "  OK: menu reopened at ($($menu3.Left),$($menu3.Top))"
}

# dismiss again via Escape so we leave things clean
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 500
$menu4 = Find-Menu $target
if ($null -eq $menu4) { Write-Host "  OK: ESC also dismisses" }
else { Write-Host "  NOTE: still open after ESC" }

# ---------- CASE 4: left click on ball still shows balance ----------
Write-Host ""
Write-Host "== CASE 4: left click on ball still opens balance popup =="
[W5]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
Start-Sleep -Milliseconds 400
[W5]::mouse_event([W5]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[W5]::mouse_event([W5]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)

$popup = $null
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.ElapsedMilliseconds -lt 4000) {
    Start-Sleep -Milliseconds 50
    foreach ($w in Get-Windows $target) {
        if ($w.Width -eq 248) { $popup = $w; break }
    }
    if ($null -ne $popup) { break }
}
if ($null -eq $popup) {
    Write-Host "  FAIL: balance popup did not appear"
    $pass = $false
} else {
    Write-Host "  OK: balance popup at ($($popup.Left),$($popup.Top)) size $($popup.Width)x$($popup.Height)"
}

Start-Sleep -Milliseconds 300
$ballEnd = Find-Ball $target
Write-Host ""
Write-Host "Ball final position ($($ballEnd.Left),$($ballEnd.Top))  [start ($($ball.Left),$($ball.Top))]"
if ($ballEnd.Left -ne $ball.Left -or $ballEnd.Top -ne $ball.Top) {
    Write-Host "  FAIL: ball moved unexpectedly"
    $pass = $false
}

Write-Host ""
if ($pass) { Write-Host "RESULT: PASS" } else { Write-Host "RESULT: FAILED" }

[W5]::SetCursorPos($origin.X, $origin.Y) | Out-Null
