using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Sets the desktop background for every monitor in one operation.
    ///
    /// The shell's <c>IDesktopWallpaper</c> service is the owner of the desktop
    /// background: a single <c>SetWallpaper(null, path)</c> assigns the image to
    /// every monitor and every Windows virtual desktop. Windows 10/11 then draw
    /// that one assignment on all desktops, which is why there is no per-desktop
    /// loop here.
    ///
    /// <c>SystemParametersInfo(SPI_SETDESKWALLPAPER)</c> is only the fallback for
    /// builds without the service. Using both in sequence (as an earlier version
    /// did) applied the same image twice, which showed up as a flicker and, when
    /// Explorer had a per-monitor override, as one screen keeping the old picture.
    /// </summary>
    public static class DesktopWallpaper
    {
        private const int SPI_SETDESKWALLPAPER = 20;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfoW(int action, int param, string value, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam,
            string lParam, uint flags, uint timeout, out IntPtr result);

        private enum DesktopWallpaperPosition
        {
            Center = 0,
            Tile = 1,
            Stretch = 2,
            Fit = 3,
            Fill = 4,
            Span = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId,
                              [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            void SetPosition(DesktopWallpaperPosition position);
            DesktopWallpaperPosition GetPosition();
            void SetSlideshow(IntPtr items);
            IntPtr GetSlideshow();
            void SetSlideshowOptions(int options, uint slideshowTick);
            void GetSlideshowOptions(out int options, out uint slideshowTick);
            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorId, int direction);
            int GetStatus();
            void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
        }

        [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaperCoClass
        {
        }

        /// <summary>
        /// Applies <paramref name="path"/> to every monitor in fill mode. Returns
        /// true when the shell accepted it; <paramref name="monitors"/> is the
        /// number of monitors the shell reports showing the image.
        /// </summary>
        public static bool ApplyEverywhere(string path) => ApplyEverywhere(path, out _);

        public static bool ApplyEverywhere(string path, out int monitors)
        {
            monitors = 0;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            WriteStyle(path);

            if (TrySetViaShell(path))
            {
                // Windows 11 can keep a wallpaper per virtual desktop, so the
                // shell's global assignment is not enough on its own: hand the
                // image to every native desktop as well. The virtual-desktop COM
                // objects are apartment-bound, so this must run on the UI thread
                // even when the caller is a worker (startup, theme timers).
                ApplyToEveryDesktop(path);

                // The shell applies the image asynchronously: a read immediately
                // after SetWallpaper can still see the old path. A short bounded
                // retry reports what actually landed instead of a false "not yet".
                monitors = WaitForMonitors(path);
                return true;
            }

            // Windows 7/8 and a broken shell service: the documented SPIs still
            // cover every virtual desktop, they just cannot address monitors.
            bool spi;
            try
            {
                spi = SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, path,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            catch { spi = false; }

            if (spi) Broadcast();
            return spi;
        }

        /// <summary>Hands the image to every native desktop on the UI thread.</summary>
        private static void ApplyToEveryDesktop(string path)
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app == null) return;
                if (app.Dispatcher.CheckAccess())
                    App.Workspaces?.ApplyWallpaperToAllDesktops(path);
                else
                    app.Dispatcher.Invoke(() => App.Workspaces?.ApplyWallpaperToAllDesktops(path));
            }
            catch { }
        }

        private static int WaitForMonitors(string path)
        {
            // Wait for every monitor the shell knows about, not just the first:
            // SetWallpaper is asynchronous per monitor, so a single early match
            // would under-report a multi-monitor apply.
            int expected = MonitorCount();
            int count = 0;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                count = CountMatches(path);
                if (count > 0 && (expected <= 0 || count >= expected)) return count;
                if (attempt < 7) Thread.Sleep(100);
            }
            return count;
        }

        private static int MonitorCount()
        {
            try
            {
                var service = (IDesktopWallpaper)new DesktopWallpaperCoClass();
                try { return (int)service.GetMonitorDevicePathCount(); }
                finally { Marshal.ReleaseComObject(service); }
            }
            catch { return 0; }
        }

        /// <summary>How many monitors currently show <paramref name="path"/>.</summary>
        public static int CountMatches(string path)
        {
            try
            {
                var service = (IDesktopWallpaper)new DesktopWallpaperCoClass();
                try
                {
                    uint count = service.GetMonitorDevicePathCount();
                    int matches = 0;
                    for (uint i = 0; i < count; i++)
                    {
                        string id = service.GetMonitorDevicePathAt(i);
                        string current = service.GetWallpaper(id);
                        if (!string.IsNullOrWhiteSpace(current)
                            && string.Equals(Path.GetFullPath(current), Path.GetFullPath(path),
                                StringComparison.OrdinalIgnoreCase))
                            matches++;
                    }
                    return matches;
                }
                finally { Marshal.ReleaseComObject(service); }
            }
            catch { return 0; }
        }

        /// <summary>The image the shell reports for all monitors, if any.</summary>
        public static string Current()
        {
            try
            {
                var service = (IDesktopWallpaper)new DesktopWallpaperCoClass();
                try { return service.GetWallpaper(null); }
                finally { Marshal.ReleaseComObject(service); }
            }
            catch { return null; }
        }

        private static bool TrySetViaShell(string path)
        {
            try
            {
                var service = (IDesktopWallpaper)new DesktopWallpaperCoClass();
                try
                {
                    // A null monitor id means "every monitor" to the shell service,
                    // which also covers every virtual desktop.
                    service.SetWallpaper(null, path);
                    service.SetPosition(DesktopWallpaperPosition.Fill);
                    return true;
                }
                finally { Marshal.ReleaseComObject(service); }
            }
            catch { return false; }
        }

        private static void WriteStyle(string path)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                key?.SetValue("WallpaperStyle", "10");   // fill
                key?.SetValue("TileWallpaper", "0");
                key?.SetValue("WallPaper", path);
            }
            catch { }
        }

        private static void Broadcast()
        {
            try
            {
                SendMessageTimeout((IntPtr)0xffff, 0x001A /* WM_SETTINGCHANGE */,
                    IntPtr.Zero, "DeskWallpaper", 0x0002 /* SMTO_ABORTIFHUNG */, 1000, out _);
            }
            catch { }
        }
    }
}
