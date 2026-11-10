using System;
using System.Collections.Generic;
using System.Linq;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.WindowSystem;

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
        private FullscreenAppService _fullscreen;
        private bool _reconciling;

        public int Count => _bars.Count;

        public void Start()
        {
            Reconcile();
            _fullscreen = new FullscreenAppService(App.Configs);
            _fullscreen.FullscreenChanged += OnFullscreenChanged;
            _fullscreen.Start();
        }

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
                        bar.SetGameMode(IsFullscreenActive);
                        continue;
                    }

                    var created = new TopBarWindow(m.Device);
                    created.SetMonitor(m.Handle, m.Bounds.Left, m.Bounds.Top,
                        m.Bounds.Right, m.Bounds.Bottom);
                    _bars[m.Device] = created;
                    try
                    {
                        created.Show();
                        created.SetGameMode(IsFullscreenActive);
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

        private bool IsFullscreenActive => _fullscreen?.IsFullscreenActive == true;

        /// <summary>
        /// Called by the appbar callback for ABN_FULLSCREENAPP. The polling
        /// service verifies and repairs this state on its next tick.
        /// </summary>
        public void ReportShellFullscreen(bool active)
        {
            _fullscreen?.SetShellState(active);
        }

        private void OnFullscreenChanged(bool active)
        {
            foreach (var bar in _bars.Values)
            {
                try { bar.SetGameMode(active); } catch { }
            }

            ConfigService.Log(active
                ? "Bar: fullscreen app detected; hiding bars and releasing appbar space."
                : "Bar: fullscreen app ended; restoring bars.");
        }

        public void ApplyConfig()
        {
            foreach (var bar in _bars.Values)
            {
                try { bar.ApplyConfig(); } catch { }
            }

            _fullscreen?.Refresh();
        }

        public void Dispose()
        {
            _fullscreen?.Dispose();
            _fullscreen = null;

            foreach (var bar in _bars.Values)
            {
                try { bar.Close(); } catch { }
            }
            _bars.Clear();
        }
    }
}
