using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.Metrics
{
    public class SystemSnapshot
    {
        public float CpuPercent;
        public float CpuTempC = -1;          // -1 = unavailable
        public ulong RamTotal;
        public ulong RamAvailable;
        public float RamPercent;
        public float GpuPercent = -1;
        public float GpuTempC = -1;
        public float VramUsedMB = -1;
        public double NetUpBps;
        public double NetDownBps;
        public long DiskFreeBytes;
        public long DiskTotalBytes;
        public float DiskReadBps = -1;
        public float DiskWriteBps = -1;
        public int BatteryPercent = -1;      // -1 = no battery
        public bool OnAc;
        public int VolumePercent = -1;
        public bool VolumeMuted;
    }

    public class ProcessInfoRow
    {
        public string Name { get; set; } = "";
        public int Pid { get; set; }
        public double CpuPercent { get; set; }
        public double MemoryMB { get; set; }
    }

    /// <summary>
    /// Polls system metrics. Cheap counters are read on every tick; expensive
    /// ones (GPU, temperatures) are refreshed less often and cached.
    /// </summary>
    public class MetricsService : IDisposable
    {
        private PerformanceCounter _cpu;
        private PerformanceCounter _diskRead, _diskWrite;
        private List<PerformanceCounter> _gpuCounters;
        private DateTime _lastGpuRefresh = DateTime.MinValue;
        private DateTime _lastTempRefresh = DateTime.MinValue;
        private float _cachedGpu = -1, _cachedCpuTemp = -1, _cachedGpuTemp = -1, _cachedVram = -1;

        private long _lastNetSent, _lastNetRecv;
        private DateTime _lastNetTime = DateTime.MinValue;

        // process CPU tracking
        private readonly Dictionary<int, (TimeSpan cpu, DateTime at)> _procCpu = new();

        public SystemSnapshot Latest { get; private set; } = new();

        public void Initialize()
        {
            try { _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", true); _cpu.NextValue(); }
            catch (Exception ex) { ConfigService.Log("CPU counter unavailable: " + ex.Message); }
            try
            {
                _diskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
                _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
                _diskRead.NextValue(); _diskWrite.NextValue();
            }
            catch (Exception ex) { ConfigService.Log("Disk counters unavailable: " + ex.Message); }
        }

        public SystemSnapshot Poll()
        {
            var s = new SystemSnapshot();

            try { s.CpuPercent = _cpu?.NextValue() ?? 0; } catch { }

            var mem = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (NativeMethods.GlobalMemoryStatusEx(ref mem))
            {
                s.RamTotal = mem.ullTotalPhys;
                s.RamAvailable = mem.ullAvailPhys;
                s.RamPercent = mem.dwMemoryLoad;
            }

            PollNetwork(s);

            try
            {
                var sys = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                s.DiskFreeBytes = sys.AvailableFreeSpace;
                s.DiskTotalBytes = sys.TotalSize;
            }
            catch { }

            try { s.DiskReadBps = _diskRead?.NextValue() ?? -1; s.DiskWriteBps = _diskWrite?.NextValue() ?? -1; } catch { }

            if (NativeMethods.GetSystemPowerStatus(out var power))
            {
                s.OnAc = power.ACLineStatus == 1;
                s.BatteryPercent = power.BatteryLifePercent == 255 || (power.BatteryFlag & 128) != 0
                    ? -1 : power.BatteryLifePercent;
            }

            try
            {
                s.VolumePercent = (int)Math.Round(CoreAudio.GetMasterVolume() * 100);
                s.VolumeMuted = CoreAudio.GetMasterMute();
            }
            catch { s.VolumePercent = -1; }

            // GPU counters are expensive to enumerate — refresh every 5s.
            if ((DateTime.UtcNow - _lastGpuRefresh).TotalSeconds >= 5)
            {
                _lastGpuRefresh = DateTime.UtcNow;
                RefreshGpu();
            }
            s.GpuPercent = _cachedGpu;
            s.VramUsedMB = _cachedVram;

            if ((DateTime.UtcNow - _lastTempRefresh).TotalSeconds >= 10)
            {
                _lastTempRefresh = DateTime.UtcNow;
                RefreshTemps();
            }
            s.CpuTempC = _cachedCpuTemp;
            s.GpuTempC = _cachedGpuTemp;

            Latest = s;
            return s;
        }

        private void PollNetwork(SystemSnapshot s)
        {
            long sent = 0, recv = 0;
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var st = nic.GetIPStatistics();
                    sent += st.BytesSent;
                    recv += st.BytesReceived;
                }
            }
            catch { }

            var now = DateTime.UtcNow;
            if (_lastNetTime != DateTime.MinValue)
            {
                double secs = (now - _lastNetTime).TotalSeconds;
                if (secs > 0.2)
                {
                    s.NetUpBps = Math.Max(0, (sent - _lastNetSent) / secs);
                    s.NetDownBps = Math.Max(0, (recv - _lastNetRecv) / secs);
                }
            }
            _lastNetSent = sent; _lastNetRecv = recv; _lastNetTime = now;
        }

        private void RefreshGpu()
        {
            try
            {
                if (_gpuCounters == null)
                {
                    var cat = new PerformanceCounterCategory("GPU Engine");
                    _gpuCounters = cat.GetInstanceNames()
                        .Where(n => n.Contains("engtype_3D"))
                        .SelectMany(n => cat.GetCounters(n))
                        .Where(c => c.CounterName == "Utilization Percentage")
                        .ToList();
                    foreach (var c in _gpuCounters) { try { c.NextValue(); } catch { } }
                    return; // first sample is meaningless
                }
                float total = 0;
                foreach (var c in _gpuCounters) { try { total += c.NextValue(); } catch { } }
                _cachedGpu = Math.Min(100, total);

                var memCat = new PerformanceCounterCategory("GPU Adapter Memory");
                float vram = 0;
                foreach (var inst in memCat.GetInstanceNames())
                {
                    using var c = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", inst, true);
                    vram += c.NextValue();
                }
                _cachedVram = vram / (1024f * 1024f);
            }
            catch
            {
                _cachedGpu = -1; _cachedVram = -1;
                _gpuCounters ??= new List<PerformanceCounter>();
            }
        }

        private void RefreshTemps()
        {
            // Best effort: MSAcpi thermal zone (often absent / BIOS-dependent).
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (var obj in searcher.Get())
                {
                    var deciKelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                    _cachedCpuTemp = (float)(deciKelvin / 10.0 - 273.15);
                    return;
                }
            }
            catch { _cachedCpuTemp = -1; }
            _cachedGpuTemp = -1; // vendor-specific; not exposed by Windows without vendor SDKs
        }

        public List<ProcessInfoRow> TopProcesses(int count)
        {
            var rows = new List<ProcessInfoRow>();
            var now = DateTime.UtcNow;
            int cores = Environment.ProcessorCount;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    double cpu = 0;
                    var total = p.TotalProcessorTime;
                    if (_procCpu.TryGetValue(p.Id, out var prev))
                    {
                        double secs = (now - prev.at).TotalSeconds;
                        if (secs > 0.2) cpu = (total - prev.cpu).TotalSeconds / secs / cores * 100.0;
                    }
                    _procCpu[p.Id] = (total, now);
                    rows.Add(new ProcessInfoRow
                    {
                        Name = p.ProcessName,
                        Pid = p.Id,
                        CpuPercent = Math.Max(0, Math.Round(cpu, 1)),
                        MemoryMB = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 1)
                    });
                }
                catch { } // access denied on system processes is normal
            }
            return rows.OrderByDescending(r => r.CpuPercent).ThenByDescending(r => r.MemoryMB).Take(count).ToList();
        }

        public static string FormatBytes(double bps)
        {
            if (bps < 0) return "--";
            if (bps < 1024) return $"{bps:0}B";
            if (bps < 1024 * 1024) return $"{bps / 1024:0.0}K";
            if (bps < 1024L * 1024 * 1024) return $"{bps / (1024.0 * 1024):0.0}M";
            return $"{bps / (1024.0 * 1024 * 1024):0.0}G";
        }

        public void Dispose()
        {
            _cpu?.Dispose(); _diskRead?.Dispose(); _diskWrite?.Dispose();
            if (_gpuCounters != null) foreach (var c in _gpuCounters) c.Dispose();
        }
    }
}
