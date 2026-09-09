using System;
using System.Runtime.InteropServices;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.VirtualDesktops
{
    /// <summary>
    /// Thin, fault-tolerant wrapper over Windows' native Virtual Desktops.
    /// All calls must come from the STA UI thread.
    ///
    /// v4: picks the COM interface set that matches the running OS build —
    /// Windows 10, Windows 11 (21H2–23H2) or Windows 11 24H2/25H2 — because
    /// Microsoft changed the internal interfaces between them (GUIDs on the
    /// Win10→Win11 boundary, and the manager vtable again inside Win11 for
    /// 24H2 while keeping the GUID). If none matches (a future Windows),
    /// switching falls back to simulating Ctrl+Win+Left/Right so the product
    /// still works, just without absolute jumps for other-process windows.
    /// </summary>
    public class VirtualDesktopService
    {
        private IServiceProvider10 _shell;
        private IApplicationViewCollection _views;
        private IVirtualDesktopPinnedApps _pinned;

        // Exactly one of these is non-null after a successful Initialize().
        private IVirtualDesktopManagerInternalWin10 _m10;
        private IVirtualDesktopManagerInternalWin11 _m11;
        private IVirtualDesktopManagerInternalWin11_24H2 _m24;

        /// <summary>Which interface set is in use — for logs and diagnostics.</summary>
        public string Mode { get; private set; } = "fallback";

        public bool Available { get; private set; }
        private int _assumedIndex; // fallback-mode tracker

        public void Initialize()
        {
            Available = false;
            _m10 = null;
            _m11 = null;
            _m24 = null;
            Mode = "fallback";

            try
            {
                _shell = (IServiceProvider10)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(VdGuids.CLSID_ImmersiveShell));

                int build = Environment.OSVersion.Version.Build;
                Guid svc = VdGuids.CLSID_VirtualDesktopManagerInternal;

                if (build >= 26100)
                {
                    // Windows 11 24H2 / 25H2: manager GUID 53F5CA0B with the
                    // SwitchDesktopAndMoveForegroundView slot inserted.
                    Guid iid24 = VdGuids.IID_IVirtualDesktopManagerInternal_Win11;
                    if (TryConnect(
                        () => (IVirtualDesktopManagerInternalWin11_24H2)_shell.QueryService(ref svc, ref iid24),
                        m => { _m24 = m; return m.GetCount(); }, "win11-24h2")) return;
                }
                else if (build >= 22000)
                {
                    // Windows 11 21H2–23H2: manager GUID 53F5CA0B, CreateDesktop
                    // directly after SwitchDesktop (no extra slot).
                    Guid iid11 = VdGuids.IID_IVirtualDesktopManagerInternal_Win11;
                    if (TryConnect(
                        () => (IVirtualDesktopManagerInternalWin11)_shell.QueryService(ref svc, ref iid11),
                        m => { _m11 = m; return m.GetCount(); }, "win11")) return;
                }

                // Windows 10 (and last-resort probe on Windows 11 builds).
                Guid iid10m = VdGuids.IID_IVirtualDesktopManagerInternal_Win10;
                if (TryConnect(
                    () => (IVirtualDesktopManagerInternalWin10)_shell.QueryService(ref svc, ref iid10m),
                    m => { _m10 = m; return m.GetCount(); }, "win10")) return;

                // On Windows 11 the Win10 GUID legitimately doesn't exist — only
                // log a warning when NOTHING connected (handled below).
                ConfigService.Log("Virtual desktop internal API unavailable, using key-simulation fallback.");
            }
            catch (Exception ex)
            {
                ConfigService.Log("Virtual desktop internal API unavailable, using key-simulation fallback: " + ex.Message);
            }
        }

        /// <summary>Queries one candidate interface set; keeps it when the sanity call answers.</summary>
        private bool TryConnect<T>(Func<T> query, Func<T, int> sanity, string mode)
        {
            try
            {
                var candidate = query();
                if (candidate == null) return false;
                int count = sanity(candidate);           // vtable probe: a wrong set throws/fails here
                if (count < 1) return false;

                Guid svcViews = typeof(IApplicationViewCollection).GUID;
                Guid iidViews = typeof(IApplicationViewCollection).GUID;
                _views = (IApplicationViewCollection)_shell.QueryService(ref svcViews, ref iidViews);

                Guid svcPin = VdGuids.CLSID_VirtualDesktopPinnedApps;
                Guid iidPin = typeof(IVirtualDesktopPinnedApps).GUID;
                _pinned = (IVirtualDesktopPinnedApps)_shell.QueryService(ref svcPin, ref iidPin);

                Available = true;
                Mode = mode;
                ConfigService.Log($"Virtual desktop API connected ({mode}, {count} desktops).");
                return true;
            }
            catch (Exception ex)
            {
                ConfigService.Log($"Virtual desktop probe '{mode}' failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>COM pointers die when Explorer restarts; call to reconnect.</summary>
        public void Reconnect() => Initialize();

        private T Guarded<T>(Func<T> action, T fallback)
        {
            if (!Available) return fallback;
            try { return action(); }
            catch (COMException)
            {
                // Explorer likely restarted — reconnect once and retry.
                Reconnect();
                if (!Available) return fallback;
                try { return action(); } catch { return fallback; }
            }
            catch { return fallback; }
        }

        public int GetCount() => Guarded(() =>
            _m24 != null ? _m24.GetCount() : _m11 != null ? _m11.GetCount() : _m10.GetCount(), 1);

        public int GetCurrentIndex()
        {
            return Guarded(() =>
            {
                Guid current = CurrentDesktopRef().GetId();
                var desktops = EnumerateDesktops();
                for (int i = 0; i < desktops.Count; i++)
                    if (desktops[i].GetId() == current) return i;
                return 0;
            }, _assumedIndex);
        }

        // ---- desktop plumbing -------------------------------------------------------

        /// <summary>Version-agnostic handle around the two IVirtualDesktop shapes.</summary>
        private readonly struct DesktopRef
        {
            public readonly IVirtualDesktopWin10 D10;
            public readonly IVirtualDesktopWin11 D11;
            public DesktopRef(IVirtualDesktopWin10 d) { D10 = d; D11 = null; }
            public DesktopRef(IVirtualDesktopWin11 d) { D11 = d; D10 = null; }
            public Guid GetId() => D10 != null ? D10.GetId() : D11.GetId();
            public bool IsViewVisible(IApplicationView v) =>
                D10 != null ? D10.IsViewVisible(v) : D11.IsViewVisible(v);
        }

        private DesktopRef CurrentDesktopRef() =>
            _m24 != null ? new DesktopRef(_m24.GetCurrentDesktop())
          : _m11 != null ? new DesktopRef(_m11.GetCurrentDesktop())
          : new DesktopRef(_m10.GetCurrentDesktop());

        private System.Collections.Generic.List<DesktopRef> EnumerateDesktops()
        {
            var list = new System.Collections.Generic.List<DesktopRef>();
            IObjectArray array = null;
            if (_m24 != null) _m24.GetDesktops(out array);
            else if (_m11 != null) _m11.GetDesktops(out array);
            else if (_m10 != null) _m10.GetDesktops(out array);
            if (array == null) return list;

            array.GetCount(out int count);
            Guid iid10 = VdGuids.IID_IVirtualDesktop_Win10;
            Guid iid11 = VdGuids.IID_IVirtualDesktop_Win11;
            for (int i = 0; i < count; i++)
            {
                if (_m10 != null)
                {
                    array.GetAt(i, ref iid10, out object obj);
                    list.Add(new DesktopRef((IVirtualDesktopWin10)obj));
                }
                else
                {
                    array.GetAt(i, ref iid11, out object obj);
                    list.Add(new DesktopRef((IVirtualDesktopWin11)obj));
                }
            }
            return list;
        }

        private DesktopRef DesktopAt(int index)
        {
            var list = EnumerateDesktops();
            return index >= 0 && index < list.Count ? list[index] : default;
        }

        /// <summary>Creates desktops until at least <paramref name="count"/> exist. Never removes.</summary>
        public void EnsureCount(int count)
        {
            Guarded<object>(() =>
            {
                while (GetCount() < count)
                {
                    if (_m24 != null) _m24.CreateDesktop();
                    else if (_m11 != null) _m11.CreateDesktop();
                    else _m10.CreateDesktop();
                }
                return null;
            }, null);
        }

        public bool SwitchTo(int index)
        {
            if (Available)
            {
                return Guarded(() =>
                {
                    EnsureCount(index + 1);
                    var d = DesktopAt(index);
                    if (d.D10 == null && d.D11 == null) return false;
                    if (_m24 != null) _m24.SwitchDesktop(d.D11);
                    else if (_m11 != null) _m11.SwitchDesktop(d.D11);
                    else _m10.SwitchDesktop(d.D10);
                    return true;
                }, false);
            }

            // Fallback: walk with native Ctrl+Win+Arrow.
            int steps = index - _assumedIndex;
            for (int i = 0; i < Math.Abs(steps); i++)
                SendCtrlWinArrow(steps > 0);
            _assumedIndex = index;
            return true;
        }

        public bool MoveWindowToDesktop(IntPtr hwnd, int index)
        {
            return Guarded(() =>
            {
                EnsureCount(index + 1);
                var d = DesktopAt(index);
                if (d.D10 == null && d.D11 == null) return false;
                if (_views.GetViewForHwnd(hwnd, out var view) != 0 || view == null) return false;
                if (_m24 != null) _m24.MoveViewToDesktop(view, d.D11);
                else if (_m11 != null) _m11.MoveViewToDesktop(view, d.D11);
                else _m10.MoveViewToDesktop(view, d.D10);
                return true;
            }, false);
        }

        /// <summary>Pin a window so it is visible on every desktop ("workspace 0" rules, our own bar/popups).</summary>
        public bool PinWindow(IntPtr hwnd)
        {
            return Guarded(() =>
            {
                if (_views.GetViewForHwnd(hwnd, out var view) != 0 || view == null) return false;
                if (!_pinned.IsViewPinned(view)) _pinned.PinView(view);
                return true;
            }, false);
        }

        /// <summary>True when the window is pinned to all desktops ("Show on all desktops").</summary>
        public bool IsWindowPinned(IntPtr hwnd)
        {
            return Guarded(() =>
            {
                if (_views.GetViewForHwnd(hwnd, out var view) != 0 || view == null) return false;
                return _pinned.IsViewPinned(view);
            }, false);
        }

        /// <summary>
        /// Per-desktop occupancy (index → any unpinned window lives on it), used by the
        /// bar's dot indicators. Pinned views are skipped — they show on every desktop.
        /// Returns all-false when the COM API is unavailable.
        /// </summary>
        public bool[] GetOccupancy()
        {
            var fallback = new bool[Math.Max(1, GetCount())];
            return Guarded(() =>
            {
                var desks = EnumerateDesktops();
                var result = new bool[desks.Count];
                _views.GetViews(out var views);
                views.GetCount(out int viewCount);
                for (int v = 0; v < viewCount; v++)
                {
                    Guid iid = VdGuids.IID_IApplicationView;
                    views.GetAt(v, ref iid, out object obj);
                    var view = (IApplicationView)obj;
                    try { if (_pinned.IsViewPinned(view)) continue; } catch { }
                    for (int i = 0; i < desks.Count; i++)
                    {
                        try { if (desks[i].IsViewVisible(view)) { result[i] = true; break; } }
                        catch { }
                    }
                }
                return result;
            }, fallback);
        }

        public bool IsWindowOnCurrentDesktop(IntPtr hwnd)
        {
            return Guarded(() =>
            {
                if (_views.GetViewForHwnd(hwnd, out var view) != 0 || view == null) return true;
                if (_pinned.IsViewPinned(view)) return true;
                return CurrentDesktopRef().IsViewVisible(view);
            }, true);
        }

        // ---- key-simulation fallback ----

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        private const uint KEYUP = 0x0002;

        private static void SendCtrlWinArrow(bool right)
        {
            byte arrow = right ? (byte)0x27 : (byte)0x25;
            keybd_event(0x11, 0, 0, UIntPtr.Zero);      // Ctrl
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);      // LWin
            keybd_event(arrow, 0, 0, UIntPtr.Zero);
            keybd_event(arrow, 0, KEYUP, UIntPtr.Zero);
            keybd_event(0x5B, 0, KEYUP, UIntPtr.Zero);
            keybd_event(0x11, 0, KEYUP, UIntPtr.Zero);
        }
    }
}
