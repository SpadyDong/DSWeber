$ErrorActionPreference = 'Stop'

Add-Type -Namespace WQ -Name Api -MemberDefinition @'
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
public const uint OBJID_CLIENT = 0xFFFFFFFC;
'@

function Get-Windows {
    $tp = (Get-Process -Name DshTray -ErrorAction SilentlyContinue).Id
    $list = New-Object System.Collections.ArrayList
    $cb = [WQ.Api+EnumWindowsProc]{
        param($h, $p)
        if ([WQ.Api]::IsWindowVisible($h)) {
            $o = 0
            [void][WQ.Api]::GetWindowThreadProcessId($h, [ref]$o)
            if ($o -eq $tp) {
                $sb = New-Object System.Text.StringBuilder 256
                [void][WQ.Api]::GetClassName($h, $sb, 256)
                $r = New-Object WQ.Api+RECT
                [void][WQ.Api]::GetWindowRect($h, [ref]$r)
                [void]$list.Add([pscustomobject]@{
                    H = $h; Cls = $sb.ToString()
                    X = $r.L; Y = $r.T; W = $r.R - $r.L; Hh = $r.B - $r.T
                })
            }
        }
        return $true
    }
    [void][WQ.Api]::EnumWindows($cb, [IntPtr]::Zero)
    return $list
}

function Find-Ball { Get-Windows | Where-Object { $_.W -eq 44 -and $_.Hh -eq 44 } | Select-Object -First 1 }
function Find-Menu {
    Get-Windows | Where-Object {
        $_.Cls -like 'WindowsForms10*' -and -not ($_.W -eq 44 -and $_.Hh -eq 44) -and $_.W -ne 248
    } | Select-Object -First 1
}

function Read-MsaaItems($hwnd) {
    $iid = [System.Guid]'618736e0-3c3d-11cf-810c-00aa00389b71'
    $acc = $null
    $hr = [WQ.Api]::AccessibleObjectFromWindow([IntPtr]$hwnd, [WQ.Api]::OBJID_CLIENT, [ref]$iid, [ref]$acc)
    Write-Host ("  AccessibleObjectFromWindow hr=0x{0:X8}" -f $hr)
    if ($hr -ne 0 -or $acc -eq $null) { return ,@() }

    $t = $acc.GetType()
    $self = $t.InvokeMember('accName', 'GetProperty', $null, $acc, @([object]0))
    $cnt = $t.InvokeMember('accChildCount', 'GetProperty', $null, $acc, $null)
    Write-Host ("  root accName='{0}' accChildCount={1}" -f $self, $cnt)
    if ($cnt -le 0) { return ,@() }

    $buf = New-Object object[] $cnt
    $got = 0
    $hr2 = [WQ.Api]::AccessibleChildren($acc, 0, $cnt, $buf, [ref]$got)
    Write-Host ("  AccessibleChildren hr=0x{0:X8} got={1}" -f $hr2, $got)

    $names = @()
    for ($k = 0; $k -lt $got; $k++) {
        $ch = $buf[$k]
        if ($ch -is [int]) {
            $n = $t.InvokeMember('accName', 'GetProperty', $null, $acc, @([object]$ch))
            $r = $t.InvokeMember('accRole', 'GetProperty', $null, $acc, @([object]$ch))
            Write-Host ("  [{0}] varchild={1} role={2} name='{3}'" -f $k, $ch, $r, $n)
            if ($n) { $names += $n }
        } else {
            $ct = $ch.GetType()
            $n = $ct.InvokeMember('accName', 'GetProperty', $null, $ch, @([object]0))
            $r = $ct.InvokeMember('accRole', 'GetProperty', $null, $ch, @([object]0))
            $cc = $ct.InvokeMember('accChildCount', 'GetProperty', $null, $ch, $null)
            Write-Host ("  [{0}] obj role={1} childCount={2} name='{3}'" -f $k, $r, $cc, $n)
            if ($n) { $names += $n }
            if ($cc -gt 0) {
                $b2 = New-Object object[] $cc
                $g2 = 0
                [void][WQ.Api]::AccessibleChildren($ch, 0, $cc, $b2, [ref]$g2)
                for ($m = 0; $m -lt $g2; $m++) {
                    $gc = $b2[$m]
                    if ($gc -is [int]) {
                        $gn = $ct.InvokeMember('accName', 'GetProperty', $null, $ch, @([object]$gc))
                        $gr = $ct.InvokeMember('accRole', 'GetProperty', $null, $ch, @([object]$gc))
                        Write-Host ("      - var={0} role={1} name='{2}'" -f $gc, $gr, $gn)
                        if ($gn) { $names += $gn }
                    } else {
                        $gt = $gc.GetType()
                        $gn = $gt.InvokeMember('accName', 'GetProperty', $null, $gc, @([object]0))
                        $gr = $gt.InvokeMember('accRole', 'GetProperty', $null, $gc, @([object]0))
                        Write-Host ("      - obj role={0} name='{1}'" -f $gr, $gn)
                        if ($gn) { $names += $gn }
                    }
                }
            }
        }
    }
    return ,$names
}

$origin = New-Object WQ.Api+POINT
[void][WQ.Api]::GetCursorPos([ref]$origin)

$ball = Find-Ball
if (-not $ball) { Write-Host 'no ball'; exit 1 }

[void][WQ.Api]::SetCursorPos(300, 300)
Start-Sleep -Milliseconds 150
[WQ.Api]::mouse_event([WQ.Api]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WQ.Api]::mouse_event([WQ.Api]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Seconds 5

[void][WQ.Api]::SetCursorPos(($ball.X + 22), ($ball.Y + 22))
Start-Sleep -Milliseconds 200
[WQ.Api]::mouse_event([WQ.Api]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WQ.Api]::mouse_event([WQ.Api]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)

$menu = $null
for ($i = 1; $i -le 20; $i++) {
    Start-Sleep -Milliseconds 250
    $menu = Find-Menu
    if ($menu) { break }
}
if (-not $menu) { Write-Host 'no menu'; [void][WQ.Api]::SetCursorPos($origin.X, $origin.Y); exit 1 }
Write-Host ("menu hwnd={0} {1}x{2}" -f $menu.H, $menu.W, $menu.Hh)
Start-Sleep -Milliseconds 800

Write-Host ''
Write-Host '--- MSAA ---'
$items = Read-MsaaItems $menu.H
Write-Host ''
Write-Host ("collected names = " + $items.Count)

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 400
[void][WQ.Api]::SetCursorPos($origin.X, $origin.Y)
Write-Host 'probe2 done'
