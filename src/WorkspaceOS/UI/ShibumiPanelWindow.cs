using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Shibumi;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// The Windows port of Shibumi's <c>ShibumiPanel</c>: one surface, anchored
    /// under the pill that opened it, 8 px below the bar, with the contract's
    /// 12 px radius, 1 px panel border, 94% paper fill and the optional shadow.
    ///
    /// Panels share this shell so every detail view — CPU, memory, network, audio,
    /// brightness — has identical spacing, header and dismissal behaviour instead
    /// of each window inventing its own geometry.
    /// </summary>
    public class ShibumiPanelWindow : Window
    {
        private readonly ShibumiTokens _tokens;
        private readonly Border _card;
        private readonly TextBlock _title;
        private readonly TextBlock _subtitle;
        private readonly double _width;
        private readonly DispatcherTimer _refresh;
        private FrameworkElement _anchor;

        /// <summary>Raised once a second while the panel is open.</summary>
        public event Action Refresh;

        protected StackPanel Body { get; }

        /// <summary>
        /// Arrow-key handler for panels with a steppable value: the volume panel
        /// sets this so Up/Down (and Left/Right) move the level while the panel is
        /// the open sheet. Steps are 1 % per press, 5 % with Shift.
        /// </summary>
        public Action<int> Nudge { get; set; }

        public ShibumiPanelWindow(string title, string subtitle = "", double width = 300, double contentWidth = 272)
        {
            _tokens = ShibumiTokens.From(App.Configs.Config.Appearance, App.Configs.Config.Bar.Shibumi);
            _width = Math.Max(220, width);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Width = _width;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = UiTheme.ResolveFont(App.Configs.Config.Appearance.FontFamily);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            // The card adds 14 px padding and a 1 px border on each side, so the
            // content may never be wider than the panel or the right-hand values
            // clip ("Muted" becomes "Mute", "100%" loses its last digit).
            double chrome = 2 * 14 + 2 * _tokens.PanelBorderWidth;
            double fitted = Math.Max(160, Math.Min(contentWidth, _width - chrome));
            Body = new StackPanel { Width = fitted };

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel();
            _title = new TextBlock
            {
                Text = title,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Solid(_tokens.Ink)
            };
            _subtitle = new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = Solid(_tokens.Sumi),
                TextWrapping = TextWrapping.Wrap
            };
            titles.Children.Add(_title);
            if (!string.IsNullOrWhiteSpace(subtitle)) titles.Children.Add(_subtitle);
            header.Children.Add(titles);

            var close = new TextBlock
            {
                Text = "\u2715",
                FontSize = 10,
                Foreground = Solid(_tokens.Sumi),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand
            };
            close.MouseLeftButtonUp += (_, _) => Close();
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            Body.Children.Add(header);

            _card = new Border
            {
                Background = Solid(_tokens.PanelBackground),
                BorderBrush = Solid(_tokens.IslandBorder),
                BorderThickness = new Thickness(_tokens.PanelBorderWidth),
                CornerRadius = new CornerRadius(_tokens.PillRadius),
                Padding = new Thickness(14),
                Child = Body
            };
            if (_tokens.ShadowEnabled)
            {
                _card.Effect = new DropShadowEffect
                {
                    BlurRadius = 26,
                    ShadowDepth = 7,
                    Opacity = 0.45,
                    Color = Colors.Black
                };
            }

            Content = _card;
            _keys.KeyDown = OnGlobalKey;
            Loaded += (_, _) => _keys.Install();
            Closed += (_, _) => _keys.Dispose();
            Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                // A click on another app closes the panel, like Shibumi's dismiss
                // layer. Give the activation a moment to settle first.
                if (IsActive) return;
                Close();
            }), DispatcherPriority.Background);

            _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refresh.Tick += (_, _) => { try { Refresh?.Invoke(); } catch { } };
            Loaded += (_, _) => _refresh.Start();
            Closed += (_, _) => _refresh.Stop();
        }

        // ---- keyboard ------------------------------------------------------

        private const int VK_ESCAPE = 0x1B, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;

        private readonly GlobalKeyHook _keys = new();

        /// <summary>
        /// Escape dismisses the sheet and the arrow keys step its value, both from
        /// the hook: the panel is opened under a bar that never activates, so it
        /// cannot rely on holding focus. Handled keys are swallowed so the page
        /// underneath never scrolls while the user is raising the volume.
        /// </summary>
        private bool OnGlobalKey(int vk)
        {
            if (vk == VK_ESCAPE)
            {
                Dispatcher.BeginInvoke(new Action(Close));
                return true;
            }

            if (Nudge == null) return false;

            int direction = vk == VK_UP || vk == VK_RIGHT ? 1
                          : vk == VK_DOWN || vk == VK_LEFT ? -1 : 0;
            if (direction == 0) return false;

            Dispatcher.BeginInvoke(new Action(() =>
                Nudge(direction * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 5 : 1))));
            return true;
        }

        /// <summary>
        /// Draws the panel's card to a PNG for visual checks. The panel normally
        /// lives in a transparent layered window; this renders the card itself so
        /// the result is independent of where the window sits.
        /// </summary>
        public bool SnapshotPanel(string path)
        {
            try
            {
                var content = (FrameworkElement)Content;
                content.Measure(new Size(_width, double.PositiveInfinity));
                content.Arrange(new Rect(0, 0, content.DesiredSize.Width, content.DesiredSize.Height));
                content.UpdateLayout();

                int cw = (int)Math.Ceiling(content.ActualWidth);
                int ch = (int)Math.Ceiling(content.ActualHeight);
                if (cw <= 0 || ch <= 0) return false;

                var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    cw, ch, 96, 96, PixelFormats.Pbgra32);
                target.Render(content);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
                using var stream = System.IO.File.Create(path);
                encoder.Save(stream);
                return true;
            }
            catch (Exception ex)
            {
                ConfigService.Log("Snapshot: panel render failed (" + ex.Message + ")");
                return false;
            }
        }

        /// <summary>Places the panel under its pill, clamped to the bar's monitor.</summary>
        public void ShowUnder(FrameworkElement anchor, Window bar)
        {
            _anchor = anchor;
            Show();
            Activate();
            Position(anchor, bar);
        }

        private void Position(FrameworkElement anchor, Window bar)
        {
            // A hidden bar (a fullscreen app owns the screen) gives no usable
            // anchor, so the sheet takes the top-right corner instead of opening
            // at a stale position.
            if (anchor == null || bar == null || !bar.IsVisible)
            {
                var area = SystemParameters.WorkArea;
                Left = area.Right - ActualWidth - 12;
                Top = area.Top + 44;
                return;
            }

            try
            {
                var origin = anchor.PointToScreen(new Point(0, 0));
                double scale = 1.0;
                var source = PresentationSource.FromVisual(bar);
                if (source?.CompositionTarget != null) scale = source.CompositionTarget.TransformToDevice.M11;

                double anchorCenter = origin.X / scale + anchor.ActualWidth / 2;
                var work = SystemParameters.WorkArea;
                double left = anchorCenter - ActualWidth / 2;
                left = Math.Max(work.Left + 8, Math.Min(left, work.Right - ActualWidth - 8));

                double top = origin.Y / scale + anchor.ActualHeight + 8;
                if (top + ActualHeight > work.Bottom - 8)
                    top = Math.Max(work.Top + 8, work.Bottom - ActualHeight - 8);

                Left = left;
                Top = top;
            }
            catch
            {
                var work = SystemParameters.WorkArea;
                Left = work.Right - ActualWidth - 12;
                Top = work.Top + 44;
            }
        }

        public void Refit() => Position(_anchor, Owner ?? Application.Current?.MainWindow);

        // ---- shared content helpers ---------------------------------------

        public SolidColorBrush Ink => Solid(_tokens.Ink);
        public SolidColorBrush Sumi => Solid(_tokens.Sumi);
        public SolidColorBrush Seal => Solid(_tokens.Seal);
        public ShibumiTokens Tokens => _tokens;

        public void Add(UIElement element) => Body.Children.Add(element);

        public TextBlock Label(string text, double size = 10, bool dim = true) => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = dim ? FontWeights.Normal : FontWeights.SemiBold,
            Foreground = dim ? Sumi : Ink,
            VerticalAlignment = VerticalAlignment.Center
        };

        public TextBlock Value(string text, double size = 12.5) => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>A label/value row with Shibumi's quiet hairline.</summary>
        public Grid Row(string label, string value, bool line = true)
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = Label(label);
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var right = Value(value);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            if (line)
            {
                var rule = new Border
                {
                    Height = 1,
                    Background = Solid(_tokens.Separator),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 12, 0, -6)
                };
                Grid.SetColumnSpan(rule, 2);
                grid.Children.Add(rule);
            }
            Body.Children.Add(grid);
            return grid;
        }

        /// <summary>Shibumi's capsule meter: seal fill on a quiet track.</summary>
        public Grid Meter(double fraction)
        {
            fraction = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1);
            var grid = new Grid { Height = 4, Margin = new Thickness(0, 7, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, fraction), GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - fraction), GridUnitType.Star) });

            var track = new Border
            {
                Background = Solid(ShibumiTokens.WithAlpha(_tokens.Ink, 0.10)),
                CornerRadius = new CornerRadius(2)
            };
            Grid.SetColumnSpan(track, 2);
            grid.Children.Add(track);

            var fill = new Border
            {
                Background = Seal,
                CornerRadius = new CornerRadius(2)
            };
            Grid.SetColumn(fill, 0);
            grid.Children.Add(fill);

            Body.Children.Add(grid);
            return grid;
        }

        /// <summary>
        /// A Shibumi capsule slider: thin track, seal fill, readout on the right.
        /// Returns the readout so a live panel can keep it in sync.
        /// <paramref name="from"/>/<paramref name="to"/> scale the value shown and
        /// reported, so one control can span e.g. 100–200 % of boost headroom.
        /// </summary>
        public TextBlock Slider(string label, double value, Action<double> onChange,
            string format = "{0:0}%", double from = 0, double to = 100) =>
            CapsuleSlider(label, value, onChange, format, from, to).Readout;

        /// <summary>The capsule slider plus a way to move it without a mouse.</summary>
        public sealed class SliderHandle
        {
            public TextBlock Readout { get; init; }
            /// <summary>Moves the fill and readout; does not raise the slider's callback.</summary>
            public Action<double> Set { get; init; }
        }

        public SliderHandle CapsuleSlider(string label, double value, Action<double> onChange,
            string format = "{0:0}%", double from = 0, double to = 100,
            Func<double, string> describe = null)
        {
            value = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1);

            var head = new Grid { Margin = new Thickness(0, 6, 0, 4) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var caption = Label(label);
            head.Children.Add(caption);
            var readout = Value(describe != null
                ? describe(value)
                : string.Format(format, from + (to - from) * value), 10.5);
            Grid.SetColumn(readout, 1);
            head.Children.Add(readout);
            Body.Children.Add(head);

            var track = new Border
            {
                Height = 6,
                Background = Solid(ShibumiTokens.WithAlpha(_tokens.Ink, 0.10)),
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 2, 0, 4)
            };
            var fill = new Border
            {
                Background = Seal,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = false
            };
            var host = new Grid();
            host.Children.Add(track);
            host.Children.Add(fill);
            Body.Children.Add(host);

            void Paint(double fraction)
            {
                fraction = Math.Clamp(fraction, 0, 1);
                double width = track.ActualWidth > 0 ? track.ActualWidth : host.ActualWidth;
                fill.Width = Math.Max(1, width * fraction);
                readout.Text = describe != null
                    ? describe(fraction)
                    : string.Format(format, from + (to - from) * fraction);
            }

            bool dragging = false;
            void Apply(Point point)
            {
                double width = track.ActualWidth > 0 ? track.ActualWidth : host.ActualWidth;
                if (width <= 0) return;
                double fraction = Math.Clamp(point.X / width, 0, 1);
                Paint(fraction);
                try { onChange(fraction); } catch { }
            }

            host.MouseLeftButtonDown += (_, e) =>
            {
                dragging = true;
                host.CaptureMouse();
                Apply(e.GetPosition(host));
            };
            host.MouseMove += (_, e) => { if (dragging) Apply(e.GetPosition(host)); };
            host.MouseLeftButtonUp += (_, _) => { dragging = false; host.ReleaseMouseCapture(); };
            host.SizeChanged += (_, _) => Paint(value);
            Paint(value);
            return new SliderHandle { Readout = readout, Set = Paint };
        }

        public Border Button(string text, Action onClick, bool primary = false)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                Foreground = primary ? Solid(_tokens.Paper) : Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var button = new Border
            {
                Child = label,
                Background = primary ? Seal : Solid(_tokens.FillIdle),
                BorderBrush = Solid(primary ? _tokens.Seal : _tokens.Separator),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(_tokens.TileRadius),
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand
            };
            button.MouseEnter += (_, _) => button.Background = primary
                ? Solid(_tokens.FillPrimaryHover)
                : Solid(_tokens.FillHover);
            button.MouseLeave += (_, _) => button.Background = primary
                ? Seal : Solid(_tokens.FillIdle);
            button.MouseLeftButtonUp += (_, _) => onClick();
            return button;
        }

        public WrapPanel Buttons() => new() { Margin = new Thickness(0, 6, 0, 0) };

        public static SolidColorBrush Solid(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
