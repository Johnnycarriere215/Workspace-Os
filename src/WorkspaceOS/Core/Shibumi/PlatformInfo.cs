using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.Shibumi
{
    /// <summary>
    /// Windows backends for the Shibumi plugins. Each member is a direct stand-in
    /// for the Linux service the plugin would have used: WMI brightness for
    /// <c>omarchy.monitor</c>, powercfg for the power-profile service, the Win32
    /// network stack for the network service, and the Bluetooth radio API for
    /// BlueZ.
    ///
    /// Everything is cached or bounded: the bar asks for these values every two
    /// seconds, so a probe must never block on WMI or a process launch twice.
    /// </summary>
    public static class PlatformInfo
    {
        // ---- battery / power ---------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        public static bool HasBattery
        {
            get
            {
                try
                {
                    if (!GetSystemPowerStatus(out var status)) return false;
                    // 128 = no system battery, 255 = unknown.
                    return status.BatteryFlag != 128 && status.BatteryFlag != 255;
                }
                catch { return false; }
            }
        }

        // ---- brightness (WMI) ---------------------------------------------

        private static readonly object BrightnessGate = new();
        private static bool _brightnessProbed;
        private static bool _brightnessSupported;
        private static int _brightness = -1;

        public static bool HasBrightness
        {
            get
            {
                lock (BrightnessGate)
                {
                    if (!_brightnessProbed) ReadBrightness();
                    return _brightnessSupported;
                }
            }
        }

        public static int Brightness
        {
            get
            {
                lock (BrightnessGate)
                {
                    if (!_brightnessProbed) ReadBrightness();
                    return _brightness;
                }
            }
        }

        private static DateTime _brightnessAt = DateTime.MinValue;

        private static void ReadBrightness()
        {
            _brightnessAt = DateTime.UtcNow;
            _brightnessProbed = true;
            _brightnessSupported = false;
            _brightness = -1;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (var item in searcher.Get())
                {
                    using (item)
                    {
                        var value = item["CurrentBrightness"];
                        if (value != null)
                        {
                            _brightness = Convert.ToInt32(value);
                            _brightnessSupported = true;
                        }
                    }
                    break;
                }
            }
            catch { }
        }

        public static bool SetBrightness(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (var item in searcher.Get())
                {
                    using (item)
                    {
                        ((ManagementObject)item).InvokeMethod(
                            "WmiSetBrightness", new object[] { 1u, (byte)percent });
                        _brightness = percent;
                        _brightnessProbed = true;
                        _brightnessSupported = true;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ---- power schemes -------------------------------------------------

        public sealed class PowerScheme
        {
            public Guid Guid { get; init; }
            public string Name { get; init; } = "";
            public bool Active { get; init; }
        }

        private static readonly object SchemeGate = new();
        private static DateTime _schemeAt = DateTime.MinValue;
        private static string _schemeName = "";

        public static List<PowerScheme> PowerSchemes()
        {
            var list = new List<PowerScheme>();
            try
            {
                string active = RunHidden("powercfg.exe", "/getactivescheme") ?? "";
                string output = RunHidden("powercfg.exe", "/list") ?? "";
                foreach (var raw in output.Split('\n'))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("Power Scheme GUID", StringComparison.OrdinalIgnoreCase)) continue;

                    int open = line.IndexOf('(');
                    int close = line.LastIndexOf(')');
                    if (open < 0 || close <= open) continue;

                    string guidText = line.Substring(line.IndexOf(':') + 1, open - line.IndexOf(':') - 1).Trim();
                    if (!Guid.TryParse(guidText, out var guid)) continue;

                    string name = line.Substring(open + 1, close - open - 1).Trim();
                    list.Add(new PowerScheme
                    {
                        Guid = guid,
                        Name = name,
                        Active = active.IndexOf(guidText, StringComparison.OrdinalIgnoreCase) >= 0
                    });
                }
            }
            catch { }
            return list;
        }

        /// <summary>Cached for 30 s: powercfg is a process launch, not a reading.</summary>
        public static string ActivePowerScheme()
        {
            lock (SchemeGate)
            {
                if ((DateTime.UtcNow - _schemeAt).TotalSeconds < 30) return _schemeName;
                var active = PowerSchemes().FirstOrDefault(p => p.Active);
                _schemeName = active?.Name ?? "";
                _schemeAt = DateTime.UtcNow;
                return _schemeName;
            }
        }

        public static bool SetPowerScheme(Guid guid)
        {
            try { return RunHidden("powercfg.exe", "/setactive " + guid) != null; }
            catch { return false; }
        }

        private static string RunHidden(string exe, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(psi);
                if (process == null) return null;
                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(2500)) { try { process.Kill(); } catch { } return null; }
                return output;
            }
            catch { return null; }
        }

        // ---- network -------------------------------------------------------

        public static string WifiSsid()
        {
            try
            {
                string output = RunHidden("netsh.exe", "wlan show interfaces");
                if (string.IsNullOrWhiteSpace(output)) return "";
                foreach (var raw in output.Split('\n'))
                {
                    string line = raw.Trim();
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string key = line.Substring(0, colon).Trim();
                    if (key.Equals("SSID", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(colon + 1).Trim();
                }
            }
            catch { }
            return "";
        }

        private static readonly object NetworkGate = new();
        private static DateTime _networkAt = DateTime.MinValue;
        private static string _networkName = "";

        public static string PrimaryNetwork()
        {
            lock (NetworkGate)
            {
                if ((DateTime.UtcNow - _networkAt).TotalSeconds < 8) return _networkName;
                _networkName = ReadPrimaryNetwork();
                _networkAt = DateTime.UtcNow;
                return _networkName;
            }
        }

        private static string ReadPrimaryNetwork()
        {
            try
            {
                var active = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .OrderByDescending(n => n.Speed)
                    .FirstOrDefault();
                if (active == null) return "Offline";

                if (active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    string ssid = WifiSsid();
                    return string.IsNullOrWhiteSpace(ssid) ? active.Name : ssid;
                }
                return active.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                    ? "Ethernet" : active.Name;
            }
            catch { return "Offline"; }
        }

        // ---- Bluetooth radio ----------------------------------------------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_RADIO_INFO
        {
            public uint dwSize;
            public ulong bluetoothAddress;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string szName;
            public uint ulClassofDevice;
            public ushort lmpSubversion;
            public ushort manufacturer;
        }

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstRadio(ref IntPtr findParams, out IntPtr radio);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindRadioClose(IntPtr findHandle);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern uint BluetoothGetRadioInfo(IntPtr radio,
            ref BLUETOOTH_RADIO_INFO info);

        [DllImport("bthprops.cpl")]
        private static extern bool BluetoothEnableDiscovery(IntPtr radio, bool enabled);

        [DllImport("bthprops.cpl")]
        private static extern bool BluetoothIsDiscoverable(IntPtr radio);

        private static readonly object BluetoothGate = new();
        private static DateTime _bluetoothAt = DateTime.MinValue;
        private static string _bluetoothName = "";

        /// <summary>The first Bluetooth radio's name, or empty when there is none.</summary>
        public static string BluetoothRadio()
        {
            lock (BluetoothGate)
            {
                if ((DateTime.UtcNow - _bluetoothAt).TotalSeconds < 30) return _bluetoothName;

                _bluetoothAt = DateTime.UtcNow;
                _bluetoothName = "";
                IntPtr find = IntPtr.Zero;
                try
                {
                    IntPtr radio;
                    find = BluetoothFindFirstRadio(ref find, out radio);
                    if (find == IntPtr.Zero && radio == IntPtr.Zero) return _bluetoothName;

                    var info = new BLUETOOTH_RADIO_INFO();
                    info.dwSize = (uint)Marshal.SizeOf<BLUETOOTH_RADIO_INFO>();
                    if (BluetoothGetRadioInfo(radio, ref info) == 0)
                        _bluetoothName = string.IsNullOrWhiteSpace(info.szName)
                            ? "Bluetooth" : info.szName.Trim();
                    else
                        _bluetoothName = "Bluetooth";
                }
                catch { }
                finally
                {
                    if (find != IntPtr.Zero) { try { BluetoothFindRadioClose(find); } catch { } }
                }
                return _bluetoothName;
            }
        }

        // ---- availability --------------------------------------------------

        /// <summary>
        /// Marks plugins this machine cannot serve. Called once at startup (and
        /// from the Shibumi settings page) so the bar never shows a pill for a
        /// sensor the hardware does not have.
        /// </summary>
        public static void Probe()
        {
            bool battery = HasBattery;
            bool brightness = HasBrightness;
            bool bluetooth = !string.IsNullOrEmpty(BluetoothRadio());

            foreach (var plugin in ShibumiSuite.All)
            {
                switch (plugin.Key)
                {
                    case "Battery": plugin.Available = battery; break;
                    case "Brightness": plugin.Available = brightness; break;
                    case "Bluetooth": plugin.Available = bluetooth; break;
                    case "Media": plugin.Available = false; break;   // needs the WinRT media bridge
                }
            }
        }
    }
}
