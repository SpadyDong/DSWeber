$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -Namespace WI3 -Name Api -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
public delegate bool EnumWindowsProc(IntPtr h, IntPtr p);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("oleacc.dll")] public static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref System.Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
public struct RECT { public int L, T, R, B; }
public struct POINT { public int X, Y; }
public const uint RIGHTDOWN = 0x0008;
public const uint RIGHTUP   = 0x0010;
public const uint LEFTDOWN  = 0x0002;
public const uint LEFTUP    = 0x0004;
public const uint OBJID_CLIENT = 0xFFFFFFFC;
'@

Add-Type -ReferencedAssemblies 'Accessibility' -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Accessibility;

public static class MsaaHelper
{
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible ppv);

    private const uint OBJID_CLIENT = 0xFFFFFFFC;

    public static string Diag = "";

    public static string[] GetChildRows(IntPtr hwnd)
    {
        Diag = "";
        IAccessible acc = null;
        Guid iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
        int hr = AccessibleObjectFromWindow(hwnd, OBJID_CLIENT, ref iid, out acc);
        if (hr != 0 || acc == null)
        {
            Diag = "AccessibleObjectFromWindow hr=0x" + hr.ToString("X8") + " acc=" + (acc != null);
            return new string[0];
        }

        int cnt = 0;
        try { cnt = acc.accChildCount; }
        catch (Exception ex) { Diag = "accChildCount ERR: " + ex.Message; return new string[0]; }

        string rootName = "";
        try { rootName = acc.get_accName(0); } catch { }
        Diag = "root accName='" + (rootName == null ? "" : rootName) + "' accChildCount=" + cnt;

        List<string> rows = new List<string>();
        for (int i = 1; i <= cnt; i++)
        {
            string nm = "";
            try { nm = acc.get_accName(i); } catch { }
            string rl = "";
            try { object r = acc.get_accRole(i); if (r != null) rl = r.ToString(); } catch { }
            rows.Add(i + "|" + rl + "|" + (nm == null ? "" : nm));
        }
        return rows.ToArray();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

    private static IntPtr[] TrayToolbars()
    {
        List<IntPtr> tbs = new List<IntPtr>();

        IntPtr tray = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
        if (tray != IntPtr.Zero)
        {
            IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (notify != IntPtr.Zero)
            {
                IntPtr pager = FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
                IntPtr tb = IntPtr.Zero;
                if (pager != IntPtr.Zero) tb = FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null);
                if (tb == IntPtr.Zero) tb = FindWindowEx(notify, IntPtr.Zero, "ToolbarWindow32", null);
                if (tb != IntPtr.Zero) tbs.Add(tb);
            }
        }

        IntPtr ovf = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "NotifyIconOverflowWindow", null);
        if (ovf != IntPtr.Zero)
        {
            IntPtr tb2 = FindWindowEx(ovf, IntPtr.Zero, "ToolbarWindow32", null);
            if (tb2 != IntPtr.Zero) tbs.Add(tb2);
        }

        return tbs.ToArray();
    }

    public static string FindTrayIcon(string needle)
    {
        Diag = "";
        Guid iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
        List<string> seen = new List<string>();

        foreach (IntPtr tb in TrayToolbars())
        {
            IAccessible acc = null;
            int hr = AccessibleObjectFromWindow(tb, OBJID_CLIENT, ref iid, out acc);
            if (hr != 0 || acc == null) continue;

            int cnt = 0;
            try { cnt = acc.accChildCount; } catch { continue; }

            for (int i = 1; i <= cnt; i++)
            {
                string nm = "";
                try { nm = acc.get_accName(i); } catch { }
                if (nm == null) nm = "";
                seen.Add(nm.Replace("\r", " ").Replace("\n", " "));
                if (nm.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;

                int l, t, w, h;
                try { acc.accLocation(out l, out t, out w, out h, i); }
                catch { continue; }
                if (w <= 0 || h <= 0) continue;

                Diag = "matched '" + nm.Replace("\r", " ").Replace("\n", " ") + "'";
                return (l + w / 2) + "|" + (t + h / 2);
            }
        }

        Diag = "no match; icons seen: " + string.Join(" ;; ", seen.ToArray());
        return "";
    }
}
'@

$EXIT_TEXT = [string][char]0x9000 + [string][char]0x51FA

