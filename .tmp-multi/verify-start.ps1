Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WS {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
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
function WinTitle($h) { $sb = New-Object System.Text.StringBuilder 256; [void][WS]::GetWindowText($h, $sb, 256); return $sb.ToString() }
function WinClass($h) { $sb = New-Object System.Text.StringBuilder 256; [void][WS]::GetClassName($h, $sb, 256); return $sb.ToString() }
function Windows($pidW) {
    $list = New-Object System.Collections.ArrayList
    [void][WS]::EnumWindows({ param($h, $p) [void]$list.Add($h); return $true }, [IntPtr]::Zero)
    $out = @()
    foreach ($h in $list) {
        $q = 0; [void][WS]::GetWindowThreadProcessId($h, [ref]$q)
        if ($q -ne $pidW -or -not [WS]::IsWindowVisible($h)) { continue }
        $r = New-Object WS+RECT; [void][WS]::GetWindowRect($h, [ref]$r)
        $out += [pscustomobject]@{ Hwnd = $h; L = $r.L; T = $r.T; R = $r.R; B = $r.B; W = $r.R - $r.L; H = $r.B - $r.T }
    }
    return $out
}
function Launchers($pidW) { @(Windows $pidW | Where-Object { $_.W -ge 400 -and $_.W -le 700 -and $_.H -gt 80 }) }
function ForegroundReport($label) {
    $fg = [WS]::GetForegroundWindow()
    "$label foreground=$fg title='$(WinTitle $fg)' class=$(WinClass $fg)"
}
$root = "C:\Users\Jmoney212\Documents\GitHub\Workspace-Os\.tmp-multi"
function Shot([int]$x, [int]$y, [int]$w, [int]$h, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $root $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
function KeyTap([byte]$vk) {
    [WS]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    [WS]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
}
$pidW = (Get-Process WorkspaceOS).Id
Send-Cmd 'ws:2' | Out-Null
Start-Sleep -Milliseconds 300
[WS]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero)
[WS]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 500
"diag=" + (Send-Cmd 'diag')
"launchers-before=$(Launchers $pidW).Count"

"--- Win+Space ---"
[WS]::keybd_event(0x5B, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
KeyTap 0x20
Start-Sleep -Milliseconds 40
[WS]::keybd_event(0x5B, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 1200
ForegroundReport "winspace"
"launchers-after-winspace=$(Launchers $pidW).Count"
Shot 0 0 1920 1080 "winspace-start.png"
KeyTap 0x1B
Start-Sleep -Milliseconds 600

"--- Win alone ---"
KeyTap 0x5B
Start-Sleep -Milliseconds 1200
ForegroundReport "win"
Shot 0 0 1920 1080 "win-alone-start.png"
KeyTap 0x1B
Start-Sleep -Milliseconds 600

"--- Alt+Space ---"
[WS]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
KeyTap 0x20
Start-Sleep -Milliseconds 40
[WS]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 1200
ForegroundReport "altspace"
$launcher = Launchers $pidW | Select-Object -First 1
"launcher-after-altspace=$([bool]$launcher)"
if ($launcher) {
    Shot $launcher.L $launcher.T $launcher.W $launcher.H "altspace-launcher.png"
    [void][WS]::PostMessage($launcher.Hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
}
"verify-start-done"
