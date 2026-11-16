using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Original 4K wallpaper art for each built-in theme. Everything is drawn
    /// with WPF vectors, so the export is a true 3840x2160 image (not an
    /// upscaled thumbnail), needs no network access and carries no third-party
    /// image licence.
    /// </summary>
    public static class ThemeWallpaper
    {
        public const int Width = 3840;
        public const int Height = 2160;

        public static BitmapSource Render(ThemePreset theme, int width = Width, int height = Height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                Draw(dc, theme, width, height);

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        public static void SavePng(BitmapSource bitmap, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void Draw(DrawingContext dc, ThemePreset t, double w, double h)
        {
            var background = new LinearGradientBrush(t.Deep1, t.Deep2, new Point(0, 0), new Point(0.3, 1));
            background.Freeze();
            dc.DrawRectangle(background, null, new Rect(0, 0, w, h));

            Stars(dc, w, h);
            switch (t.Wallpaper)
            {
                case WallpaperKind.Synth: Synth(dc, t, w, h); break;
                case WallpaperKind.Toxic: Toxic(dc, t, w, h); break;
                case WallpaperKind.Solar: Solar(dc, t, w, h); break;
                case WallpaperKind.Frost: Frost(dc, t, w, h); break;
                case WallpaperKind.Blood: Blood(dc, t, w, h); break;
                default: Void(dc, t, w, h); break;
            }

            Grid(dc, t, w, h);
            Shards(dc, t, w, h);
            Vignette(dc, w, h);
        }

        // ---- compositions -------------------------------------------------

        private static void Void(DrawingContext dc, ThemePreset t, double w, double h)
        {
            Glow(dc, w * 0.5, h * 0.44, w * 0.34, h * 0.34, t.Accent, 70);

            var pen = Pen(t.Accent, 150, Math.Max(1, h * 0.0016));
            dc.DrawGeometry(null, pen, Diamond(w * 0.5, h * 0.42, h * 0.30));
            dc.DrawGeometry(null, Pen(t.Accent2, 80, Math.Max(1, h * 0.0011)),
                Diamond(w * 0.5, h * 0.42, h * 0.19));

            // GD spike row hugging the horizon.
            double horizon = h * 0.66;
            var spikes = new StreamGeometry();
            using (var g = spikes.Open())
            {
                const int n = 26;
                for (int i = 0; i < n; i++)
                {
                    double x0 = w * i / n;
                    double x1 = w * (i + 0.5) / n;
                    double x2 = w * (i + 1) / n;
                    double top = horizon - h * (0.02 + 0.035 * Math.Abs(Math.Sin(i * 1.7)));
                    g.BeginFigure(new Point(x0, horizon), true, true);
                    g.LineTo(new Point(x1, top), true, false);
                    g.LineTo(new Point(x2, horizon), true, false);
                }
            }
            spikes.Freeze();
            dc.DrawGeometry(Brush(t.Accent, 24), Pen(t.Accent, 90, Math.Max(1, h * 0.0008)), spikes);
        }

        private static void Synth(DrawingContext dc, ThemePreset t, double w, double h)
        {
            double cx = w * 0.5, cy = h * 0.47, r = h * 0.24;
            Glow(dc, cx, cy, r * 2.0, r * 1.6, t.Accent, 120);

            var sun = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE1, 0x6B), t.Accent, 90);
            sun.Freeze();
            dc.DrawEllipse(sun, null, new Point(cx, cy), r, r);

            // Retro slice bands across the lower half of the disc.
            for (int i = 1; i <= 8; i++)
            {
                double y = cy + r * (i * 0.11 - 0.02);
                double thick = r * 0.015 + r * 0.02 * i;
                dc.DrawRectangle(Brush(t.Deep2, 255), null, new Rect(cx - r - 2, y, 2 * r + 4, thick));
            }

            Glow(dc, cx, h * 0.8, w * 0.26, h * 0.1, t.Accent2, 70);
        }

        private static void Toxic(DrawingContext dc, ThemePreset t, double w, double h)
        {
            var rnd = new Random(7);
            for (int i = 0; i < 9; i++)
            {
                double cx = rnd.NextDouble() * w;
                double cy = (0.15 + rnd.NextDouble() * 0.7) * h;
                double r = h * (0.04 + rnd.NextDouble() * 0.12);
                Glow(dc, cx, cy, r * 2.2, r * 2.2, t.Accent, 60);
                dc.DrawEllipse(Brush(t.Accent2, 16),
                    Pen(t.Accent, 70, Math.Max(1, h * 0.0010)),
                    new Point(cx, cy), r, r);
            }

            for (int i = 0; i < 260; i++)
            {
                double r = h * (0.0008 + rnd.NextDouble() * 0.0022);
                dc.DrawEllipse(Brush(t.Accent, (byte)(40 + rnd.Next(120))), null,
                    new Point(rnd.NextDouble() * w, rnd.NextDouble() * h), r, r);
            }
        }

        private static void Solar(DrawingContext dc, ThemePreset t, double w, double h)
        {
            var origin = new Point(w * 0.16, h * 0.14);
            Glow(dc, origin.X, origin.Y, w * 0.42, h * 0.42, t.Accent2, 130);

            for (int i = 0; i < 26; i++)
            {
                double angle = i * (Math.PI / 25) - 0.15;
                double len = (0.9 + 0.35 * Math.Sin(i * 1.9)) * h;
                const double spread = 0.016;
                var ray = new StreamGeometry();
                using (var g = ray.Open())
                {
                    g.BeginFigure(origin, true, true);
                    g.LineTo(new Point(origin.X + Math.Cos(angle - spread) * len,
                                        origin.Y + Math.Sin(angle - spread) * len), true, false);
                    g.LineTo(new Point(origin.X + Math.Cos(angle + spread) * len,
                                        origin.Y + Math.Sin(angle + spread) * len), true, false);
                }
                ray.Freeze();
                byte alpha = (byte)(18 + 22 * Math.Abs(Math.Sin(i * 0.8)));
                dc.DrawGeometry(Brush(t.Accent, alpha), null, ray);
            }

            Glow(dc, w * 0.7, h * 0.85, w * 0.4, h * 0.2, t.Accent, 50);
        }

        private static void Frost(DrawingContext dc, ThemePreset t, double w, double h)
        {
            var rnd = new Random(11);
            for (int i = 0; i < 16; i++)
            {
                double x = w * (i + rnd.NextDouble() * 0.7) / 16.0;
                double top = h * (0.3 + rnd.NextDouble() * 0.35);
                double halfBase = w * (0.012 + rnd.NextDouble() * 0.03);
                var shard = new StreamGeometry();
                using (var g = shard.Open())
                {
                    g.BeginFigure(new Point(x, top), true, true);
                    g.LineTo(new Point(x + halfBase, h * 1.02), true, false);
                    g.LineTo(new Point(x - halfBase, h * 1.02), true, false);
                }
                shard.Freeze();
                dc.DrawGeometry(Brush(t.Accent, 18),
                    Pen(t.Accent2, 80, Math.Max(1, h * 0.0010)), shard);
            }

            for (int i = 0; i < 4; i++)
            {
                double y = h * (0.45 + i * 0.12);
                var fog = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255),
                    Color.FromArgb(26, 220, 245, 255), 0);
                fog.Freeze();
                dc.DrawRectangle(fog, null, new Rect(0, y, w, h * 0.05));
            }
        }

        private static void Blood(DrawingContext dc, ThemePreset t, double w, double h)
        {
            double cx = w * 0.72, cy = h * 0.28, r = h * 0.17;
            Glow(dc, cx, cy, r * 2.6, r * 2.6, t.Accent, 110);

            var moon = new RadialGradientBrush(Color.FromRgb(0xFF, 0x8A, 0x6B), Color.FromRgb(0xB3, 0x12, 0x2C));
            moon.Freeze();
            dc.DrawEllipse(moon, null, new Point(cx, cy), r, r);

            var rnd = new Random(3);
            for (int i = 0; i < 12; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2;
                double d = Math.Sqrt(rnd.NextDouble()) * r * 0.78;
                double crater = r * (0.03 + rnd.NextDouble() * 0.07);
                dc.DrawEllipse(Brush(Color.FromRgb(0x6B, 0x0A, 0x18), 90), null,
                    new Point(cx + Math.Cos(a) * d, cy + Math.Sin(a) * d), crater, crater);
            }

            for (int i = 0; i < 3; i++)
            {
                double y = h * (0.55 + i * 0.13);
                var mist = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(70, 0xFF, 0x3B, 0x5C), 0);
                mist.Freeze();
                dc.DrawRectangle(mist, null, new Rect(0, y, w, h * 0.06));
            }
        }

        // ---- shared layers ------------------------------------------------

        private static void Stars(DrawingContext dc, double w, double h)
        {
            var rnd = new Random(2024);
            for (int i = 0; i < 320; i++)
            {
                double r = h * (0.0006 + rnd.NextDouble() * 0.0012);
                dc.DrawEllipse(Brush(Colors.White, (byte)(30 + rnd.Next(140))), null,
                    new Point(rnd.NextDouble() * w, rnd.NextDouble() * h * 0.78), r, r);
            }
        }

        private static void Grid(DrawingContext dc, ThemePreset t, double w, double h)
        {
            double horizon = h * 0.66;
            var pen = Pen(t.Accent, 46, Math.Max(1, h * 0.0007));
            var vanishing = new Point(w * 0.5, horizon);

            for (int i = -14; i <= 14; i++)
                dc.DrawLine(pen, vanishing, new Point(w * 0.5 + i * w * 0.11, h * 1.05));

            for (int i = 1; i <= 16; i++)
            {
                double y = horizon + (h - horizon) * Math.Pow(i / 16.0, 2.3);
                dc.DrawLine(pen, new Point(0, y), new Point(w, y));
            }

            var haze = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0),
                Color.FromArgb(90, t.Accent.R, t.Accent.G, t.Accent.B), 90);
            haze.Freeze();
            dc.DrawRectangle(haze, null, new Rect(0, horizon - h * 0.08, w, h * 0.1));
        }

        private static void Shards(DrawingContext dc, ThemePreset t, double w, double h)
        {
            var rnd = new Random(5150);
            var pen = Pen(t.Accent, 70, Math.Max(1, h * 0.0006));
            for (int i = 0; i < 8; i++)
            {
                double cx = rnd.NextDouble() * w;
                double cy = (0.08 + rnd.NextDouble() * 0.5) * h;
                double s = (0.02 + rnd.NextDouble() * 0.06) * h;

                var shard = new StreamGeometry();
                using (var g = shard.Open())
                {
                    g.BeginFigure(new Point(cx, cy - s), true, true);
                    g.LineTo(new Point(cx + s * 0.62, cy), true, false);
                    g.LineTo(new Point(cx, cy + s), true, false);
                    g.LineTo(new Point(cx - s * 0.62, cy), true, false);
                }
                shard.Freeze();
                dc.DrawGeometry(Brush(t.Accent, 14), pen, shard);
            }
        }

        private static void Vignette(DrawingContext dc, double w, double h)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.46),
                GradientOrigin = new Point(0.5, 0.46),
                RadiusX = 0.78,
                RadiusY = 0.88
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(165, 0, 0, 0), 1));
            brush.Freeze();
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }

        // ---- primitives ---------------------------------------------------

        private static Geometry Diamond(double cx, double cy, double r)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(cx, cy - r), true, true);
                g.LineTo(new Point(cx + r, cy), true, false);
                g.LineTo(new Point(cx, cy + r), true, false);
                g.LineTo(new Point(cx - r, cy), true, false);
            }
            geo.Freeze();
            return geo;
        }

        private static void Glow(DrawingContext dc, double cx, double cy,
            double rx, double ry, Color accent, byte alpha)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, accent.R, accent.G, accent.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha / 3), accent.R, accent.G, accent.B), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(cx, cy), rx, ry);
        }

        private static SolidColorBrush Brush(Color color, byte alpha)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private static Pen Pen(Color color, byte alpha, double thickness)
        {
            var pen = new Pen(Brush(color, alpha), thickness);
            pen.Freeze();
            return pen;
        }
    }
}