function Get-Windows {
    $tp = (Get-Process -Name DshTray -ErrorAction SilentlyContinue).Id
    $list = New-Object System.Collections.ArrayList
    $cb = [WI3.Api+EnumWindowsProc]{
        param($h, $p)
        if ([WI3.Api]::IsWindowVisible($h)) {
            $o = 0
            [void][WI3.Api]::GetWindowThreadProcessId($h, [ref]$o)
            if ($o -eq $tp) {
                $sb = New-Object System.Text.StringBuilder 256
                [void][WI3.Api]::GetClassName($h, $sb, 256)
                $r = New-Object WI3.Api+RECT
                [void][WI3.Api]::GetWindowRect($h, [ref]$r)
                [void]$list.Add([pscustomobject]@{
                    H = $h; Cls = $sb.ToString()
                    X = $r.L; Y = $r.T; W = $r.R - $r.L; Hh = $r.B - $r.T
                })
            }
        }
        return $true
    }
    [void][WI3.Api]::EnumWindows($cb, [IntPtr]::Zero)
    return $list
}

function Find-Ball { Get-Windows | Where-Object { $_.W -eq 44 -and $_.Hh -eq 44 } | Select-Object -First 1 }
function Find-Popup { Get-Windows | Where-Object { $_.W -eq 248 } | Select-Object -First 1 }
function Find-Menu {
    Get-Windows | Where-Object {
        $_.Cls -like 'WindowsForms10*' -and -not ($_.W -eq 44 -and $_.Hh -eq 44) -and $_.W -ne 248
    } | Select-Object -First 1
}

function Read-Items($hwnd) {
    for ($try = 1; $try -le 10; $try++) {
        $raw = [MsaaHelper]::GetChildRows([IntPtr]$hwnd)
        Write-Host ("    [diag] " + [MsaaHelper]::Diag)
        if ($raw.Length -gt 0) {
            $rows = @()
            foreach ($line in $raw) {
                $p = $line.Split('|')
                $rows += [pscustomobject]@{ Idx = $p[0]; Role = $p[1]; Name = $p[2] }
            }
            return ,$rows
        }
        Start-Sleep -Milliseconds 400
    }
    return ,@()
}

