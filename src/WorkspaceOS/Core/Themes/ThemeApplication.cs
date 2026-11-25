using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Taskbar;

namespace WorkspaceOS.Core.Themes
{
    /// <summary>
    /// Applies one theme to the whole PC in a single transaction:
    ///
    /// <list type="bullet">
    /// <item>the theme's wallpaper, once, for every monitor and every virtual
    /// desktop (a stable copy in <c>%LOCALAPPDATA%\WorkspaceOS\wallpapers</c> so
    /// Windows never holds a lock on the build output);</item>
    /// <item>the Windows accent colour and light/dark app mode, so the Start menu,
    /// taskbar flyouts and window chrome follow the bar;</item>
    /// <item>the taskbar surface (clear/blur/acrylic) in the theme's paper colour;</item>
    /// <item>the Windhawk taskbar preset text for the pill geometry.</item>
    /// </list>
    ///
    /// Every step is logged separately. A theme change that cannot reach one of
    /// them still applies the rest, and the caller can re-run it (startup,
    /// Explorer restart, session unlock) to repair anything Windows reset.
    /// </summary>
    public static class ThemeApplication
    {
        private static readonly object Gate = new();
        private static string _lastAccent = "";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam,
            string lParam, uint flags, uint timeout, out IntPtr result);

        /// <summary>
        /// Applies the active theme. With <paramref name="forceWallpaper"/> false an
        /// already-correct wallpaper is left alone (startup stays quiet); the accent,
        /// dark mode and taskbar surface are re-asserted either way, because Windows
        /// resets them on updates, Explorer restarts and monitor changes.
        /// </summary>
        public static void Apply(AppConfig config, bool forceWallpaper)
        {
            if (config?.Appearance == null) return;

            lock (Gate)
            {
                var appearance = config.Appearance;
                var theme = ThemeLibrary.Find(appearance.BarTheme);

                ApplyWindowsAppearance(appearance, theme);

                if (appearance.ApplyThemeWallpaper)
                    ApplyThemeWallpaper(appearance, theme, forceWallpaper);

                try { TaskbarBackdrop.Apply(config); } catch { }
                // Stages the preset and, when it differs from what the mod is
                // running, pushes it into Windhawk (directly when elevated, else
                // through the task the setup script installed).
                // Both Windhawk glass presets are staged and applied together,
                // so a theme change costs one elevated run and one re-init of
                // the open Explorer windows.
                try { WindhawkGlass.Sync(appearance); } catch { }
            }
        }

