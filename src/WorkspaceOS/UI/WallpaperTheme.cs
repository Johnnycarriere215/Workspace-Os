using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WorkspaceOS.UI
{
    /// <summary>Nine bar colours derived from an image.</summary>
    public sealed class BarPalette
    {
        public string BarBackground { get; init; }
        public string BarForeground { get; init; }
        public string InactiveText { get; init; }
        public string ActiveBackground { get; init; }
        public string ActiveForeground { get; init; }
        public string ModuleLabel { get; init; }
        public string ModuleBackground { get; init; }
        public string ModuleBorder { get; init; }
        public string Separator { get; init; }
    }

    /// <summary>
    /// Reads the user's wallpaper, sets a new one, and builds a bar palette that
    /// matches any image: the bar surface takes the wallpaper's darkest hue, the
    /// active slot takes its most vivid one.
    /// </summary>
    public static class WallpaperTheme
    {
        public static string CacheFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkspaceOS", "wallpapers");

        /// <summary>Path Windows is currently using as the desktop wallpaper.</summary>
        public static string CurrentWallpaperPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var path = key?.GetValue("WallPaper") as string;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
            }
            catch { }

            // Slideshow / theme wallpapers are transcoded into this file.
            var transcoded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
            return File.Exists(transcoded) ? transcoded : null;
        }

        public static BitmapSource Load(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        /// Sets the wallpaper on every monitor through the same single shell call
        /// as the theme transaction, so a photo can never reach just one screen.
        /// </summary>
        public static bool Apply(string path) => DesktopWallpaper.ApplyEverywhere(path);

        public static BarPalette Derive(ThemePreset theme) => new BarPalette
        {
            BarBackground = theme.BarBackground,
            BarForeground = theme.BarForeground,
            InactiveText = theme.AccentColor,
            ActiveBackground = theme.ActiveWorkspaceBackground,
            ActiveForeground = theme.ActiveWorkspaceForeground,
            ModuleLabel = theme.ModuleLabelColor,
            ModuleBackground = theme.ModuleBackground,
            ModuleBorder = theme.ModuleBorder,
            Separator = theme.SeparatorColor
        };

        /// <summary>
        /// Samples a downscaled copy of the image and derives a readable palette:
        /// darkest hue for the bar, most vivid hue for the active slot.
        /// </summary>
        public static BarPalette Derive(BitmapSource source)
        {
            const int w = 48, h = 27;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawImage(source, new Rect(0, 0, w, h));

            var scaled = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            scaled.Render(visual);
            scaled.Freeze();

            var pixels = new byte[w * h * 4];
            scaled.CopyPixels(pixels, w * 4, 0);

            var hueBins = new double[36];
            double vividScore = -1, vividHue = 200, vividSat = 0;
            double darkSumR = 0, darkSumG = 0, darkSumB = 0, darkWeight = 0;
            double lightSumR = 0, lightSumG = 0, lightSumB = 0, lightWeight = 0;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                double b = pixels[i] / 255.0, g = pixels[i + 1] / 255.0, r = pixels[i + 2] / 255.0;
                RgbToHsv(r, g, b, out double hue, out double sat, out double val);

                if (sat > 0.22 && val > 0.18)
                {
                    int bin = (int)Math.Round(hue / 10.0) % 36;
                    hueBins[bin] += sat * val + 0.05;
                }

                double score = sat * val;
                if (score > vividScore)
                {
                    vividScore = score;
                    vividHue = hue;
                    vividSat = sat;
                }

                if (val < 0.18) { darkSumR += r; darkSumG += g; darkSumB += b; darkWeight++; }
                if (val > 0.88) { lightSumR += r; lightSumG += g; lightSumB += b; lightWeight++; }
            }

            int bestBin = 0;
            for (int i = 1; i < hueBins.Length; i++)
                if (hueBins[i] > hueBins[bestBin]) bestBin = i;

            double baseHue = hueBins[bestBin] > 0 ? bestBin * 10.0 : vividHue;
            double accentHue = vividScore > 0.05 ? vividHue : baseHue;
            bool monochrome = vividSat < 0.06;

            if (darkWeight > 4)
                RgbToHsv(darkSumR / darkWeight, darkSumG / darkWeight, darkSumB / darkWeight,
                    out baseHue, out _, out _);
            if (lightWeight > 4)
            {
                RgbToHsv(lightSumR / lightWeight, lightSumG / lightWeight, lightSumB / lightWeight,
                    out double lightHue, out _, out _);
                if (hueBins[bestBin] <= 0) baseHue = lightHue;
            }

            if (monochrome) accentHue = baseHue;

            return new BarPalette
            {
                BarBackground = Hex(Hsv(baseHue, 0.55, 0.055)),
                BarForeground = Hex(Hsv(baseHue, 0.06, 0.95)),
                InactiveText = Hex(Hsv(accentHue, 0.32, 0.62)),
                ActiveBackground = Hex(Hsv(accentHue, monochrome ? 0.04 : 0.80, 0.93)),
                ActiveForeground = Hex(Hsv(accentHue, monochrome ? 0.05 : 0.60, 0.07)),
                ModuleLabel = Hex(Hsv(accentHue, monochrome ? 0.08 : 0.55, 0.86)),
                ModuleBackground = Hex(Hsv(baseHue, 0.45, 0.10)),
                ModuleBorder = Hex(Hsv(accentHue, 0.45, 0.20)),
                Separator = Hex(Hsv(accentHue, 0.45, 0.20))
            };
        }

        // ---- colour maths -------------------------------------------------

        private static string Hex(Color c) => $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";

        private static Color Hsv(double hue, double sat, double val)
        {
            hue = ((hue % 360) + 360) % 360;
            double c = val * sat;
            double x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
            double m = val - c;
            double r, g, b;
            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb(
                (byte)Math.Round(Math.Clamp(r + m, 0, 1) * 255),
                (byte)Math.Round(Math.Clamp(g + m, 0, 1) * 255),
                (byte)Math.Round(Math.Clamp(b + m, 0, 1) * 255));
        }

        private static void RgbToHsv(double r, double g, double b,
            out double hue, out double sat, out double val)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            hue = 0;
            if (delta > 0.00001)
            {
                if (max == r) hue = 60 * (((g - b) / delta) % 6);
                else if (max == g) hue = 60 * ((b - r) / delta + 2);
                else hue = 60 * ((r - g) / delta + 4);
            }
            if (hue < 0) hue += 360;

            sat = max <= 0.00001 ? 0 : delta / max;
            val = max;
        }
    }
}
