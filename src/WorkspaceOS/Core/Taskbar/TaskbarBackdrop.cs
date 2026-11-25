using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Themes;

namespace WorkspaceOS.Core.Taskbar
{
    /// <summary>Which taskbar implementation Explorer is running.</summary>
    public enum TaskbarKind
    {
        Unknown,
        /// <summary>Windows 10 taskbar, or a Windows 11 classic taskbar (ExplorerPatcher).</summary>
        Classic,
        /// <summary>Windows 11 22H2+ XAML taskbar: it ignores accent policies.</summary>
        Xaml
    }

    /// <summary>
    /// A TranslucentTB-style taskbar surface for the classic Windows taskbar.
    ///
    /// The shell taskbar is a normal top-level window (<c>Shell_TrayWnd</c>, plus a
    /// <c>Shell_SecondaryTrayWnd</c> per extra monitor), and the compositor lets a
    /// process set its accent policy with <c>SetWindowCompositionAttribute</c>. That
    /// is the same mechanism TranslucentTB uses: no Explorer patching, no injected
    /// DLL, and the change is gone the moment the taskbar window is recreated.
    ///
    /// <see cref="TaskbarBackdropMode.Dynamic"/> follows the foreground: a clear
    /// taskbar on the desktop, tinted acrylic when a window is in front, and a
    /// stronger tint for a maximized window, so the taskbar never competes with
    /// the app it belongs to. The tint comes from the active Omarchy theme, which
    /// is what makes the taskbar read as part of the bar instead of stock Windows.
    ///
    /// Windows 11 22H2 and later draw the taskbar with XAML and ignore the accent
    /// policy entirely; TranslucentTB handles that by injecting a helper into
    /// Explorer through XAML Diagnostics. WorkspaceOS does not inject code into
    /// Explorer. On those builds the honest path is the Windhawk
    /// <c>windows-11-taskbar-styler</c> preset this app already generates, so the
    /// backend detects the taskbar kind and reports it instead of pretending the
    /// accent policy worked.
    /// </summary>
    public static class TaskbarBackdrop
    {
        private const int WCA_ACCENT_POLICY = 19;

        private enum AccentState
        {
            Disabled = 0,
            EnableTransparentGradient = 2,
            EnableBlurBehind = 3,
            EnableAcrylicBlurBehind = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd,
            ref WindowCompositionAttributeData data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowW(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after,
            string className, string windowName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int count);

        private static DispatcherTimer _timer;
        private static string _lastSignature = "";
        private static string _lastMode = "off";
        private static string _lastTint = "";
        private static int _lastWindows;
        private static bool _reportedFailure;
        private static TaskbarKind? _kind;

        /// <summary>Which taskbar Explorer is running (cached; it does not change).</summary>
        public static TaskbarKind Kind => _kind ??= DetectKind();

        /// <summary>Human-readable state for the log, Settings and the diag command.</summary>
        public static string Describe()
        {
            string tint = _lastTint.Length > 0 ? _lastTint : "none";
            if (Kind == TaskbarKind.Xaml)
                return $"taskbar=xaml tint=none windows={_lastWindows} windhawk={WindhawkState()}";
            return $"taskbar={_lastMode} tint={tint} windows={_lastWindows}";
        }

        /// <summary>Applies the configured surface now; safe to call repeatedly.</summary>
        public static bool Apply(AppConfig config, bool force = false)
        {
            var cfg = config?.Taskbar ?? new TaskbarConfig();
            var appearance = config?.Appearance;

            // The XAML taskbar ignores WCA_ACCENT_POLICY. Report it once and let
            // the Windhawk preset (the rice's own route) own the surface; writing
            // the policy here would only pretend to work.
            if (Kind == TaskbarKind.Xaml)
            {
                _lastWindows = EnumerateTaskbars().Count;
                bool hidden = !cfg.EnableAppearance ||
                    string.Equals(cfg.Mode, "Disabled", StringComparison.OrdinalIgnoreCase);
                if (!_reportedFailure)
                {
                    _reportedFailure = true;
                    ConfigService.Log("Taskbar: Windows 11 XAML taskbar detected; it ignores accent " +
                        "policies. Use the Windhawk windows-11-taskbar-styler preset (bar menu → " +
                        "Copy taskbar preset).");
                }
                return hidden;
            }

            if (!cfg.EnableAppearance)
            {
                SetEverywhere(TaskbarBackdropMode.Disabled, cfg, appearance, force);
                return true;
            }

            var foreground = GetForegroundWindow();
            var mode = TaskbarSurface.Resolve(TaskbarSurface.Parse(cfg.Mode),
                IsDesktop(foreground), IsMaximized(foreground));
            return SetEverywhere(mode, cfg, appearance, force);
        }

        /// <summary>
        /// Starts the low-frequency watcher that re-applies on desktop/maximized
        /// changes and after Explorer recreates the taskbar window. Two seconds is
        /// invisible to the eye and costs nothing; it never touches the taskbar when
        /// the resolved state has not changed.
        /// </summary>
        public static void Start(AppConfig config)
        {
            Apply(config, force: true);

            // The XAML taskbar has no settable surface; the preset owns it. A
            // watcher would only re-detect that every two seconds.
            if (Kind == TaskbarKind.Xaml || _timer != null) return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, _) =>
            {
                try { Apply(config); } catch { }
            };
            _timer.Start();
        }

        public static void Stop()
        {
            _timer?.Stop();
            _timer = null;
        }

        /// <summary>Gives the taskbar back to Windows when WorkspaceOS exits.</summary>
        public static void Reset()
        {
            _lastSignature = "";
            foreach (var hwnd in EnumerateTaskbars())
                SetWindow(hwnd, TaskbarBackdropMode.Disabled, "#000000", 0);
            _lastMode = "off";
            _lastTint = "";
            _lastWindows = 0;
        }