        /// <summary>True when the shell is already showing this theme's wallpaper.</summary>
        public static bool WallpaperIsCurrent(string themeName)
        {
            string wanted = CachedPath(themeName);
            if (wanted == null || !File.Exists(wanted)) return false;

            try
            {
                string current = UI.DesktopWallpaper.Current();
                if (!string.IsNullOrWhiteSpace(current)
                    && string.Equals(Path.GetFullPath(current), Path.GetFullPath(wanted),
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            string registered = UI.WallpaperTheme.CurrentWallpaperPath();
            return !string.IsNullOrWhiteSpace(registered)
                && string.Equals(Path.GetFullPath(registered), Path.GetFullPath(wanted),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyThemeWallpaper(AppearanceConfig appearance,
            OmarchyTheme theme, bool force)
        {
            string name = theme?.Name ?? appearance.BarTheme;
            if (string.IsNullOrWhiteSpace(name)) return;

            string source = ThemePhoto.PathFor(name);
            if (source == null)
            {
                // A drop-in theme with no artwork keeps the current background
                // instead of being repainted with an unrelated image.
                ConfigService.Log($"Theme: '{name}' has no wallpaper file; desktop left as it is.");
                return;
            }

            if (!force && WallpaperIsCurrent(name)) return;

            string target = CacheWallpaper(source, name) ?? source;
            bool ok = UI.DesktopWallpaper.ApplyEverywhere(target, out int monitors);
            if (!ok)
            {
                ConfigService.Log($"Theme: could not set the wallpaper for '{name}' — the session may be locked.");
                return;
            }

            ConfigService.Log(monitors > 1
                ? $"Theme: wallpaper '{name}' applied to {monitors} monitors and every desktop ({target})."
                : $"Theme: wallpaper '{name}' applied to every desktop ({target}).");

            if (monitors == 0)
                ConfigService.Log("Theme: the shell has not reported the new wallpaper yet; it will be re-applied on the next check.");
        }

        /// <summary>Stable per-theme path inside the app's wallpaper cache.</summary>
        private static string CachedPath(string themeName)
        {
            string source = ThemePhoto.PathFor(themeName);
            if (source == null) return null;

            string safe = SafeName(themeName);
            string extension = Path.GetExtension(source);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
            return Path.Combine(CacheFolder, safe + extension);
        }

        /// <summary>
        /// Copies the image into a stable cache folder. Windows keeps the wallpaper
        /// file open while it is in use, so pointing it at the app's build output
        /// would make the next publish or theme switch fail to overwrite it.
        /// </summary>
        private static string CacheWallpaper(string source, string themeName)
        {
            try
            {
                Directory.CreateDirectory(CacheFolder);
                string target = CachedPath(themeName);
                if (target == null) return null;

                // Embedded wallpapers are already extracted to this exact path.
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target),
                        StringComparison.OrdinalIgnoreCase))
                    return target;

                var sourceInfo = new FileInfo(source);
                var targetInfo = new FileInfo(target);
                if (!targetInfo.Exists || targetInfo.Length != sourceInfo.Length
                    || targetInfo.LastWriteTimeUtc < sourceInfo.LastWriteTimeUtc)
                {
                    File.Copy(source, target, true);
                }
                return target;
            }
            catch (Exception ex)
            {
                ConfigService.Log("Theme: wallpaper cache failed (" + ex.Message + "); using the original file.");
                return null;
            }
        }

        private static string CacheFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkspaceOS", "wallpapers");

        private static string SafeName(string themeName) =>
            string.Join("_", (themeName ?? "").Split(Path.GetInvalidFileNameChars())).Trim();

        /// <summary>
        /// Windows' own colours. The accent goes in as ABGR (what DWM stores), the
        /// app mode follows the theme's lightness, and the change is broadcast so
        /// the shell rebuilds its brushes now instead of after a restart.
        /// </summary>
        private static void ApplyWindowsAppearance(AppearanceConfig appearance, OmarchyTheme theme)
        {
            string accentHex = theme?.Accent ?? appearance.ActiveWorkspaceBackground;
            if (!TryParse(accentHex, out byte r, out byte g, out byte b))
            {
                ConfigService.Log("Theme: Windows accent skipped — no valid accent colour.");
                return;
            }

            bool light = theme?.IsLight == true;
            string key = $"{r:X2}{g:X2}{b:X2}";
            if (_lastAccent == key)
            {
                WritePersonalize(light);
                return;
            }

            try
            {
                uint abgr = 0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | r;

                using (var dwm = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    dwm?.SetValue("AccentColor", unchecked((int)abgr), RegistryValueKind.DWord);
                    dwm?.SetValue("ColorizationColor", unchecked((int)abgr), RegistryValueKind.DWord);
                    dwm?.SetValue("ColorizationAfterglow", unchecked((int)abgr), RegistryValueKind.DWord);
                    dwm?.SetValue("ColorizationColorBalance", 100, RegistryValueKind.DWord);
                }

                using (var explorer = Registry.CurrentUser.CreateSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    explorer?.SetValue("AccentColorMenu", unchecked((int)abgr), RegistryValueKind.DWord);
                    explorer?.SetValue("StartColorMenu", unchecked((int)abgr), RegistryValueKind.DWord);
                }

                WritePersonalize(light);
                Broadcast("ImmersiveColorSet");
                _lastAccent = key;
                ConfigService.Log($"Theme: Windows accent set to #{key} ({(light ? "light" : "dark")} app mode).");
            }
            catch (Exception ex)
            {
                ConfigService.Log("Theme: Windows accent failed (" + ex.Message + ").");
            }
        }

        private static void WritePersonalize(bool light)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key == null) return;
                // Apps follow the theme; the taskbar stays dark on dark themes so
                // the bar and the tray do not disagree.
                key.SetValue("AppsUseLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SystemUsesLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static void Broadcast(string setting)
        {
            try
            {
                SendMessageTimeout((IntPtr)0xffff, 0x001A /* WM_SETTINGCHANGE */,
                    IntPtr.Zero, setting, 0x0002, 1000, out _);
            }
            catch { }
        }

        private static bool TryParse(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string value = hex.TrimStart('#');
            if (value.Length == 8) value = value.Substring(2);   // drop AARRGGBB alpha
            if (value.Length != 6) return false;

            var sb = new StringBuilder("#");
            sb.Append(value);
            try
            {
                var color = (System.Windows.Media.Color)
                    System.Windows.Media.ColorConverter.ConvertFromString(sb.ToString());
                r = color.R; g = color.G; b = color.B;
                return true;
            }
            catch { return false; }
        }
    }
}
