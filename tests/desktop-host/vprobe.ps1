$ErrorActionPreference = 'Stop'

Add-Type -Namespace WP -Name Api -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
public delegate bool EnumWindowsProc(IntPtr h, IntPtr p);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
[DllImport("oleacc.dll")] public static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref System.Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
[DllImport("oleacc.dll")] public static extern int AccessibleChildren(object paccContainer, int iChildStart, int cChildren, [Out] object[] rgv, out int pcObtained);
public struct RECT { public int L, T, R, B; }
public struct POINT { public int X, Y; }
public const uint RIGHTDOWN = 0x0008;
public const uint RIGHTUP   = 0x0010;
public const uint LEFTDOWN  = 0x0002;
public const uint LEFTUP    = 0x0004;
'@

function Get-Windows {
    $pid1 = (Get-Process -Name DshTray -ErrorAction SilentlyContinue).Id
    $list = New-Object System.Collections.ArrayList
    $cb = [WP.Api+EnumWindowsProc]{
        param($h, $p)
        if ([WP.Api]::IsWindowVisible($h)) {
            $o = 0
            [void][WP.Api]::GetWindowThreadProcessId($h, [ref]$o)
            if ($o -eq $pid1) {
                $sb = New-Object System.Text.StringBuilder 256
                [void][WP.Api]::GetClassName($h, $sb, 256)
                $r = New-Object WP.Api+RECT
                [void][WP.Api]::GetWindowRect($h, [ref]$r)
                [void]$list.Add([pscustomobject]@{
                    H = $h; Cls = $sb.ToString()
                    X = $r.L; Y = $r.T; W = $r.R - $r.L; Hh = $r.B - $r.T
                })
            }
        }
        return $true
    }
    [void][WP.Api]::EnumWindows($cb, [IntPtr]::Zero)
    return $list
}

function Find-Ball { Get-Windows | Where-Object { $_.W -eq 44 -and $_.Hh -eq 44 } | Select-Object -First 1 }
function Find-Menu {
    Get-Windows | Where-Object {
        $_.Cls -like 'WindowsForms10*' -and -not ($_.W -eq 44 -and $_.Hh -eq 44) -and $_.W -ne 248
    } | Select-Object -First 1
}

$origin = New-Object WP.Api+POINT
[void][WP.Api]::GetCursorPos([ref]$origin)

$ball = Find-Ball
if (-not $ball) { Write-Host 'no ball'; exit 1 }

# clean state
[void][WP.Api]::SetCursorPos(300, 300)
Start-Sleep -Milliseconds 150
[WP.Api]::mouse_event([WP.Api]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WP.Api]::mouse_event([WP.Api]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Seconds 5

$cx = $ball.X + 22
$cy = $ball.Y + 22
[void][WP.Api]::SetCursorPos($cx, $cy)
Start-Sleep -Milliseconds 200
[WP.Api]::mouse_event([WP.Api]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WP.Api]::mouse_event([WP.Api]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)

$menu = $null
for ($i = 1; $i -le 20; $i++) {
    Start-Sleep -Milliseconds 250
    $menu = Find-Menu
    if ($menu) { break }
}
if (-not $menu) { Write-Host 'no menu'; [void][WP.Api]::SetCursorPos($origin.X, $origin.Y); exit 1 }
Write-Host ("menu hwnd={0} cls={1} {2}x{3}" -f $menu.H, $menu.Cls, $menu.W, $menu.Hh)

Start-Sleep -Milliseconds 1200

Write-Host ''
Write-Host '--- strategy 1: UIA FromHandle + RawViewWalker ---'
try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$menu.H)
    Write-Host ("  root: name='{0}' type={1}" -f $root.Current.Name, $root.Current.ControlType.ProgrammaticName)
    $w = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $c = $w.GetFirstChild($root)
    $n = 0
    while ($c -ne $null -and $n -lt 40) {
        Write-Host ("  child[{0}] name='{1}' type={2}" -f $n, $c.Current.Name, $c.Current.ControlType.ProgrammaticName)
        $c = $w.GetNextSibling($c); $n++
    }
    if ($n -eq 0) { Write-Host '  (no raw children)' }
} catch { Write-Host ("  ERR: " + $_.Exception.Message) }

Write-Host ''
Write-Host '--- strategy 2: UIA FindAll Descendants MenuItem ---'
try {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$menu.H)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    Write-Host ("  count = " + $all.Count)
    foreach ($e in $all) { Write-Host ("  * " + $e.Current.Name) }
} catch { Write-Host ("  ERR: " + $_.Exception.Message) }

Write-Host ''
Write-Host '--- strategy 3: MSAA AccessibleObjectFromWindow ---'
try {
    $iid = [System.Guid]'618736e0-3c3d-11cf-810c-00aa00389b71'
    $acc = $null
    $hr = [WP.Api]::AccessibleObjectFromWindow([IntPtr]$menu.H, 0xFFFFFFFC, [ref]$iid, [ref]$acc)
    Write-Host ("  hr=0x{0:X8} obj={1}" -f $hr, ($acc -ne $null))
    if ($acc -ne $null) {
        $t = $acc.GetType()
        $cnt = $t.InvokeMember('accChildCount', 'GetProperty', $null, $acc, $null)
        Write-Host ("  accChildCount = " + $cnt)
        $nm = $t.InvokeMember('accName', 'GetProperty', $null, $acc, @([object]0))
        Write-Host ("  accName(self) = '" + $nm + "'")
        if ($cnt -gt 0) {
            $buf = New-Object object[] $cnt
            $got = 0
            $hr2 = [WP.Api]::AccessibleChildren($acc, 0, $cnt, $buf, [ref]$got)
            Write-Host ("  AccessibleChildren hr=0x{0:X8} got={1}" -f $hr2, $got)
            for ($k = 0; $k -lt $got; $k++) {
                $ch = $buf[$k]
                if ($ch -is [int]) {
                    $cn = $t.InvokeMember('accName', 'GetProperty', $null, $acc, @([object]$ch))
                    Write-Host ("  [{0}] (varchild) '{1}'" -f $k, $cn)
                } else {
                    $ct = $ch.GetType()
                    $cn = $ct.InvokeMember('accName', 'GetProperty', $null, $ch, @([object]0))
                    $cc = $ct.InvokeMember('accChildCount', 'GetProperty', $null, $ch, $null)
                    Write-Host ("  [{0}] '{1}' childCount={2}" -f $k, $cn, $cc)
                    if ($cc -gt 0) {
                        $b2 = New-Object object[] $cc
                        $g2 = 0
                        [void][WP.Api]::AccessibleChildren($ch, 0, $cc, $b2, [ref]$g2)
                        for ($m = 0; $m -lt $g2; $m++) {
                            $gc = $b2[$m]
                            if ($gc -is [int]) {
                                $gn = $ct.InvokeMember('accName', 'GetProperty', $null, $ch, @([object]$gc))
                                Write-Host ("      - (var {0}) '{1}'" -f $gc, $gn)
                            } else {
                                $gt = $gc.GetType()
                                $gn = $gt.InvokeMember('accName', 'GetProperty', $null, $gc, @([object]0))
                                Write-Host ("      - '{0}'" -f $gn)
                            }
                        }
                    }
                }
            }
        }
    }
} catch { Write-Host ("  ERR: " + $_.Exception.Message) }

# teardown
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 400
[void][WP.Api]::SetCursorPos($origin.X, $origin.Y)
Write-Host ''
Write-Host 'probe done'
