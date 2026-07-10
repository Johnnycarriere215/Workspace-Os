using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WorkspaceOS.Core.Config;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Region screenshot with annotation. Flow:
    /// 1. Capture whole virtual screen to a bitmap.
    /// 2. Show it fullscreen dimmed; user rubber-bands a region (Esc cancels).
    /// 3. Annotation mode: draw with mouse (pen) or R for rectangles.
    /// 4. Enter/Ctrl+S saves PNG to Pictures\WorkspaceOS and copies to clipboard.
    /// </summary>
    public class ScreenshotWindow : Window
    {
        private readonly DrawingBitmap _screen;
        private readonly BitmapSource _screenSource;
        private readonly Canvas _canvas = new();
        private readonly System.Windows.Shapes.Rectangle _selRect = new()
        {
            Stroke = System.Windows.Media.Brushes.Gold,
            StrokeThickness = 1.5,
            Fill = new SolidColorBrush(Color.FromArgb(30, 255, 215, 95)),
            Visibility = Visibility.Collapsed
        };
        private System.Windows.Point _dragStart;
        private bool _selecting, _selected, _annotating;
        private Rect _selection;
        private Polyline _currentStroke;
        private System.Windows.Shapes.Rectangle _currentBox;
        private bool _rectMode;
        private readonly double _virtLeft, _virtTop;

        public static void StartCapture()
        {
            try { new ScreenshotWindow().Show(); }
            catch (Exception ex) { ConfigService.Log("Screenshot failed: " + ex.Message); }
        }

        private ScreenshotWindow()
        {
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            _virtLeft = vs.Left; _virtTop = vs.Top;
            _screen = new DrawingBitmap(vs.Width, vs.Height);
            using (var g = DrawingGraphics.FromImage(_screen))
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0, _screen.Size);
            _screenSource = ToBitmapSource(_screen);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Left = vs.Left; Top = vs.Top; Width = vs.Width; Height = vs.Height;
            Cursor = Cursors.Cross;

            var img = new Image { Source = _screenSource, Stretch = Stretch.Fill };
            var dim = new System.Windows.Shapes.Rectangle { Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)) };
            var root = new Grid();
            root.Children.Add(img);
            root.Children.Add(dim);
            root.Children.Add(_canvas);
            _canvas.Children.Add(_selRect);
            Content = root;

            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            KeyDown += OnKey;
            Loaded += (_, _) => { Activate(); Focus(); };
        }

        private void OnDown(object s, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (!_selected)
            {
                _selecting = true;
                _dragStart = p;
                _selRect.Visibility = Visibility.Visible;
            }
            else if (_annotating)
            {
                if (_rectMode)
                {
                    _currentBox = new System.Windows.Shapes.Rectangle { Stroke = System.Windows.Media.Brushes.Red, StrokeThickness = 2 };
                    Canvas.SetLeft(_currentBox, p.X); Canvas.SetTop(_currentBox, p.Y);
                    _canvas.Children.Add(_currentBox);
                    _dragStart = p;
                }
                else
                {
                    _currentStroke = new Polyline { Stroke = System.Windows.Media.Brushes.Red, StrokeThickness = 2.5 };
                    _currentStroke.Points.Add(p);
                    _canvas.Children.Add(_currentStroke);
                }
            }
        }

        private void OnMove(object s, MouseEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (_selecting)
            {
                var r = new Rect(_dragStart, p);
                Canvas.SetLeft(_selRect, r.X); Canvas.SetTop(_selRect, r.Y);
                _selRect.Width = r.Width; _selRect.Height = r.Height;
            }
            else if (_annotating && e.LeftButton == MouseButtonState.Pressed)
            {
                if (_rectMode && _currentBox != null)
                {
                    var r = new Rect(_dragStart, p);
                    Canvas.SetLeft(_currentBox, r.X); Canvas.SetTop(_currentBox, r.Y);
                    _currentBox.Width = r.Width; _currentBox.Height = r.Height;
                }
                else _currentStroke?.Points.Add(p);
            }
        }

        private void OnUp(object s, MouseButtonEventArgs e)
        {
            if (_selecting)
            {
                _selecting = false;
                _selection = new Rect(_dragStart, e.GetPosition(_canvas));
                if (_selection.Width < 4 || _selection.Height < 4) { Close(); return; }
                _selected = true;
                _annotating = true;
                Title = "Draw to annotate · R rectangle/pen · Enter save · Esc cancel";
            }
            _currentBox = null;
            _currentStroke = null;
        }

        private void OnKey(object s, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.R: _rectMode = !_rectMode; break;
                case Key.Enter:
                case Key.S when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                    if (_selected) SaveAndClose();
                    break;
            }
        }

        private void SaveAndClose()
        {
            try
            {
                _selRect.Visibility = Visibility.Collapsed;

                // Render annotations over the screenshot at screen resolution.
                var dpi = VisualTreeHelper.GetDpi(this);
                var rtb = new RenderTargetBitmap(_screen.Width, _screen.Height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawImage(_screenSource, new Rect(0, 0, Width, Height));
                    var brush = new VisualBrush(_canvas) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
                    dc.DrawRectangle(brush, null, new Rect(0, 0, Width, Height));
                }
                rtb.Render(dv);

                // Crop to selection (convert DIU -> px).
                var crop = new CroppedBitmap(rtb, new Int32Rect(
                    (int)(_selection.X * dpi.DpiScaleX), (int)(_selection.Y * dpi.DpiScaleY),
                    (int)(_selection.Width * dpi.DpiScaleX), (int)(_selection.Height * dpi.DpiScaleY)));

                Clipboard.SetImage(crop);

                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WorkspaceOS");
                Directory.CreateDirectory(dir);
                var file = System.IO.Path.Combine(dir, $"shot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(crop));
                using var fs = File.Create(file);
                enc.Save(fs);
                ConfigService.Log("Screenshot saved: " + file);
            }
            catch (Exception ex) { ConfigService.Log("Screenshot save failed: " + ex.Message); }
            finally { Close(); }
        }

        protected override void OnClosed(EventArgs e)
        {
            _screen.Dispose();
            base.OnClosed(e);
        }

        private static BitmapSource ToBitmapSource(DrawingBitmap bmp)
        {
            var hBitmap = bmp.GetHbitmap();
            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally { DeleteObject(hBitmap); }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
