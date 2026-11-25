using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// The marks the bar can show at its left edge. They are the launcher
    /// identities shipped by Shibumi-Shell itself: its SHIBUMI wordmark, the
    /// Hyprland and Omarchy wordmarks, the Arch lockup, OMACOM, and the Shibumi
    /// Hikiryō mark. The same control is the bar's menu button.
    /// </summary>
    public static class BarIconCatalog
    {
        public static readonly string[] Names =
            { "Shibumi", "Hyprland", "Arch", "Omarchy", "Omacom", "Dragon", "Mark" };

        private const double CanvasHeight = 24;
        private const double Edge = 4;          // breathing room either side of the art

        private static readonly FontFamily MarkFont =
            new("JetBrainsMono Nerd Font, Cascadia Mono, Consolas");

        // Recolouring a PNG is per-pixel work, and every config apply asks for
        // the same asset in the same ink. Keyed by file + RGB; a theme change
        // adds a handful of entries, so a small cap is enough.
        private static readonly Dictionary<(string File, uint Ink), BitmapSource> Tinted = new();

        public static bool IsKnown(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (var item in Names)
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Wordmarks need the wide canvas; glyph marks stay square.</summary>
        public static bool IsWordmark(string name) =>
            Find(name) is "Shibumi" or "Hyprland" or "Arch" or "Omarchy" or "Omacom";

        /// <summary>
        /// Renders into a canvas the caller wraps in a Viewbox. The canvas width
        /// is set to the artwork so a wordmark is never squeezed into a square
        /// icon slot.
        /// </summary>
        public static void Render(Canvas canvas, string name, string customPath,
            Color accent, Color ink, Color outline)
        {
            if (canvas == null) return;
            canvas.Children.Clear();
            canvas.Height = CanvasHeight;

            if (!string.IsNullOrWhiteSpace(customPath) && TryRenderImage(canvas, customPath))
                return;

            switch (Find(name))
            {
                case "Hyprland": DrawAsset(canvas, "bob3.png", ink, 16); break;
                case "Omarchy": DrawAsset(canvas, "bob2.png", ink, 16); break;
                case "Omacom": DrawAsset(canvas, "omacom-text.png", ink, 14); break;
                case "Arch": DrawArchWordmark(canvas, ink); break;
                case "Dragon": DrawGlyph(canvas, "\u2EEF", ink, 20); break;
                case "Mark": DrawShibumiMark(canvas, ink); break;
                default: DrawWordmark(canvas, "SHIBUMI", ink, 14); break;
            }
        }

        /// <summary>Known name in canonical casing; anything else is the Shibumi mark.</summary>
        private static string Find(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                foreach (var item in Names)
                    if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase)) return item;
            }
            return "Shibumi";
        }

        private static bool TryRenderImage(Canvas canvas, string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return false;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                double width = Math.Min(100, CanvasHeight * bitmap.PixelWidth / bitmap.PixelHeight);
                canvas.Width = width + Edge * 2;

                var image = new Image
                {
                    Source = bitmap,
                    Width = width,
                    Height = CanvasHeight,
                    Stretch = Stretch.Uniform
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                Canvas.SetLeft(image, Edge);
                Canvas.SetTop(image, 0);
                canvas.Children.Add(image);
                return true;
            }
            catch { return false; }
        }

        // ---- actual Shibumi launcher assets --------------------------------

        /// <summary>
        /// The default Shibumi launcher is a text wordmark: medium weight at the
        /// bar's cap height. The canvas hugs the text so the wordmark is the
        /// button rather than a mark floating in an empty box.
        /// </summary>
        private static void DrawWordmark(Canvas canvas, string text, Color ink, double size)
        {
            var block = new TextBlock
            {
                Text = text,
                FontFamily = MarkFont,
                FontSize = size,
                FontWeight = FontWeights.Medium,
                Foreground = Brush(ink)
            };
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            canvas.Width = block.DesiredSize.Width + Edge * 2;
            Canvas.SetLeft(block, Edge);
            Canvas.SetTop(block, (canvas.Height - block.DesiredSize.Height) / 2);
            canvas.Children.Add(block);
        }

        private static void DrawArchWordmark(Canvas canvas, Color ink)
        {
            // Arch glyph + ARCH + LINUX, the same lockup as the QML widget.
            const double height = 13;
            var glyph = new TextBlock
            {
                Text = "\uE732",
                FontFamily = MarkFont,
                FontSize = 15,
                Foreground = Brush(ink),
                VerticalAlignment = VerticalAlignment.Center
            };
            glyph.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var arch = LoadTinted("arch-header-arch.png", ink);
            var linux = LoadTinted("arch-header-linux.png", ink);
            double archW = WidthFor(arch, height);
            double linuxW = WidthFor(linux, height);

            double total = glyph.DesiredSize.Width + 3 + archW + (linuxW > 0 ? 3 + linuxW : 0);
            canvas.Width = total + Edge * 2;

            double x = Edge;
            Canvas.SetLeft(glyph, x);
            Canvas.SetTop(glyph, (canvas.Height - glyph.DesiredSize.Height) / 2);
            canvas.Children.Add(glyph);
            x += glyph.DesiredSize.Width + 3;

            if (arch != null)
            {
                DrawBitmapAt(canvas, arch, archW, height, x);
                x += archW + 3;
            }
            if (linux != null) DrawBitmapAt(canvas, linux, linuxW, height, x);
        }

        /// <summary>Draws one Shibumi asset at its natural aspect for the height.</summary>
        private static void DrawAsset(Canvas canvas, string file, Color ink, double height)
        {
            var bitmap = LoadTinted(file, ink);
            double width = WidthFor(bitmap, height);
            canvas.Width = width + Edge * 2;
            if (bitmap == null) return;

            DrawBitmapAt(canvas, bitmap, width, height, Edge);
        }

        private static double WidthFor(BitmapSource bitmap, double height) =>
            bitmap == null ? 0 : height * bitmap.PixelWidth / bitmap.PixelHeight;

        private static void DrawBitmapAt(Canvas canvas, BitmapSource bitmap,
            double width, double height, double x)
        {
            var image = new Image
            {
                Source = bitmap,
                Width = width,
                Height = height,
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(image, x);
            Canvas.SetTop(image, (canvas.Height - height) / 2);
            canvas.Children.Add(image);
        }

        /// <summary>
        /// The Shibumi PNGs are white/orange art with an alpha channel. Recolour
        /// the opaque pixels to the bar's ink so the mark follows the theme.
        /// </summary>
        private static BitmapSource LoadTinted(string file, Color ink)
        {
            uint key = ((uint)ink.R << 16) | ((uint)ink.G << 8) | ink.B;
            if (Tinted.TryGetValue((file, key), out var cached)) return cached;

            try
            {
                var uri = new Uri("pack://application:,,,/Assets/logos/" + file, UriKind.Absolute);
                var source = new BitmapImage();
                source.BeginInit();
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.UriSource = uri;
                source.EndInit();
                source.Freeze();

                int w = source.PixelWidth, h = source.PixelHeight;
                int stride = w * 4;
                var pixels = new byte[stride * h];
                source.CopyPixels(pixels, stride, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] == 0) continue;
                    pixels[i] = ink.B;
                    pixels[i + 1] = ink.G;
                    pixels[i + 2] = ink.R;
                }

                var tinted = BitmapSource.Create(w, h, 96, 96,
                    PixelFormats.Bgra32, null, pixels, stride);
                tinted.Freeze();

                if (Tinted.Count > 64) Tinted.Clear();
                Tinted[(file, key)] = tinted;
                return tinted;
            }
            catch { return null; }
        }

        private static void DrawGlyph(Canvas canvas, string glyph, Color ink, double size)
        {
            var text = new TextBlock
            {
                Text = glyph,
                FontFamily = MarkFont,
                FontSize = size,
                Foreground = Brush(ink)
            };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            canvas.Width = Math.Max(canvas.Width, text.DesiredSize.Width + Edge * 2);
            Canvas.SetLeft(text, (canvas.Width - text.DesiredSize.Width) / 2);
            Canvas.SetTop(text, (canvas.Height - text.DesiredSize.Height) / 2);
            canvas.Children.Add(text);
        }

        /// <summary>The Shibumi mark: an open ring with a small diamond notch.</summary>
        private static void DrawShibumiMark(Canvas canvas, Color ink)
        {
            var figure = new PathFigure
            {
                StartPoint = new Point(18.4, 5.8),
                IsClosed = false,
                IsFilled = false
            };
            figure.Segments.Add(new ArcSegment(
                new Point(18.9, 14.8), new Size(8.8, 8.8), 0,
                true, SweepDirection.Counterclockwise, true));
            var arc = new Path
            {
                Data = new PathGeometry(new[] { figure }),
                Stroke = Brush(ink),
                StrokeThickness = 2.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(arc);

            var diamond = Poly((18.6, 17.1), (20.3, 18.8), (18.6, 20.5), (16.9, 18.8));
            canvas.Children.Add(new Path { Data = diamond, Fill = Brush(ink) });
        }

        // ---- helpers -------------------------------------------------------

        private static Geometry Poly(params (double X, double Y)[] points)
        {
            var figure = new PathFigure
            {
                StartPoint = new Point(points[0].X, points[0].Y),
                IsClosed = true,
                IsFilled = true
            };
            for (int i = 1; i < points.Length; i++)
                figure.Segments.Add(new LineSegment(new Point(points[i].X, points[i].Y), true));
            return new PathGeometry(new[] { figure });
        }

        private static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
