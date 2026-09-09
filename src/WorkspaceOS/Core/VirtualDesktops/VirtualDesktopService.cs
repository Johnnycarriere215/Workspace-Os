using System;
using System.Runtime.InteropServices;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.VirtualDesktops
{
    /// <summary>
    /// Thin, fault-tolerant wrapper over Windows' native Virtual Desktops.
    /// All calls must come from the STA UI thread.
    ///
    /// If the internal COM interfaces are unavailable (future Windows build),
    /// switching falls back to simulating Ctrl+Win+Left/Right so the product
    /// still works, just without absolute jumps for other-process windows.
    /// </summary>
    public class VirtualDesktopService
    {
        private IServiceProvider10 _shell;
        private IVirtualDesktopManagerInternal _internal;
        private IApplicationViewCollection _views;
        private IVirtualDesktopPinnedApps _pinned;

        public bool Available { get; private set; }
        private int _assumedIndex; // fallback-mode tracker

        public void Initialize()
        {
            try
            {
                _shell = (IServiceProvider10)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(VdGuids.CLSID_ImmersiveShell));

                Guid svc = VdGuids.CLSID_VirtualDesktopManagerInternal;
                Guid iid = typeof(IVirtualDesktopManagerInternal).GUID;
                _internal = (IVirtualDesktopManagerInternal)_shell.QueryService(ref svc, ref iid);

                Guid svcViews = typeof(IApplicationViewCollection).GUID;
                Guid iidViews = typeof(IApplicationViewCollection).GUID;
                _views = (IApplicationViewCollection)_shell.QueryService(ref svcViews, ref iidViews);

                Guid svcPin = VdGuids.CLSID_VirtualDesktopPinnedApps;
                Guid iidPin = typeof(IVirtualDesktopPinnedApps).GUID;
                _pinned = (IVirtualDesktopPinnedApps)_shell.QueryService(ref svcPin, ref iidPin);

                // sanity call
                _internal.GetCount();
                Available = true;
                ConfigService.Log($"Virtual desktop API connected ({_internal.GetCount()} desktops).");
            }
            catch (Exception ex)
            {
                Available = false;
                ConfigService.Log("Virtual desktop internal API unavailable, using key-simulation fallback: " + ex.Message);
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

        public int GetCount() => Guarded(() => _internal.GetCount(), 1);

        public int GetCurrentIndex()
        {
            return Guarded(() =>
            {
                var current = _internal.GetCurrentDesktop();
                Guid id = current.GetId();
                _internal.GetDesktops(out var array);
                array.GetCount(out int count);
                for (int i = 0; i < count; i++)
                {
                    Guid iid = VdGuids.IID_IVirtualDesktop;
                    array.GetAt(i, ref iid, out object obj);
                    if (((IVirtualDesktop)obj).GetId() == id) return i;
                }
                return 0;
            }, _assumedIndex);
        }

        private IVirtualDesktop GetDesktopAt(int index)
        {
            _internal.GetDesktops(out var array);
            array.GetCount(out int count);
            if (index < 0 || index >= count) return null;
            Guid iid = VdGuids.IID_IVirtualDesktop;
            array.GetAt(index, ref iid, out object obj);
            return (IVirtualDesktop)obj;
        }

        /// <summary>Creates desktops until at least <paramref name="count"/> exist. Never removes.</summary>
        public void EnsureCount(int count)
        {
            Guarded<object>(() =>
            {
                while (_internal.GetCount() < count) _internal.CreateDesktop();
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
                    var d = GetDesktopAt(index);
                    if (d == null) return false;
                    _internal.SwitchDesktop(d);
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
                var d = GetDesktopAt(index);
                if (d == null) return false;
                if (_views.GetViewForHwnd(hwnd, out var view) != 0 || view == null) return false;
                _internal.MoveViewToDesktop(view, d);
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
                _internal.GetDesktops(out var desktops);
                desktops.GetCount(out int deskCount);
                var desks = new IVirtualDesktop[deskCount];
                for (int i = 0; i < deskCount; i++)
                {
                    Guid iid = VdGuids.IID_IVirtualDesktop;
                    desktops.GetAt(i, ref iid, out object obj);
                    desks[i] = (IVirtualDesktop)obj;
                }

                var result = new bool[deskCount];
                _views.GetViews(out var views);
                views.GetCount(out int viewCount);
                for (int v = 0; v < viewCount; v++)
                {
                    Guid iid = VdGuids.IID_IApplicationView;
                    views.GetAt(v, ref iid, out object obj);
                    var view = (IApplicationView)obj;
                    try { if (_pinned.IsViewPinned(view)) continue; } catch { }
                    for (int i = 0; i < deskCount; i++)
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
                return _internal.GetCurrentDesktop().IsViewVisible(view);
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