function Find-TrayIcon($needle) {
    $seen = @()
    foreach ($cls in @('Shell_TrayWnd', 'TopLevelWindowForOverflowXamlIsland')) {
        $h = [WI3.Api]::FindWindow($cls, $null)
        if ($h -eq [IntPtr]::Zero) { continue }
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
        if (-not $root) { continue }
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
        foreach ($e in $all) {
            $nm = $e.Current.Name
            if (-not $nm) { $nm = '' }
            $seen += ($nm -replace "`r|`n", ' ')
            if ($nm.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
            $r = $e.Current.BoundingRectangle
            if ($r.Width -le 0 -or $r.Height -le 0) { continue }
            $script:TrayDiag = "matched '" + ($nm -replace "`r|`n", ' ') + "'"
            return [pscustomobject]@{
                X = [int]($r.X + $r.Width / 2)
                Y = [int]($r.Y + $r.Height / 2)
            }
        }
    }
    $script:TrayDiag = 'no match; buttons seen: ' + ($seen -join ' ;; ')
    return $null
}

function Clear-State {
    [void][WI3.Api]::SetCursorPos(300, 300)
    Start-Sleep -Milliseconds 150
    [WI3.Api]::mouse_event([WI3.Api]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    [WI3.Api]::mouse_event([WI3.Api]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Seconds 5
}

$proc = Get-Process -Name DshTray -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host 'FAIL: DshTray not running'; exit 1 }
Write-Host ("DshTray PID = " + $proc.Id)

$origin = New-Object WI3.Api+POINT
[void][WI3.Api]::GetCursorPos([ref]$origin)

$ball = Find-Ball
if (-not $ball) { Write-Host 'FAIL: ball window not found'; exit 1 }
$startX = $ball.X
$startY = $ball.Y

Clear-State

$fail = 0

Write-Host ''
Write-Host '== A: BALL right-click menu (must NOT contain exit) =='
[void][WI3.Api]::SetCursorPos(($ball.X + 22), ($ball.Y + 22))
Start-Sleep -Milliseconds 200
[WI3.Api]::mouse_event([WI3.Api]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
[WI3.Api]::mouse_event([WI3.Api]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)

$menu = $null
for ($i = 1; $i -le 20; $i++) {
    Start-Sleep -Milliseconds 250
    $menu = Find-Menu
    if ($menu) { break }
}

if (-not $menu) {
    Write-Host '  FAIL: menu did not appear'
    $fail = 1
} else {
    Write-Host ("  menu hwnd={0} size {1}x{2}" -f $menu.H, $menu.W, $menu.Hh)
    $rows = Read-Items $menu.H
    Write-Host ("  child count = " + $rows.Count)
    foreach ($r in $rows) {
        Write-Host ("    [{0}] role={1} name='{2}'" -f $r.Idx, $r.Role, $r.Name)
    }

    if ($rows.Count -eq 0) {
        Write-Host '  FAIL: could not enumerate children'
        $fail = 1
    } else {
        $named = @($rows | Where-Object { $_.Name -ne '' })
        Write-Host ("  named items = " + $named.Count)
        if ($named.Count -eq 0) {
            Write-Host '  FAIL: no named items, absence check is meaningless'
            $fail = 1
        } else {
            $hit = @($named | Where-Object { $_.Name -eq $EXIT_TEXT })
            if ($hit.Count -gt 0) {
                Write-Host ("  FAIL: ball menu contains '{0}'" -f $EXIT_TEXT)
                $fail = 1
            } else {
                Write-Host ("  OK: ball menu does NOT contain '{0}'" -f $EXIT_TEXT)
            }
        }
    }

    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 600
    if (Find-Menu) {
        Write-Host '  FAIL: menu still visible after ESC'
        $fail = 1
    } else {
        Write-Host '  OK: menu dismissed by ESC'
    }
}

Clear-State

Write-Host ''
Write-Host '== B: TRAY icon right-click menu (MUST contain exit) =='
$loc = Find-TrayIcon 'dsh web'
Write-Host ("  [diag] " + $script:TrayDiag)

if (-not $loc) {
    Write-Host '  FAIL: tray icon not found'
    $fail = 1
} else {
    $tx = $loc.X
    $ty = $loc.Y
    Write-Host ("  tray icon center = ({0},{1})" -f $tx, $ty)

    [void][WI3.Api]::SetCursorPos($tx, $ty)
    Start-Sleep -Milliseconds 400
    [WI3.Api]::mouse_event([WI3.Api]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    [WI3.Api]::mouse_event([WI3.Api]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)

    $menu2 = $null
    for ($i = 1; $i -le 20; $i++) {
        Start-Sleep -Milliseconds 250
        $menu2 = Find-Menu
        if ($menu2) { break }
    }

    if (-not $menu2) {
        Write-Host '  FAIL: tray menu did not appear'
        $fail = 1
    } else {
        Write-Host ("  menu hwnd={0} size {1}x{2}" -f $menu2.H, $menu2.W, $menu2.Hh)
        $rows2 = Read-Items $menu2.H
        Write-Host ("  child count = " + $rows2.Count)
        foreach ($r in $rows2) {
            Write-Host ("    [{0}] role={1} name='{2}'" -f $r.Idx, $r.Role, $r.Name)
        }

        if ($rows2.Count -eq 0) {
            Write-Host '  FAIL: could not enumerate children'
            $fail = 1
        } else {
            if ($rows2.Count -ne 10) {
                Write-Host ("  FAIL: expected 10 children, got " + $rows2.Count)
                $fail = 1
            } else {
                Write-Host '  OK: tray menu has 10 children (8 + exit + separator)'
            }
            $hit2 = @($rows2 | Where-Object { $_.Name -eq $EXIT_TEXT })
            if ($hit2.Count -gt 0) {
                Write-Host ("  OK: tray menu contains '{0}'" -f $EXIT_TEXT)
            } else {
                Write-Host ("  FAIL: tray menu does NOT contain '{0}'" -f $EXIT_TEXT)
                $fail = 1
            }
        }

        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 600
        if (Find-Menu) {
            Write-Host '  FAIL: tray menu still visible after ESC'
            $fail = 1
        } else {
            Write-Host '  OK: tray menu dismissed by ESC'
        }
    }
}

$ball2 = Find-Ball
if ($ball2) {
    Write-Host ''
    Write-Host ("Ball final position ({0},{1})  [start ({2},{3})]" -f $ball2.X, $ball2.Y, $startX, $startY)
    if ($ball2.X -ne $startX -or $ball2.Y -ne $startY) {
        Write-Host '  FAIL: ball moved'
        $fail = 1
    }
}

[void][WI3.Api]::SetCursorPos($origin.X, $origin.Y)

Write-Host ''
if ($fail -eq 0) { Write-Host 'RESULT: PASS'; exit 0 } else { Write-Host 'RESULT: FAIL'; exit 1 }
