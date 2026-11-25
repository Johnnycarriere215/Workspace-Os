using System;
using System.IO;

namespace WorkspaceOS.Core.Themes
{
    /// <summary>
    /// The photo each Omarchy theme ships as its background — embedded in the exe
    /// (the installer and the portable zip carry a single executable, so loose
    /// Assets files would never reach them) and extracted to
    /// <c>%LOCALAPPDATA%\WorkspaceOS\wallpapers</c> the first time it is applied.
    /// Applying a theme sets it, so the desktop follows the bar instead of keeping
    /// whatever image was there before.
    /// </summary>
    public static class ThemePhoto
    {
        /// <summary>
        /// The theme's wallpaper file, or null when it ships none. A loose copy
        /// next to the exe wins (development builds, a hand-made portable layout);
        /// otherwise the embedded copy is extracted and returned.
        /// </summary>
        public static string PathFor(string themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName)) return null;
            string name = themeName.Trim();

            var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "wallpapers", name);
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir, "*.jpg");
                if (files.Length > 0) return files[0];
            }

            return Extract(name);
        }

        private static string Extract(string themeName)
        {
            try
            {
                var assembly = typeof(ThemePhoto).Assembly;
                // MSBuild turns folder and file names into resource names: spaces
                // become underscores, so "Harbor Dark" is
                // WorkspaceOS.Assets.wallpapers.Harbor_Dark.wallpaper.jpg. The file
                // name itself varies per theme (5-leaves.jpg, 1-kanagawa.jpg…).
                var invalid = new System.Text.StringBuilder();
                foreach (char c in "wallpapers." + themeName)
                    invalid.Append(char.IsLetterOrDigit(c) || c == '.' ? c : '_');
                // The trailing dot keeps "Harbor" from matching "Harbor_Dark".
                string prefix = assembly.GetName().Name + ".Assets." + invalid + ".";
                string resource = Array.Find(assembly.GetManifestResourceNames(),
                    n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (resource == null) return null;

                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream == null) return null;

                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WorkspaceOS", "wallpapers");
                Directory.CreateDirectory(dir);

                string safe = string.Join("_", themeName.Split(Path.GetInvalidFileNameChars())).Trim();
                string target = Path.Combine(dir, safe + ".jpg");

                var info = new FileInfo(target);
                if (!info.Exists || info.Length != stream.Length)
                {
                    using var file = File.Create(target);
                    stream.CopyTo(file);
                }
                return target;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Sets every monitor to the theme's photo. False when it has none.</summary>
        public static bool Apply(string themeName)
        {
            var path = PathFor(themeName);
            return path != null && File.Exists(path) && UI.DesktopWallpaper.ApplyEverywhere(path);
        }
    }
}
