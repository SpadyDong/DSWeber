$ErrorActionPreference = 'Stop'

Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class WM2{
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

function Get-Windows {
    $script:acc = @()
    $cb = [WM2+EnumProc]{
        param($h, $l)
        if ([WM2]::IsWindowVisible($h)) {
            $wpid = 0
            [void][WM2]::GetWindowThreadProcessId($h, [ref]$wpid)
            if ($wpid -eq $script:targetPid) {
                $sb = New-Object Text.StringBuilder 256
                [void][WM2]::GetClassName($h, $sb, 256)
                $r = New-Object WM2+R
                [void][WM2]::GetWindowRect($h, [ref]$r)
                $script:acc += [pscustomobject]@{
                    Hwnd = $h; Cls = $sb.ToString()
                    X = $r.L; Y = $r.T; W = $r.Rr - $r.L; H = $r.B - $r.T
                }
            }
        }
        return $true
    }
    [void][WM2]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:acc
}
function Find-Ball  { Get-Windows | Where-Object { $_.W -eq 44 -and $_.H -eq 44 } | Select-Object -First 1 }
function Find-Popup { Get-Windows | Where-Object { $_.W -eq 248 } | Select-Object -First 1 }
function Find-Menu  { Get-Windows | Where-Object { $_.Cls -like 'WindowsForms10.Window*' -and -not ($_.W -eq 44 -and $_.H -eq 44) -and $_.W -ne 248 } | Select-Object -First 1 }

$ball = Find-Ball
$bx = $ball.X + 22
$by = $ball.Y + 22
$origin = New-Object WM2+P
[void][WM2]::GetCursorPos([ref]$origin)

# 清场
[void][WM2]::SetCursorPos(400, 300)
Start-Sleep -Milliseconds 200
[WM2]::mouse_event([WM2]::LEFTDOWN,0,0,0,[UIntPtr]::Zero)
[WM2]::mouse_event([WM2]::LEFTUP,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Seconds 4

"--- BASELINE: right click on a clean ball (no popup) ---"
[void][WM2]::SetCursorPos($bx, $by)
Start-Sleep -Milliseconds 150
[WM2]::mouse_event([WM2]::RIGHTDOWN,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[WM2]::mouse_event([WM2]::RIGHTUP,0,0,0,[UIntPtr]::Zero)
$m = $null
for ($i=1; $i -le 20; $i++) {
    Start-Sleep -Milliseconds 250
    $m = Find-Menu
    if ($m) { "  menu appeared at t=$([math]::Round($i*0.25,2))s  ($($m.X),$($m.Y)) $($m.W)x$($m.H)"; break }
}
if (-not $m) { "  menu NEVER appeared within 5s" }

# 关菜单
[void][WM2]::SetCursorPos(400,300)
Start-Sleep -Milliseconds 150
[WM2]::mouse_event([WM2]::LEFTDOWN,0,0,0,[UIntPtr]::Zero)
[WM2]::mouse_event([WM2]::LEFTUP,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Seconds 4

""
"--- RACE: popup visible, then right click ---"
[void][WM2]::SetCursorPos($bx, $by)
Start-Sleep -Milliseconds 150
[WM2]::mouse_event([WM2]::LEFTDOWN,0,0,0,[UIntPtr]::Zero)
[WM2]::mouse_event([WM2]::LEFTUP,0,0,0,[UIntPtr]::Zero)
for ($i=0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 100; if (Find-Popup) { break } }
$p = Find-Popup
if ($p) { "  popup up: $($p.W)x$($p.H)" } else { "  popup missing" }

[void][WM2]::SetCursorPos($bx, $by)
Start-Sleep -Milliseconds 80
[WM2]::mouse_event([WM2]::RIGHTDOWN,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[WM2]::mouse_event([WM2]::RIGHTUP,0,0,0,[UIntPtr]::Zero)

$m2 = $null
for ($i=1; $i -le 20; $i++) {
    Start-Sleep -Milliseconds 250
    $m2 = Find-Menu
    if ($m2) { "  menu appeared at t=$([math]::Round($i*0.25,2))s  ($($m2.X),$($m2.Y)) $($m2.W)x$($m2.H)"; break }
}
if (-not $m2) {
    "  menu NEVER appeared within 5s  <-- regression path"
    "  all visible windows of pid $targetPid :"
    Get-Windows | ForEach-Object { "    cls=$($_.Cls) $($_.W)x$($_.H) at ($($_.X),$($_.Y))" }
}

[void][WM2]::SetCursorPos(400,300)
Start-Sleep -Milliseconds 150
[WM2]::mouse_event([WM2]::LEFTDOWN,0,0,0,[UIntPtr]::Zero)
[WM2]::mouse_event([WM2]::LEFTUP,0,0,0,[UIntPtr]::Zero)
[void][WM2]::SetCursorPos($origin.X, $origin.Y)
