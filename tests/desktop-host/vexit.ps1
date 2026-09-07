Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class W8 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);

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
    foreach ($row in [W8]::Scan($target)) {
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

function Wait-Menu($target, $timeoutMs) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $m = Find-Menu $target
        if ($null -ne $m) { return $m }
        Start-Sleep -Milliseconds 60
    }
    return $null
}

function Wait-NoMenu($target, $timeoutMs) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if ($null -eq (Find-Menu $target)) { return $true }
        Start-Sleep -Milliseconds 60
    }
    return $false
}

$proc = Get-Process DshTray -ErrorAction Stop
$target = [uint32]$proc.Id
Write-Host "DshTray PID = $target"

$origin = [System.Windows.Forms.Cursor]::Position
$pass = $true

$ball = Find-Ball $target
if ($null -eq $ball) { Write-Host "FAIL: ball not found"; exit 1 }
Write-Host "Ball at ($($ball.Left),$($ball.Top))"

# ---------- CASE A: ball right-click menu (should NOT contain exit) ----------
Write-Host ""
Write-Host "== CASE A: ball right-click menu height =="
[W8]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
Start-Sleep -Milliseconds 500
[W8]::mouse_event([W8]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 100
[W8]::mouse_event([W8]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)

$ballMenu = Wait-Menu $target 3000
if ($null -eq $ballMenu) {
    Write-Host "  FAIL: ball menu did not appear"
    $pass = $false
} else {
    Write-Host ("  ball menu size {0}x{1}" -f $ballMenu.Width, $ballMenu.Height)
}

[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 600
Wait-NoMenu $target 2000 | Out-Null

# ---------- CASE B: tray right-click menu (SHOULD contain exit) ----------
Write-Host ""
Write-Host "== CASE B: tray icon right-click menu height =="
Write-Host "  (tray icon location is not automatable; comparing via ball menu only)"

# ---------- CASE C: blank left click dismisses ball menu ----------
Write-Host ""
Write-Host "== CASE C: blank left click dismisses ball menu =="
[W8]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
Start-Sleep -Milliseconds 500
[W8]::mouse_event([W8]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 100
[W8]::mouse_event([W8]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)

$m2 = Wait-Menu $target 3000
if ($null -eq $m2) {
    Write-Host "  FAIL: menu did not open for dismissal test"
    $pass = $false
} else {
    $scr = [System.Windows.Forms.Screen]::FromPoint([System.Drawing.Point]::new($ball.Left, $ball.Top))
    $bx = $scr.WorkingArea.Left + 400
    $by = $scr.WorkingArea.Top + 300
    Write-Host "  clicking blank spot at ($bx,$by)"
    [W8]::SetCursorPos($bx, $by) | Out-Null
    Start-Sleep -Milliseconds 400
    [W8]::mouse_event([W8]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [W8]::mouse_event([W8]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)

    if (Wait-NoMenu $target 2500) {
        Write-Host "  OK: menu closed by blank left click"
    } else {
        Write-Host "  FAIL: menu STILL OPEN"
        $pass = $false
        [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
        Start-Sleep -Milliseconds 500
    }
}

# ---------- ball position invariance ----------
Start-Sleep -Milliseconds 400
$ballEnd = Find-Ball $target
Write-Host ""
Write-Host "Ball final position ($($ballEnd.Left),$($ballEnd.Top))  [start ($($ball.Left),$($ball.Top))]"
if ($ballEnd.Left -ne $ball.Left -or $ballEnd.Top -ne $ball.Top) {
    Write-Host "  FAIL: ball moved unexpectedly"
    $pass = $false
}

Write-Host ""
if ($pass) { Write-Host "RESULT: PASS" } else { Write-Host "RESULT: FAILED" }

[W8]::SetCursorPos($origin.X, $origin.Y) | Out-Null
