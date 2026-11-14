using System;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.WindowSystem
{
    /// <summary>
    /// Watches for a foreground fullscreen app. Windows' appbar notification is
    /// the fast path; this polled fallback also catches borderless games and
    /// fullscreen windows that do not trigger that notification.
    ///
    /// The service never touches the game window. It only tells the bars to
    /// leave the screen and release their appbar reservation.
    /// </summary>
    public sealed class FullscreenAppService : IDisposable
    {
        private readonly ConfigService _configs;
        private readonly DispatcherTimer _timer;
        private bool _active;
        private bool _disposed;

        public FullscreenAppService(ConfigService configs)
        {
            _configs = configs;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timer.Tick += (_, _) => Refresh();
        }

        public event Action<bool> FullscreenChanged;

        public bool IsFullscreenActive => _active;

        public void Start()
        {
            Refresh();
            _timer.Start();
        }

        /// <summary>
        /// Re-evaluate immediately, including after the setting is changed in
        /// Settings. A disabled setting always restores the bars.
        /// </summary>
        public void Refresh()
        {
            if (_disposed) return;

            bool enabled = _configs.Config.General.HideBarInFullscreen;
            Apply(enabled && IsForegroundFullscreen());
        }

        /// <summary>
        /// Fast path from the appbar ABN_FULLSCREENAPP notification. The timer
        /// still verifies the state on its next tick, so a stale notification
        /// cannot strand the bars hidden.
        /// </summary>
        public void SetShellState(bool active)
        {
            if (_disposed) return;
            if (!_configs.Config.General.HideBarInFullscreen) active = false;
            Apply(active);
        }

        private void Apply(bool active)
        {
            if (_active == active) return;
            _active = active;
            FullscreenChanged?.Invoke(active);
        }

        /// <summary>
        /// True when the foreground window is a fullscreen game/app. Shared by
        /// the bar hider and hover-to-focus so neither service focuses a game
        /// away from the screen.
        /// </summary>
        public static bool IsForegroundFullscreen()
        {
            // Exclusive Direct3D fullscreen is reported by the shell itself.
            // Some games use borderless fullscreen, which the foreground-window
            // check below catches instead.
            try
            {
                if (NativeMethods.SHQueryUserNotificationState(out var state) == 0
                    && state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN)
                    return true;
            }
            catch
            {
                // The shell API is unavailable on unusual shells; the window
                // check below is still useful.
            }

            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return false;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);

            if (!NativeMethods.GetWindowRect(hwnd, out RECT window)) return false;

            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero
                || !NativeMethods.GetMonitorRect(monitor, out RECT bounds, out _, out _))
                return false;

            // Chrome/Edge can keep the window maximized (IsZoomed) while a
            // YouTube/HTML5 element owns the screen. The reliable distinction is
            // the native frame: a normal maximized window retains DLGFRAME or a
            // sizing border, while fullscreen removes both. This is the same
            // check Chromium uses for its own fullscreen mode.
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            bool hasFrame = (style & (NativeMethods.WS_DLGFRAME | NativeMethods.WS_THICKFRAME)) != 0
                || (exStyle & (NativeMethods.WS_EX_WINDOWEDGE | NativeMethods.WS_EX_TOOLWINDOW)) != 0;

            string windowClass = NativeMethods.GetWindowClass(hwnd);
            return FullscreenDetector.IsFullscreenWindow(
                window,
                bounds,
                NativeMethods.IsWindowVisible(hwnd),
                NativeMethods.IsIconic(hwnd),
                NativeMethods.IsZoomed(hwnd),
                pid,
                Environment.ProcessId,
                windowClass,
                hasFrame);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
        }
    }
}
