$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -Namespace VT -Name Api -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
'@

function Dump($cls) {
    $h = [VT.Api]::FindWindow($cls, $null)
    Write-Host ("---- {0} hwnd={1} ----" -f $cls, $h)
    if ($h -eq [IntPtr]::Zero) { Write-Host '  not found'; return }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
    if (-not $root) { Write-Host '  FromHandle null'; return }
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host ("  descendants = " + $all.Count)
    foreach ($e in $all) {
        $r = $e.Current.BoundingRectangle
        Write-Host ("  [{0}] name='{1}' aid='{2}' rect={3},{4} {5}x{6}" -f `
            $e.Current.ControlType.ProgrammaticName, $e.Current.Name, $e.Current.AutomationId,
            [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
    }
}

Dump 'Shell_TrayWnd'
Write-Host ''
Dump 'TopLevelWindowForOverflowXamlIsland'
