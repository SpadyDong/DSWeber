Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class W10 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);

    public struct RECT { public int Left, Top, Right, Bottom; }
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP   = 0x0004;

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
    foreach ($row in [W10]::Scan($target)) {
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

function Find-Ball($target) {
    foreach ($w in Get-Windows $target) {
        if ($w.Width -eq 44 -and $w.Height -eq 44) { return $w }
    }
    return $null
}

function Find-Popup($target) {
    foreach ($w in Get-Windows $target) {
        if ($w.Width -eq 248 -and $w.Height -ge 80 -and $w.Height -le 160) { return $w }
    }
    return $null
}

function Wait-Popup($target, $timeoutMs) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $p = Find-Popup $target
        if ($null -ne $p) { return $p }
        Start-Sleep -Milliseconds 60
    }
    return $null
}

function Wait-NoPopup($target, $timeoutMs) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        if ($null -eq (Find-Popup $target)) { return $true }
        Start-Sleep -Milliseconds 60
    }
    return $false
}

$proc = Get-Process DshTray -ErrorAction Stop
$target = [uint32]$proc.Id
Write-Host "DshTray PID = $target"

$pass = $true
$origin = [System.Windows.Forms.Cursor]::Position

# ============ CASE A: click ball -> popup appears -> auto-close after 3s ============
Write-Host ""
Write-Host "== CASE A: auto-close after 3s =="

$ball = Find-Ball $target
if ($null -eq $ball) { Write-Host "  FAIL: ball not found"; exit 1 }

[W10]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
Start-Sleep -Milliseconds 500
[W10]::mouse_event([W10]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 100
[W10]::mouse_event([W10]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)

$popup = Wait-Popup $target 3000
if ($null -eq $popup) {
    Write-Host "  FAIL: popup did not appear"
    $pass = $false
} else {
    Write-Host ("  popup appeared at ({0},{1}) size {2}x{3}" -f $popup.Left, $popup.Top, $popup.Width, $popup.Height)
    
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $closed = Wait-NoPopup $target 8000
    $sw.Stop()
    $elapsed = $sw.ElapsedMilliseconds
    if ($closed) {
        Write-Host ("  popup closed after {0} ms" -f $elapsed)
        if ($elapsed -ge 2000 -and $elapsed -le 4200) {
            Write-Host "  OK: auto-close timing matches 3s"
        } else {
            Write-Host "  FAIL: auto-close timing is not ~3s"
            $pass = $false
        }
    } else {
        Write-Host "  FAIL: popup did not auto-close within 8s"
        $pass = $false
    }
}

# ============ CASE B: click ball -> popup -> click blank spot -> close ============
Write-Host ""
Write-Host "== CASE B: click blank spot closes popup =="

$ball = Find-Ball $target
if ($null -eq $ball) { Write-Host "  FAIL: ball not found"; exit 1 }

[W10]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
Start-Sleep -Milliseconds 500
[W10]::mouse_event([W10]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 100
[W10]::mouse_event([W10]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)

$popup = Wait-Popup $target 3000
if ($null -eq $popup) {
    Write-Host "  FAIL: popup did not appear"
    $pass = $false
} else {
    Write-Host ("  popup appeared at ({0},{1}) size {2}x{3}" -f $popup.Left, $popup.Top, $popup.Width, $popup.Height)
    
    $blankX = 400
    $blankY = 300
    [W10]::SetCursorPos($blankX, $blankY) | Out-Null
    Start-Sleep -Milliseconds 300
    [W10]::mouse_event([W10]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [W10]::mouse_event([W10]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    
    $closed = Wait-NoPopup $target 3000
    if ($closed) {
        Write-Host "  OK: popup closed by blank click at ($blankX,$blankY)"
    } else {
        Write-Host "  FAIL: popup did not close after blank click"
        $pass = $false
    }
}

# ============ CASE C: drag ball -> popup must stay and follow ============
Write-Host ""
Write-Host "== CASE C: drag ball keeps popup and follows =="

$ball = Find-Ball $target
if ($null -eq $ball) { Write-Host "  FAIL: ball not found"; exit 1 }

$startLeft = $ball.Left
$startTop  = $ball.Top

[W10]::SetCursorPos(($ball.Left + 22), ($ball.Top + 22)) | Out-Null
Start-Sleep -Milliseconds 500
[W10]::mouse_event([W10]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 100
[W10]::mouse_event([W10]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)

$popup = Wait-Popup $target 3000
if ($null -eq $popup) {
    Write-Host "  FAIL: popup did not appear"
    $pass = $false
} else {
    $popupLeft0 = $popup.Left
    $popupTop0  = $popup.Top
    Write-Host ("  popup appeared at ({0},{1})" -f $popupLeft0, $popupTop0)

    $dx = -80
    $dy = -60

    [W10]::SetCursorPos(($startLeft + 22), ($startTop + 22)) | Out-Null
    [W10]::mouse_event([W10]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    for ($i = 1; $i -le 4; $i++) {
        $mx = $startLeft + 22 + [int]($dx * $i / 4)
        $my = $startTop  + 22 + [int]($dy * $i / 4)
        [W10]::SetCursorPos($mx, $my) | Out-Null
        Start-Sleep -Milliseconds 60
    }
    [W10]::mouse_event([W10]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 150

    $popup2 = Find-Popup $target
    $ball2  = Find-Ball $target

    if ($null -eq $popup2) {
        Write-Host "  FAIL: popup was dismissed by dragging the ball"
        $pass = $false
    } else {
        Write-Host ("  ball moved ({0},{1}) -> ({2},{3})" -f $startLeft, $startTop, $ball2.Left, $ball2.Top)
        Write-Host ("  popup moved ({0},{1}) -> ({2},{3})" -f $popupLeft0, $popupTop0, $popup2.Left, $popup2.Top)

        $ballMoved  = ($ball2.Left -ne $startLeft) -or ($ball2.Top -ne $startTop)
        $popupMoved = ($popup2.Left -ne $popupLeft0) -or ($popup2.Top -ne $popupTop0)

        if (-not $ballMoved) {
            Write-Host "  FAIL: ball did not move, drag was not performed"
            $pass = $false
        } elseif (-not $popupMoved) {
            Write-Host "  FAIL: popup stayed while ball moved"
            $pass = $false
        } else {
            Write-Host "  OK: popup survived the drag and followed the ball"
        }
    }

    # drag the ball back to its original spot
    $cur = Find-Ball $target
    if ($null -ne $cur) {
        [W10]::SetCursorPos(($cur.Left + 22), ($cur.Top + 22)) | Out-Null
        Start-Sleep -Milliseconds 200
        [W10]::mouse_event([W10]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 60
        for ($i = 1; $i -le 4; $i++) {
            $mx = $cur.Left + 22 + [int](($startLeft - $cur.Left) * $i / 4)
            $my = $cur.Top  + 22 + [int](($startTop  - $cur.Top)  * $i / 4)
            [W10]::SetCursorPos($mx, $my) | Out-Null
            Start-Sleep -Milliseconds 60
        }
        [W10]::mouse_event([W10]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 200
        $back = Find-Ball $target
        if ($null -ne $back) {
            Write-Host ("  ball restored to ({0},{1})" -f $back.Left, $back.Top)
        }
    }
}

Write-Host ""
if ($pass) { Write-Host "RESULT: PASS" } else { Write-Host "RESULT: FAILED" }

[W10]::SetCursorPos($origin.X, $origin.Y) | Out-Null