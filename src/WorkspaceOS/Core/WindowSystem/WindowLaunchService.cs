using System;
using System.Collections.Generic;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.WindowSystem
{
    /// <summary>
    /// Maximizes each newly opened normal application window once, then gets out
    /// of the way. This is not tiling: it only changes the window's native
    /// maximized state, so the user can restore, move and resize it normally.
    /// Fullscreen/borderless games, dialogs, tool windows and already-maximized
    /// windows are left alone.
    /// </summary>
    public sealed class WindowLaunchService : IDisposable
    {
        private sealed class PendingWindow
        {
            public int Attempts;
            public DateTime Due;
        }

        private readonly ConfigService _configs;
        private readonly Dictionary<IntPtr, PendingWindow> _pending = new();
        private readonly HashSet<IntPtr> _handled = new();
        private readonly List<IntPtr> _hooks = new();
        private NativeMethods.WinEventDelegate _winEventProc;
        private DispatcherTimer _timer;
        private bool _disposed;

        public WindowLaunchService(ConfigService configs)
        {
            _configs = configs;
        }

        public void Initialize()
        {
            // Windows that already exist are not "new". In particular, a
            // virtual-desktop switch can re-show/uncloak them; they must keep
            // their current geometry.
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                _handled.Add(hwnd);
                return true;
            }, IntPtr.Zero);

            _winEventProc = OnWinEvent;
            Hook(NativeMethods.EVENT_OBJECT_SHOW);
            Hook(NativeMethods.EVENT_OBJECT_UNCLOAKED);
            Hook(NativeMethods.EVENT_OBJECT_DESTROY);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _timer.Tick += (_, _) => Tick();
        }

        private void Hook(uint ev) =>
            _hooks.Add(NativeMethods.SetWinEventHook(
                ev, ev, IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT));

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint thread, uint time)
        {
            if (_disposed || hwnd == IntPtr.Zero || idObject != 0) return;

            if (eventType == NativeMethods.EVENT_OBJECT_DESTROY)
            {
                _pending.Remove(hwnd);
                _handled.Remove(hwnd);
                return;
            }

            if (!_configs.Config.General.MaximizeNewWindows) return;
            if (_handled.Contains(hwnd) || _pending.ContainsKey(hwnd)) return;

            // Apps often show a window before its title/text is ready. Retry
            // briefly instead of deciding from that first empty frame.
            _pending[hwnd] = new PendingWindow
            {
                Attempts = 0,
                Due = DateTime.UtcNow.AddMilliseconds(350)
            };
            _timer.Start();
        }

        private void Tick()
        {
            if (_disposed) return;

            if (!_configs.Config.General.MaximizeNewWindows)
            {
                _pending.Clear();
                _timer.Stop();
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var pair in new List<KeyValuePair<IntPtr, PendingWindow>>(_pending))
            {
                IntPtr hwnd = pair.Key;
                PendingWindow pending = pair.Value;
                if (now < pending.Due) continue;

                if (TryMaximize(hwnd))
                {
                    _handled.Add(hwnd);
                    _pending.Remove(hwnd);
                    continue;
                }

                pending.Attempts++;
                if (pending.Attempts >= 4)
                {
                    _pending.Remove(hwnd);
                    _handled.Add(hwnd);
                }
                else
                {
                    pending.Due = now.AddMilliseconds(300);
                }
            }

            if (_pending.Count == 0) _timer.Stop();
        }

        /// <summary>True when the window is finished (handled or deliberately skipped).</summary>
        private bool TryMaximize(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindow(hwnd)) return true;
            if (!NativeMethods.IsWindowVisible(hwnd)) return false;

            // A missing title means the app is still constructing the window.
            if (!WindowTracker.IsManageable(hwnd)) return false;

            if (NativeMethods.IsIconic(hwnd)) return false;
            if (NativeMethods.IsZoomed(hwnd)) return true;
            if (WindowTracker.IsHelperWindow(hwnd) || WindowTracker.LooksLikeDialog(hwnd)) return true;

            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            // No caption / maximize box means a borderless or fixed-size window;
            // games in fullscreen mode must keep their own presentation.
            if ((style & (NativeMethods.WS_CAPTION | NativeMethods.WS_MAXIMIZEBOX)) !=
                (NativeMethods.WS_CAPTION | NativeMethods.WS_MAXIMIZEBOX)) return true;

            if (!NativeMethods.GetWindowRect(hwnd, out RECT rect)) return false;
            if (rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0) return false;

            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (NativeMethods.GetMonitorRect(monitor, out RECT bounds, out _, out _) &&
                Covers(rect, bounds))
            {
                // Already fullscreen (or larger than the monitor): leave it alone.
                return true;
            }

            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
            ConfigService.Log($"Window: maximized new {NativeMethods.GetWindowTitle(hwnd)}");
            _handled.Add(hwnd);
            return true;
        }

        private static bool Covers(RECT window, RECT area) =>
            window.Left <= area.Left + 2 &&
            window.Top <= area.Top + 2 &&
            window.Right >= area.Right - 2 &&
            window.Bottom >= area.Bottom - 2;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Stop();
            foreach (var hook in _hooks) NativeMethods.UnhookWinEvent(hook);
            _hooks.Clear();
            _pending.Clear();
        }
    }
}
