Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
public class WX {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
function Send-Cmd([string]$c) {
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'WorkspaceOS.Ctrl', [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(3000)
        $w = New-Object System.IO.StreamWriter($pipe); $w.AutoFlush = $true; $w.WriteLine($c)
        $r = New-Object System.IO.StreamReader($pipe); $line = $r.ReadLine()
        $pipe.Dispose()
        return $line
    } catch { return "ERROR: " + $_.Exception.Message }
}
function StartState {
    $tray = [WX]::FindWindow("Shell_TrayWnd", $null)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($tray)
    $start = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "StartButton")))
    if (-not $start) { return "unknown" }
    $t = $start.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    return $t.Current.ToggleState.ToString()
}
function Windows($pidW) {
    $list = New-Object System.Collections.ArrayList
    [void][WX]::EnumWindows({ param($h, $p) [void]$list.Add($h); return $true }, [IntPtr]::Zero)
    $out = @()
    foreach ($h in $list) {
        $q = 0; [void][WX]::GetWindowThreadProcessId($h, [ref]$q)
        if ($q -ne $pidW -or -not [WX]::IsWindowVisible($h)) { continue }
        $r = New-Object WX+RECT; [void][WX]::GetWindowRect($h, [ref]$r)
        $out += [pscustomobject]@{ Hwnd = $h; L = $r.L; T = $r.T; R = $r.R; B = $r.B; W = $r.R - $r.L; H = $r.B - $r.T }
    }
    return $out
}
$root = "C:\Users\Jmoney212\Documents\GitHub\Workspace-Os\.tmp-multi"
function Shot([int]$x, [int]$y, [int]$w, [int]$h, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $root $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
function Esc {
    [WX]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero)
    [WX]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
}
$pidW = (Get-Process WorkspaceOS).Id
Send-Cmd 'ws:2' | Out-Null
Start-Sleep -Milliseconds 300
Esc; Start-Sleep -Milliseconds 500
"diag=" + (Send-Cmd 'diag')
"start-before=$(StartState)"

"--- Win+Space ---"
[WX]::keybd_event(0x5B, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[WX]::keybd_event(0x20, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[WX]::keybd_event(0x20, 0, 2, [UIntPtr]::Zero)
[WX]::keybd_event(0x5B, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 1600
"start-after-winspace=$(StartState)"
Shot 0 0 1920 1080 "final2-winspace.png"
Esc; Start-Sleep -Milliseconds 600
"start-after-escape=$(StartState)"

"--- Win alone ---"
[WX]::keybd_event(0x5B, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[WX]::keybd_event(0x5B, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 1200
"start-after-win=$(StartState)"
Esc; Start-Sleep -Milliseconds 600

"--- Alt+Space ---"
[WX]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[WX]::keybd_event(0x20, 0, 0, [UIntPtr]::Zero)
[WX]::keybd_event(0x20, 0, 2, [UIntPtr]::Zero)
[WX]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 1200
$launcher = Windows $pidW | Where-Object { $_.W -ge 400 -and $_.W -le 700 -and $_.H -gt 80 } | Select-Object -First 1
"launcher-after-altspace=$([bool]$launcher)"
if ($launcher) {
    Shot $launcher.L $launcher.T $launcher.W $launcher.H "final2-launcher.png"
    [void][WX]::PostMessage($launcher.Hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
}
Start-Sleep -Milliseconds 500

"--- clock ---"
$bar = Windows $pidW | Where-Object { $_.H -le 60 -and $_.W -ge 800 -and $_.L -eq 0 } | Select-Object -First 1
$el = [System.Windows.Automation.AutomationElement]::FromHandle($bar.Hwnd)
$texts = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)))
foreach ($t in $texts) {
    if ($t.Current.Name -match '^\d{2}:\d{2}') { "bar-clock=" + $t.Current.Name }
}
Shot 0 980 1920 100 "final2-taskbar.png"
"verify-start2-done"
