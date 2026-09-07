Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class W9 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);

    public struct RECT { public int Left, Top, Right, Bottom; }
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
            list.Add(h.ToInt64() + "|" + sb.ToString() + "|" + r.Left + "|" + r.Top + "|" + (r.Right - r.Left) + "|" + (r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

function Get-Windows($target) {
    $out = New-Object System.Collections.ArrayList
    foreach ($row in [W9]::Scan($target)) {
        $p = $row -split '\|'
        [void]$out.Add([PSCustomObject]@{
            Hwnd   = [int64]$p[0]
            Class  = $p[1]
            Left   = [int]$p[2]
            Top    = [int]$p[3]
            Width  = [int]$p[4]
            Height = [int]$p[5]
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

function Get-MenuItems($hwnd) {
    $el = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$hwnd)
    if ($null -eq $el) {
        Write-Host "  (UIA: FromHandle returned null)"
        return @()
    }
    Write-Host ("  (UIA root: name='{0}' type={1})" -f $el.Current.Name, $el.Current.ControlType.ProgrammaticName)

    $found = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                         [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host ("  (UIA descendants = {0})" -f $found.Count)

    $names = New-Object System.Collections.ArrayList
    foreach ($f in $found) {
        $t = $f.Current.ControlType.ProgrammaticName
        Write-Host ("    raw: type={0} name='{1}'" -f $t, $f.Current.Name)
        if ($t -eq "ControlType.MenuItem") { [void]$names.Add($f.Current.Name) }
    }
    return $names.ToArray()
}

$proc = Get-Process DshTray -ErrorAction Stop
$target = [uint32]$proc.Id
Write-Host "DshTray PID = $target"

$origin = [System.Windows.Forms.Cursor]::Position
$pass = $true

$ball = Find-Ball $target
if ($null -eq $ball) { Write-Host "FAIL: ball not found"; exit 1 }

Write-Host ""
Write-Host "== ball right-click menu items =="
[W9]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
Start-Sleep -Milliseconds 500
[W9]::mouse_event([W9]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 100
[W9]::mouse_event([W9]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)

$menu = Wait-Menu $target 3000
if ($null -eq $menu) {
    Write-Host "  FAIL: menu did not appear"
    $pass = $false
} else {
    Start-Sleep -Milliseconds 300
    $items = Get-MenuItems $menu.Hwnd
    Write-Host ("  item count = {0}" -f $items.Count)
    foreach ($n in $items) { Write-Host ("    [{0}]" -f $n) }

    $hasExit = $false
    $exitText = [string][char]0x9000 + [string][char]0x51FA
    foreach ($n in $items) { if ($n -eq $exitText) { $hasExit = $true } }
    if ($items.Count -eq 0) {
        Write-Host "  FAIL: no menu items were enumerated, absence check is meaningless"
        $pass = $false
    } elseif ($hasExit) {
        Write-Host "  FAIL: ball menu still contains exit item"
        $pass = $false
    } else {
        Write-Host "  OK: ball menu has no exit item"
    }
}

[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 600

Write-Host ""
if ($pass) { Write-Host "RESULT: PASS" } else { Write-Host "RESULT: FAILED" }

[W9]::SetCursorPos($origin.X, $origin.Y) | Out-Null
