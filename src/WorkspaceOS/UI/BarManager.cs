using System;
using System.Collections.Generic;
using System.Linq;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Owns one <see cref="TopBarWindow"/> per physical monitor. Each bar registers
    /// its own Win32 appbar on its monitor, so every screen reserves its own strip,
    /// and all bars follow the same global virtual desktop.
    /// </summary>
    public sealed class BarManager : IDisposable
    {
        private readonly Dictionary<string, TopBarWindow> _bars =
            new(StringComparer.OrdinalIgnoreCase);
        private bool _reconciling;

        public int Count => _bars.Count;

        public void Start() => Reconcile();

        /// <summary>Creates, removes and repositions bars to match the monitor set.</summary>
        public void Reconcile()
        {
            if (_reconciling) return;
            _reconciling = true;
            try
            {
                var monitors = NativeMethods.EnumMonitors();
                var devices = new HashSet<string>(
                    monitors.Select(m => m.Device), StringComparer.OrdinalIgnoreCase);

                // A monitor was disconnected: tear its bar (and appbar) down.
                foreach (var device in _bars.Keys.Where(d => !devices.Contains(d)).ToList())
                {
                    try { _bars[device].Close(); } catch { }
                    _bars.Remove(device);
                }

                foreach (var m in monitors)
                {
                    if (_bars.TryGetValue(m.Device, out var bar))
                    {
                        bar.SetMonitor(m.Handle, m.Bounds.Left, m.Bounds.Top,
                            m.Bounds.Right, m.Bounds.Bottom);
                        bar.ApplyConfig();
                        continue;
                    }

                    var created = new TopBarWindow(m.Device);
                    created.SetMonitor(m.Handle, m.Bounds.Left, m.Bounds.Top,
                        m.Bounds.Right, m.Bounds.Bottom);
                    _bars[m.Device] = created;
                    try
                    {
                        created.Show();
                        ConfigService.Log($"Bar: created on {m.Device} ({m.Bounds.Right - m.Bounds.Left}x{m.Bounds.Bottom - m.Bounds.Top}).");
                    }
                    catch (Exception ex)
                    {
                        _bars.Remove(m.Device);
                        ConfigService.Log($"Bar: failed on {m.Device}: {ex.Message}");
                    }
                }
            }
            finally { _reconciling = false; }
        }

        public void ApplyConfig()
        {
            foreach (var bar in _bars.Values)
            {
                try { bar.ApplyConfig(); } catch { }
            }
        }

        public void Dispose()
        {
            foreach (var bar in _bars.Values)
            {
                try { bar.Close(); } catch { }
            }
            _bars.Clear();
        }
    }
}
