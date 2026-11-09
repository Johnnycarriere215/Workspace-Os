$key = 'HKCU:\Control Panel\International'
Set-ItemProperty -Path $key -Name sShortTime -Value 'HH:mm'
Set-ItemProperty -Path $key -Name sTimeFormat -Value 'HH:mm:ss'
Set-ItemProperty -Path $key -Name sTime -Value 'HH:mm:ss'
Set-ItemProperty -Path $key -Name iTime -Value '1'
Set-ItemProperty -Path $key -Name iTimePrefix -Value '0'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class IntlNotify {
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam,
        string lParam, uint flags, uint timeout, out UIntPtr result);
}
"@
$result = [UIntPtr]::Zero
[void][IntlNotify]::SendMessageTimeout([IntPtr]0xffff, 0x001A, [UIntPtr]::Zero, "intl", 2, 3000, [ref]$result)

$i = Get-ItemProperty $key
"shortTime=$($i.sShortTime) longTime=$($i.sTimeFormat) iTime=$($i.iTime)"
