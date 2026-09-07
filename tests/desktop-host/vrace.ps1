$ErrorActionPreference = 'Stop'

Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class WR{
 public delegate bool EnumProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc cb,IntPtr l);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")]public static extern int GetWindowThreadProcessId(IntPtr h,out int pid);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out R r);
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]public static extern bool GetCursorPos(out P p);
 [DllImport("user32.dll")]public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 public struct R{public int L,T,Rr,B;}
 public struct P{public int X,Y;}
 public const uint LEFTDOWN=0x0002,LEFTUP=0x0004,RIGHTDOWN=0x0008,RIGHTUP=0x0010;
}
"@

$proc = Get-Process DshTray -ErrorAction SilentlyContinue
if (-not $proc) { "FAIL: DshTray not running"; exit 1 }
$targetPid = $proc.Id
"DshTray PID = $targetPid"

function Get-Windows {
    $script:acc = @()
    $cb = [WR+EnumProc]{
        param($h, $l)
        if ([WR]::IsWindowVisible($h)) {
            $wpid = 0
            [void][WR]::GetWindowThreadProcessId($h, [ref]$wpid)
            if ($wpid -eq $script:targetPid) {
                $sb = New-Object Text.StringBuilder 256
                [void][WR]::GetClassName($h, $sb, 256)
                $r = New-Object WR+R
                [void][WR]::GetWindowRect($h, [ref]$r)
                $script:acc += [pscustomobject]@{
                    Hwnd  = $h
                    Cls   = $sb.ToString()
                    X     = $r.L
                    Y     = $r.T
                    W     = $r.Rr - $r.L
                    H     = $r.B - $r.T
                }
            }
        }
        return $true
    }
    [void][WR]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:acc
}

function Find-Ball   { Get-Windows | Where-Object { $_.W -eq 44 -and $_.H -eq 44 } | Select-Object -First 1 }
function Find-Popup  { Get-Windows | Where-Object { $_.W -eq 248 } | Select-Object -First 1 }
function Find-Menu   { Get-Windows | Where-Object { $_.Cls -like 'WindowsForms10.Window*' -and -not ($_.W -eq 44 -and $_.H -eq 44) -and $_.W -ne 248 } | Select-Object -First 1 }

$ball = Find-Ball
if (-not $ball) { "FAIL: ball not found"; exit 1 }
$bx = $ball.X + 22
$by = $ball.Y + 22
"Ball center = ($bx,$by)"

$origin = New-Object WR+P
[void][WR]::GetCursorPos([ref]$origin)

$fail = $false

# 先清场：ESC 关掉可能残留的菜单，等浮层自动关闭
[void][WR]::SetCursorPos(400, 300)
Start-Sleep -Milliseconds 200
[WR]::mouse_event([WR]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WR]::mouse_event([WR]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Seconds 4

""
"== CASE R1: left click ball, then right click BEFORE balance returns =="

[void][WR]::SetCursorPos($bx, $by)
Start-Sleep -Milliseconds 150
[WR]::mouse_event([WR]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WR]::mouse_event([WR]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)

# 等浮层出现（loading 态）
$loading = $null
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 100
    $loading = Find-Popup
    if ($loading) { break }
}
if ($loading) {
    "  loading popup shown at ($($loading.X),$($loading.Y)) size $($loading.W)x$($loading.H)"
} else {
    "  WARN: loading popup never appeared (cached result may have returned instantly)"
}

# 立刻右键——此时异步查询大概率仍在飞
[void][WR]::SetCursorPos($bx, $by)
Start-Sleep -Milliseconds 80
[WR]::mouse_event([WR]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 50

$popupAfterDown = Find-Popup
if ($popupAfterDown) {
    "  FAIL: popup still visible after RIGHTDOWN (should hide on mouse-down)"
    $fail = $true
} else {
    "  OK: popup hidden immediately on RIGHTDOWN"
}

[WR]::mouse_event([WR]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)

$menu = $null
for ($i = 1; $i -le 20; $i++) {
    Start-Sleep -Milliseconds 250
    $menu = Find-Menu
    if ($menu) { break }
}
if ($menu) {
    "  OK: context menu opened at ($($menu.X),$($menu.Y)) size $($menu.W)x$($menu.H)"
} else {
    "  FAIL: context menu did not open within 5s"
    $fail = $true
}

""
"== CASE R2: watch 12s - stale balance callback must NOT resurrect the popup =="
$resurrect = $false
for ($i = 1; $i -le 24; $i++) {
    Start-Sleep -Milliseconds 500
    $p = Find-Popup
    if ($p) {
        "  FAIL at t=$([math]::Round($i*0.5,1))s: popup reappeared at ($($p.X),$($p.Y))"
        $resurrect = $true
        break
    }
}
if ($resurrect) {
    $fail = $true
} else {
    "  OK: popup stayed hidden for the full 12s window"
}

$menuStill = Find-Menu
if ($menuStill) {
    "  OK: menu still present and not covered"
} else {
    "  NOTE: menu no longer present (may have been dismissed)"
}

# 收尾：关掉菜单
[void][WR]::SetCursorPos(400, 300)
Start-Sleep -Milliseconds 150
[WR]::mouse_event([WR]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WR]::mouse_event([WR]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 400

""
"== CASE R3: after the race, left click still shows balance normally =="
[void][WR]::SetCursorPos($bx, $by)
Start-Sleep -Milliseconds 150
[WR]::mouse_event([WR]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WR]::mouse_event([WR]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)

$ok3 = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 200
    $p = Find-Popup
    if ($p) {
        "  OK: balance popup at ($($p.X),$($p.Y)) size $($p.W)x$($p.H)"
        $ok3 = $true
        break
    }
}
if (-not $ok3) {
    "  FAIL: balance popup did not show after the race"
    $fail = $true
}

$ballEnd = Find-Ball
if ($ballEnd) { "" ; "Ball final position ($($ballEnd.X),$($ballEnd.Y))  [start ($($ball.X),$($ball.Y))]" }
if ($ballEnd -and ($ballEnd.X -ne $ball.X -or $ballEnd.Y -ne $ball.Y)) {
    "  FAIL: ball moved during the test"
    $fail = $true
}

[void][WR]::SetCursorPos($origin.X, $origin.Y)

""
if ($fail) { "RESULT: FAIL"; exit 1 } else { "RESULT: PASS"; exit 0 }
