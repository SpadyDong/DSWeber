$ErrorActionPreference = 'Continue'

Add-Type -Namespace WR3 -Name Api -MemberDefinition @'
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
public struct RECT { public int L, T, R, B; }
public struct POINT { public int X, Y; }
public const uint RIGHTDOWN = 0x0008;
public const uint RIGHTUP   = 0x0010;
public const uint LEFTDOWN  = 0x0002;
public const uint LEFTUP    = 0x0004;
public const uint OBJID_CLIENT = 0xFFFFFFFC;
'@

function Get-Windows {
    $tp = (Get-Process -Name DshTray -ErrorAction SilentlyContinue).Id
    $list = New-Object System.Collections.ArrayList
    $cb = [WR3.Api+EnumWindowsProc]{
        param($h, $p)
        if ([WR3.Api]::IsWindowVisible($h)) {
            $o = 0
            [void][WR3.Api]::GetWindowThreadProcessId($h, [ref]$o)
            if ($o -eq $tp) {
                $sb = New-Object System.Text.StringBuilder 256
                [void][WR3.Api]::GetClassName($h, $sb, 256)
                $r = New-Object WR3.Api+RECT
                [void][WR3.Api]::GetWindowRect($h, [ref]$r)
                [void]$list.Add([pscustomobject]@{
                    H = $h; Cls = $sb.ToString()
                    X = $r.L; Y = $r.T; W = $r.R - $r.L; Hh = $r.B - $r.T
                })
            }
        }
        return $true
    }
    [void][WR3.Api]::EnumWindows($cb, [IntPtr]::Zero)
    return $list
}
function Find-Ball { Get-Windows | Where-Object { $_.W -eq 44 -and $_.Hh -eq 44 } | Select-Object -First 1 }
function Find-Menu {
    Get-Windows | Where-Object {
        $_.Cls -like 'WindowsForms10*' -and -not ($_.W -eq 44 -and $_.Hh -eq 44) -and $_.W -ne 248
    } | Select-Object -First 1
}
function Show-Ball($tag) {
    $b = Find-Ball
    if ($b) { Write-Host ("  [ball@{0}] ({1},{2})" -f $tag, $b.X, $b.Y) }
    else { Write-Host ("  [ball@{0}] NOT FOUND" -f $tag) }
}

$origin = New-Object WR3.Api+POINT
[void][WR3.Api]::GetCursorPos([ref]$origin)
Write-Host ("cursor origin = ({0},{1})" -f $origin.X, $origin.Y)

Show-Ball 'script-start'

$ball = Find-Ball
if (-not $ball) { Write-Host 'no ball'; exit 1 }

Write-Host 'clearing state...'
[void][WR3.Api]::SetCursorPos(300, 300)
Start-Sleep -Milliseconds 150
[WR3.Api]::mouse_event([WR3.Api]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WR3.Api]::mouse_event([WR3.Api]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Seconds 5
Show-Ball 'after-clear'

$ball = Find-Ball
[void][WR3.Api]::SetCursorPos(($ball.X + 22), ($ball.Y + 22))
Start-Sleep -Milliseconds 200
Show-Ball 'after-cursor-move'

[WR3.Api]::mouse_event([WR3.Api]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WR3.Api]::mouse_event([WR3.Api]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)

$menu = $null
for ($i = 1; $i -le 20; $i++) {
    Start-Sleep -Milliseconds 250
    $menu = Find-Menu
    if ($menu) { break }
}
if (-not $menu) { Write-Host 'no menu'; [void][WR3.Api]::SetCursorPos($origin.X, $origin.Y); exit 1 }
Write-Host ("menu hwnd={0} {1}x{2}" -f $menu.H, $menu.W, $menu.Hh)
Show-Ball 'after-menu-open'

Start-Sleep -Milliseconds 800

Write-Host ''
Write-Host '--- MSAA inline (same shape as working probe) ---'
$iid = [System.Guid]'618736e0-3c3d-11cf-810c-00aa00389b71'
$acc = $null
$hr = [WR3.Api]::AccessibleObjectFromWindow([IntPtr]$menu.H, [WR3.Api]::OBJID_CLIENT, [ref]$iid, [ref]$acc)
Write-Host ("hr=0x{0:X8}  accIsNull={1}" -f $hr, ($acc -eq $null))

if ($acc -ne $null) {
    $t = $acc.GetType()
    Write-Host ("acc type = " + $t.FullName)

    $cnt = -1
    try {
        $cnt = $t.InvokeMember('accChildCount', 'GetProperty', $null, $acc, $null)
    } catch {
        Write-Host ("accChildCount THREW: " + $_.Exception.GetType().FullName + " :: " + $_.Exception.Message)
        if ($_.Exception.InnerException) {
            Write-Host ("  inner: " + $_.Exception.InnerException.GetType().FullName + " :: " + $_.Exception.InnerException.Message)
        }
    }
    Write-Host ("accChildCount = " + $cnt)

    if ($cnt -gt 0) {
        for ($i = 1; $i -le $cnt; $i++) {
            $nm = '<throw>'
            $rl = '<throw>'
            try { $nm = $t.InvokeMember('accName', 'GetProperty', $null, $acc, @([object]$i)) } catch { $nm = '<ERR:' + $_.Exception.Message + '>' }
            try { $rl = $t.InvokeMember('accRole', 'GetProperty', $null, $acc, @([object]$i)) } catch { $rl = '<ERR>' }
            Write-Host ("  child[{0}] role={1} name='{2}'" -f $i, $rl, $nm)
        }
    }
}

Write-Host ''
Write-Host '--- same call wrapped in a function (the failing shape) ---'
function Get-Acc2($hwnd) {
    $g = [System.Guid]'618736e0-3c3d-11cf-810c-00aa00389b71'
    $a = $null
    $h2 = [WR3.Api]::AccessibleObjectFromWindow([IntPtr]$hwnd, [WR3.Api]::OBJID_CLIENT, [ref]$g, [ref]$a)
    Write-Host ("  fn hr=0x{0:X8} isNull={1}" -f $h2, ($a -eq $null))
    if ($h2 -ne 0) { return $null }
    return $a
}
$acc2 = Get-Acc2 $menu.H
Write-Host ("  returned isNull=" + ($acc2 -eq $null))
if ($acc2 -ne $null) {
    Write-Host ("  returned type = " + $acc2.GetType().FullName)
    try {
        $c2 = $acc2.GetType().InvokeMember('accChildCount', 'GetProperty', $null, $acc2, $null)
        Write-Host ("  fn accChildCount = " + $c2)
    } catch {
        Write-Host ("  fn accChildCount THREW: " + $_.Exception.Message)
    }
}

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 500
Show-Ball 'after-esc'
[void][WR3.Api]::SetCursorPos($origin.X, $origin.Y)
Write-Host 'probe3 done'
