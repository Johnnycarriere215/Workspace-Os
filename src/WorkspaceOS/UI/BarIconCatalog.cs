using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Built-in GD-style ship marks for the left side of the bar, plus an
    /// optional custom image path for community-shop icons the user owns.
    /// All built-in shapes are original vector silhouettes, not copied assets.
    /// </summary>
    public static class BarIconCatalog
    {
        public static readonly string[] Names =
        {
            "GD Fish",
            "Comet",
            "Viper",
            "Nova",
            "Prism",
            "Warden",
            "Specter",
            "Ember"
        };

        public static bool IsKnown(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (var item in Names)
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Renders into a 46x24 canvas. The caller normally wraps this in a
        /// Viewbox so the stored BarIconWidth controls the final size.
        /// </summary>
        public static void Render(Canvas canvas, string name, string customPath,
            Color accent, Color ink, Color outline)
        {
            if (canvas == null) return;
            canvas.Children.Clear();

            if (!string.IsNullOrWhiteSpace(customPath) &&
                TryRenderImage(canvas, customPath))
                return;

            switch (FindName(name))
            {
                case "Comet": DrawComet(canvas, accent, ink, outline); break;
                case "Viper": DrawViper(canvas, accent, ink, outline); break;
                case "Nova": DrawNova(canvas, accent, ink, outline); break;
                case "Prism": DrawPrism(canvas, accent, ink, outline); break;
                case "Warden": DrawWarden(canvas, accent, ink, outline); break;
                case "Specter": DrawSpecter(canvas, accent, ink, outline); break;
                case "Ember": DrawEmber(canvas, accent, ink, outline); break;
                default: DrawFish(canvas, accent, ink, outline); break;
            }
        }

        private static string FindName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "GD Fish";
            foreach (var item in Names)
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase)) return item;
            return "GD Fish";
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

                var image = new Image
                {
                    Source = bitmap,
                    Width = 46,
                    Height = 24,
                    Stretch = Stretch.Uniform
                };
                Canvas.SetLeft(image, 0);
                Canvas.SetTop(image, 0);
                canvas.Children.Add(image);
                return true;
            }
            catch
            {
                // A missing/corrupt community image falls back to the selected
                // built-in ship instead of leaving the left dock blank.
                return false;
            }
        }

        // ---- built-in ships ------------------------------------------------

        private static void DrawFish(Canvas c, Color accent, Color ink, Color outline)
        {
            var body = Poly((12, 4), (31, 4), (43, 12), (31, 20), (12, 20), (4, 12));
            var tail = Poly((12, 4), (3, 0), (6, 12), (3, 24), (12, 20));
            AddGlow(c, accent, body);
            AddGlow(c, accent, tail);
            AddShape(c, body, ink, outline, 1.4);
            AddShape(c, tail, ink, outline, 1.4);
            AddEllipse(c, 31.5, 8, 4.2, 4.2, outline, accent, 0.9);
        }

        private static void DrawComet(Canvas c, Color accent, Color ink, Color outline)
        {
            var body = Poly((5, 12), (13, 5), (31, 4), (42, 12), (31, 20), (13, 19));
            var fin = Poly((13, 8), (3, 2), (8, 12), (3, 22), (13, 16));
            AddGlow(c, accent, body);
            AddShape(c, fin, ink, outline, 1.3);
            AddShape(c, body, ink, outline, 1.4);
            AddEllipse(c, 31.5, 8, 4.6, 4.6, outline, accent, 0.9);
            AddOutline(c, accent, Poly((13, 12), (31, 10), (40, 12)), 1.0);
        }

        private static void DrawViper(Canvas c, Color accent, Color ink, Color outline)
        {
            var body = Poly((4, 12), (20, 3), (43, 11), (28, 13), (43, 14), (20, 21));
            var wingTop = Poly((17, 8), (7, 2), (12, 12), (17, 11));
            var wingBottom = Poly((17, 16), (7, 22), (12, 12), (17, 13));
            AddGlow(c, accent, body);
            AddShape(c, wingTop, ink, outline, 1.2);
            AddShape(c, wingBottom, ink, outline, 1.2);
            AddShape(c, body, ink, outline, 1.4);
            AddOutline(c, accent, Poly((11, 12), (20, 8), (28, 12)), 1.0);
            AddEllipse(c, 28, 8, 4.2, 4.2, outline, accent, 0.9);
        }

        private static void DrawNova(Canvas c, Color accent, Color ink, Color outline)
        {
            var body = Poly((5, 10), (16, 4), (31, 4), (43, 12), (31, 20), (16, 20), (5, 14));
            AddGlow(c, accent, body);
            AddShape(c, body, ink, outline, 1.4);
            AddOutline(c, accent, Poly((9, 8), (20, 4), (35, 6), (40, 12)), 1.0);
            AddEllipse(c, 30, 8, 4.4, 4.4, outline, accent, 0.9);
            AddOutline(c, accent, Poly((16, 20), (23, 23), (31, 20)), 1.0);
        }

        private static void DrawPrism(Canvas c, Color accent, Color ink, Color outline)
        {
            var body = Poly((24, 2), (43, 19), (31, 22), (23, 17), (14, 22), (5, 18));
            AddGlow(c, accent, body);
            AddShape(c, body, ink, outline, 1.4);
            AddOutline(c, accent, Poly((24, 6), (34, 17), (18, 17)), 1.1);
            AddEllipse(c, 20, 8, 3.8, 3.8, outline, accent, 0.9);
        }

        private static void DrawWarden(Canvas c, Color accent, Color ink, Color outline)
        {
            var body = Poly((4, 8), (30, 5), (43, 12), (30, 19), (4, 16));
            var wing = Poly((15, 8), (7, 3), (10, 12), (7, 21), (16, 16));
            AddGlow(c, accent, body);
            AddShape(c, wing, ink, outline, 1.2);
            AddShape(c, body, ink, outline, 1.4);
            AddOutline(c, accent, Poly((25, 8), (34, 12), (25, 16)), 1.0);
            AddEllipse(c, 11, 9, 4.2, 4.2, outline, accent, 0.9);
        }

        private static void DrawSpecter(Canvas c, Color accent, Color ink, Color outline)
        {
            var body = Poly((6, 12), (18, 4), (31, 5), (43, 12), (31, 20), (18, 19));
            var tail = Poly((18, 4), (5, 1), (11, 12), (5, 23), (18, 19));
            AddGlow(c, accent, body);
            AddShape(c, tail, ink, outline, 1.3);
            AddShape(c, body, ink, outline, 1.4);
            AddOutline(c, accent, Poly((18, 8), (28, 12), (18, 16)), 1.0);
            AddEllipse(c, 32, 8, 4.2, 4.2, outline, accent, 0.9);
        }

        private static void DrawEmber(Canvas c, Color accent, Color ink, Color outline)
        {
            var body = Poly((5, 18), (8, 8), (18, 2), (20, 9), (30, 3), (44, 12),
                            (30, 18), (22, 14), (16, 22));
            AddGlow(c, accent, body);
            AddShape(c, body, ink, outline, 1.4);
            AddOutline(c, accent, Poly((13, 15), (20, 10), (27, 13), (34, 11)), 1.0);
            AddEllipse(c, 28, 8, 4.2, 4.2, outline, accent, 0.9);
        }

        // ---- drawing helpers ----------------------------------------------

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

        private static void AddShape(Canvas c, Geometry geometry, Color fill, Color outline, double thickness)
        {
            c.Children.Add(new Path
            {
                Data = geometry,
                Fill = Brush(fill),
                Stroke = Brush(outline),
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round
            });
        }

        private static void AddOutline(Canvas c, Color color, Geometry geometry, double thickness)
        {
            c.Children.Add(new Path
            {
                Data = geometry,
                Fill = null,
                Stroke = Brush(color),
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }

        private static void AddGlow(Canvas c, Color accent, Geometry geometry)
        {
            c.Children.Add(new Path
            {
                Data = geometry,
                Fill = null,
                Stroke = Brush(accent),
                StrokeThickness = 3.2,
                StrokeLineJoin = PenLineJoin.Round,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.65,
                    Color = accent
                }
            });
        }

        private static void AddEllipse(Canvas c, double x, double y, double width, double height,
            Color fill, Color outline, double thickness)
        {
            var ellipse = new Ellipse
            {
                Width = width,
                Height = height,
                Fill = Brush(fill),
                Stroke = Brush(outline),
                StrokeThickness = thickness
            };
            Canvas.SetLeft(ellipse, x);
            Canvas.SetTop(ellipse, y);
            c.Children.Add(ellipse);
        }
    }
}