        private static bool SetEverywhere(TaskbarBackdropMode mode, TaskbarConfig cfg,
            AppearanceConfig appearance, bool force)
        {
            var tint = Tint(appearance);
            var windows = EnumerateTaskbars();

            string signature = $"{mode}|{tint}|{cfg.Opacity:0.00}|{windows.Count}";
            if (!force && signature == _lastSignature) return true;

            bool allOk = windows.Count > 0;
            foreach (var hwnd in windows)
            {
                if (!SetWindow(hwnd, mode, tint, cfg.Opacity)) allOk = false;
            }

            // Keep the status truthful even when the composer refused the policy.
            _lastSignature = signature;
            _lastMode = mode.ToString().ToLowerInvariant();
            _lastTint = tint;
            _lastWindows = windows.Count;

            if (windows.Count == 0)
            {
                if (!_reportedFailure)
                {
                    _reportedFailure = true;
                    ConfigService.Log("Taskbar: no taskbar window found; appearance left alone.");
                }
            }
            else if (!allOk && !_reportedFailure)
            {
                _reportedFailure = true;
                ConfigService.Log("Taskbar: the compositor refused the accent policy; keeping the stock taskbar.");
            }
            else if (allOk)
            {
                ConfigService.Log($"Taskbar: {_lastMode} {tint} on {windows.Count} window(s).");
            }
            return allOk;
        }

        private static bool SetWindow(IntPtr hwnd, TaskbarBackdropMode mode, string tint, double opacity)
        {
            if (hwnd == IntPtr.Zero) return false;

            AccentState state = mode switch
            {
                TaskbarBackdropMode.Clear => AccentState.EnableTransparentGradient,
                TaskbarBackdropMode.Blur => AccentState.EnableBlurBehind,
                TaskbarBackdropMode.Acrylic => AccentState.EnableAcrylicBlurBehind,
                _ => AccentState.Disabled
            };

            // ABGR is what the compositor stores, matching the accent colour path.
            var (r, g, b) = ThemeColor.Rgb(tint);
            byte alpha = mode switch
            {
                TaskbarBackdropMode.Clear => 0x00,
                TaskbarBackdropMode.Blur => 0x00,
                TaskbarBackdropMode.Acrylic => (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255),
                _ => 0x00
            };

            var policy = new AccentPolicy
            {
                AccentState = (int)state,
                // Acrylic needs the bordered blur mode; clear/blur work with no flags.
                AccentFlags = state == AccentState.EnableAcrylicBlurBehind ? 2 : 0,
                GradientColor = (alpha << 24) | (b << 16) | (g << 8) | r
            };

            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size
                };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        private static List<IntPtr> EnumerateTaskbars()
        {
            var list = new List<IntPtr>();
            var primary = FindWindowW("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero) list.Add(primary);

            IntPtr secondary = IntPtr.Zero;
            while ((secondary = FindWindowExW(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            {
                if (secondary != primary) list.Add(secondary);
            }
            return list;
        }

        private static bool IsDesktop(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return true;
            string name = ClassName(hwnd);
            return name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        }

        private static bool IsMaximized(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try { return IsZoomed(hwnd); } catch { return false; }
        }

        /// <summary>
        /// Explorer loads <c>Taskbar.dll</c> only for the Windows 11 XAML taskbar,
        /// which is exactly how TranslucentTB tells the two implementations apart.
        /// The module list is read once per session; it is stable for the process.
        /// </summary>
        private static TaskbarKind DetectKind()
        {
            try
            {
                foreach (var explorer in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        foreach (ProcessModule module in explorer.Modules)
                        {
                            if (string.Equals(module.ModuleName, "Taskbar.dll",
                                    StringComparison.OrdinalIgnoreCase))
                                return TaskbarKind.Xaml;
                        }
                    }
                    catch { }
                    finally { explorer.Dispose(); }
                }
                return TaskbarKind.Classic;
            }
            catch { return TaskbarKind.Unknown; }
        }

        /// <summary>Whether Windhawk and the taskbar styler are available for the XAML route.</summary>
        private static string WindhawkState()
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!File.Exists(Path.Combine(programFiles, "Windhawk", "windhawk.exe")))
                    return "missing";

                string engine = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Windhawk", "Engine", "Mods");
                bool mod = Directory.Exists(engine) && Directory.GetFiles(engine, "windows-11-taskbar-styler*.dll",
                    SearchOption.AllDirectories).Length > 0;
                if (!mod) return "no-mod";

                // Enabled state is machine-wide in Windhawk 1.x.
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Windhawk\Engine\Mods\windows-11-taskbar-styler");
                bool enabled = key?.GetValue("Disabled") is int disabled && disabled == 0;
                return enabled ? "enabled" : "installed";
            }
            catch { return "unknown"; }
        }

        private static string ClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(64);
            try
            {
                int n = GetClassNameW(hwnd, sb, sb.Capacity);
                return n > 0 ? sb.ToString(0, n) : "";
            }
            catch { return ""; }
        }

        /// <summary>The theme's paper colour, without alpha — the same surface the bar uses.</summary>
        private static string Tint(AppearanceConfig appearance)
        {
            var theme = ThemeLibrary.Find(appearance?.BarTheme);
            if (!string.IsNullOrWhiteSpace(theme?.Background)) return ThemeColor.Normalize(theme.Background);

            var (r, g, b) = ThemeColor.Rgb(appearance?.BarBackground ?? "#121212");
            return ThemeColor.FromRgb(r, g, b);
        }
    }
}
