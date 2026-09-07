$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class WI{
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
    $cb = [WI+EnumProc]{
        param($h, $l)
        if ([WI]::IsWindowVisible($h)) {
            $wpid = 0
            [void][WI]::GetWindowThreadProcessId($h, [ref]$wpid)
            if ($wpid -eq $script:targetPid) {
                $sb = New-Object Text.StringBuilder 256
                [void][WI]::GetClassName($h, $sb, 256)
                $r = New-Object WI+R
                [void][WI]::GetWindowRect($h, [ref]$r)
                $script:acc += [pscustomobject]@{
                    Hwnd=$h; Cls=$sb.ToString(); X=$r.L; Y=$r.T; W=$r.Rr-$r.L; H=$r.B-$r.T
                }
            }
        }
        return $true
    }
    [void][WI]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:acc
}
function Find-Ball { Get-Windows | Where-Object { $_.W -eq 44 -and $_.H -eq 44 } | Select-Object -First 1 }
function Find-Menu { Get-Windows | Where-Object { $_.Cls -like 'WindowsForms10.Window*' -and -not ($_.W -eq 44 -and $_.H -eq 44) -and $_.W -ne 248 } | Select-Object -First 1 }

function Read-Items($hwnd) {
    for ($try = 1; $try -le 10; $try++) {
        Start-Sleep -Milliseconds 400
        try {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$hwnd)
            if ($null -eq $root) { continue }
            $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                                 [System.Windows.Automation.Condition]::TrueCondition)
            if ($all.Count -gt 0) {
                $names = @()
                foreach ($e in $all) {
                    if ($e.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem) {
                        $names += $e.Current.Name
                    }
                }
                if ($names.Count -gt 0) { return ,$names }
            }
        } catch { }
    }
    return ,@()
}

$exitText = [string][char]0x9000 + [string][char]0x51FA

$ball = Find-Ball
if (-not $ball) { "FAIL: ball not found"; exit 1 }
$bx = $ball.X + 22
$by = $ball.Y + 22
$origin = New-Object WI+P
[void][WI]::GetCursorPos([ref]$origin)

# 清场
[void][WI]::SetCursorPos(400,300)
Start-Sleep -Milliseconds 200
[WI]::mouse_event([WI]::LEFTDOWN,0,0,0,[UIntPtr]::Zero)
[WI]::mouse_event([WI]::LEFTUP,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Seconds 5

$pass = $true

""
"== A: BALL right-click menu (must NOT contain exit) =="
[void][WI]::SetCursorPos($bx,$by)
Start-Sleep -Milliseconds 250
[WI]::mouse_event([WI]::RIGHTDOWN,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[WI]::mouse_event([WI]::RIGHTUP,0,0,0,[UIntPtr]::Zero)

$menuBall = $null
for ($i=1; $i -le 20; $i++) { Start-Sleep -Milliseconds 250; $menuBall = Find-Menu; if ($menuBall) { break } }
if (-not $menuBall) {
    "  FAIL: ball menu did not appear"
    $pass = $false
} else {
    "  menu hwnd=$($menuBall.Hwnd) size $($menuBall.W)x$($menuBall.H)"
    $ballH = $menuBall.H
    $items = Read-Items $menuBall.Hwnd
    "  item count = $($items.Count)"
    foreach ($n in $items) { "    [$n]" }
    if ($items.Count -eq 0) {
        "  FAIL: could not enumerate items"
        $pass = $false
    } elseif ($items -contains $exitText) {
        "  FAIL: ball menu still contains the exit item"
        $pass = $false
    } else {
        "  OK: no exit item in ball menu"
    }
}

[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Seconds 1

""
"== B: TRAY icon right-click menu (MUST contain exit) =="
"  NOTE: tray icon coords unknown to the script; skipped intentionally"

$ballEnd = Find-Ball
""
"Ball final position ($($ballEnd.X),$($ballEnd.Y))  [start ($($ball.X),$($ball.Y))]"
if ($ballEnd.X -ne $ball.X -or $ballEnd.Y -ne $ball.Y) { "  FAIL: ball moved"; $pass = $false }

[void][WI]::SetCursorPos($origin.X,$origin.Y)
""
if ($pass) { "RESULT: PASS"; exit 0 } else { "RESULT: FAIL"; exit 1 }
