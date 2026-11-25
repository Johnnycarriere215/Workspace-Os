using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.Shibumi;
using WorkspaceOS.Core.Themes;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Omarchy's Hearthstone picker, ported: the theme set as a fan of cards laid
    /// over the whole desktop, walked with the arrow keys and committed with
    /// Enter. It is not a dialog — no panel, no frame: the cards float in front of
    /// whatever windows are open, and the desktop dims behind them.
    /// </summary>
    public class ThemePickerWindow : Window
    {
        private const double CardWidth = 250, CardHeight = 344, Step = 152;

        private static ThemePickerWindow _open;

        private readonly IReadOnlyList<OmarchyTheme> _themes = ThemeLibrary.All;
        private readonly Canvas _stage = new();
        private readonly TextBlock _caption = new();
        private readonly TextBlock _tagline = new();
        private readonly GlobalKeyHook _keys = new();
        private readonly List<Border> _cards = new();
        private int _index;
        private DateTime _openedAt = DateTime.UtcNow;
        private DispatcherTimer _guard;

        /// <summary>Opens the picker on the active theme; a second call closes it.</summary>
        public static void Open()
        {
            if (_open != null) { _open.Close(); return; }
            _open = new ThemePickerWindow();
            _open.Show();
        }

        private ThemePickerWindow()
        {
            var tokens = ShibumiTokens.From(App.Configs.Config.Appearance, App.Configs.Config.Bar.Shibumi);

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowState = WindowState.Normal;
            FontFamily = UiTheme.ResolveFont(App.Configs.Config.Appearance.FontFamily);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            // The desktop dims behind the fan instead of the fan sitting in a box.
            var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB4, 0, 0, 0)) };
            scrim.MouseLeftButtonDown += (_, _) => Close();

            _caption.FontSize = 34;
            _caption.FontWeight = FontWeights.SemiBold;
            _caption.Foreground = ShibumiPanelWindow.Solid(tokens.Ink);
            _caption.HorizontalAlignment = HorizontalAlignment.Center;

            _tagline.FontSize = 14;
            _tagline.Foreground = ShibumiPanelWindow.Solid(tokens.Sumi);
            _tagline.HorizontalAlignment = HorizontalAlignment.Center;
            _tagline.Margin = new Thickness(0, 6, 0, 0);

            var hint = new TextBlock
            {
                Text = "\u2190 \u2192 choose    \u21B5 apply    esc cancel",
                FontSize = 12,
                Foreground = ShibumiPanelWindow.Solid(tokens.Ink),
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var header = new StackPanel { Margin = new Thickness(0, 64, 0, 0) };
            header.Children.Add(_caption);
            header.Children.Add(_tagline);

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(header, 0);
            Grid.SetRow(_stage, 1);
            Grid.SetRow(hint, 2);
            hint.Margin = new Thickness(0, 0, 0, 56);
            layout.Children.Add(header);
            layout.Children.Add(_stage);
            layout.Children.Add(hint);

            var root = new Grid();
            root.Children.Add(scrim);
            root.Children.Add(layout);
            Content = root;

            _index = IndexOfActive();

            foreach (var theme in _themes)
            {
                var face = Card(theme);
                _cards.Add(face);
                _stage.Children.Add(face);
            }

            _keys.KeyDown = OnKey;
            Loaded += (_, _) =>
            {
                Place();
                Layout(false);
                UpdateCaption();
                _openedAt = DateTime.UtcNow;
                _keys.Install();
                Activate();
                Focus();
                HoldFront();
                // A fullscreen app (or a video that grabbed the arrow keys) can
                // re-raise itself over a topmost overlay; keeping the picker in
                // the topmost band and focused keeps its keys its own.
                _guard = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Normal,
                    (_, _) => HoldFront(), Dispatcher);
                _guard.Start();
                ConfigService.Log($"ThemePicker: opened ({_themes.Count} themes, hint='{_themes[_index].Name}').");
            };
            SizeChanged += (_, _) => { Place(); Layout(false); };
            Closed += (_, _) =>
            {
                _guard?.Stop();
                _keys.Dispose();
                if (ReferenceEquals(_open, this)) _open = null;
                ConfigService.Log("ThemePicker: closed.");
            };
        }

        /// <summary>Keeps the scrim in the topmost band and the keyboard on it.</summary>
        private void HoldFront()
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;
                NativeMethods.SetWindowPos(handle, new IntPtr(-1) /*HWND_TOPMOST*/, 0, 0, 0, 0, 0x13 /*NOMOVE|NOSIZE|NOACTIVATE*/);
                if (!IsActive) NativeMethods.SetForegroundWindow(handle);
            }
            catch { }
        }

        private int IndexOfActive()
        {
            string active = App.Configs.Config.Appearance.BarTheme;
            for (int i = 0; i < _themes.Count; i++)
                if (string.Equals(_themes[i].Name, active, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        /// <summary>Covers the whole screen the pointer is on, taskbar included.</summary>
        private void Place()
        {
            var work = SystemParameters.WorkArea;
            try
            {
                var cursor = System.Windows.Forms.Cursor.Position;
                var point = new POINT { X = cursor.X, Y = cursor.Y };
                var monitor = NativeMethods.MonitorFromPoint(point, 2 /*MONITOR_DEFAULTTONEAREST*/);
                if (NativeMethods.GetMonitorRect(monitor, out var bounds, out _, out _))
                {
                    double scale = 1.0;
                    var source = PresentationSource.FromVisual(this);
                    if (source?.CompositionTarget != null) scale = source.CompositionTarget.TransformToDevice.M11;

                    Left = bounds.Left / scale;
                    Top = bounds.Top / scale;
                    Width = (bounds.Right - bounds.Left) / scale;
                    Height = (bounds.Bottom - bounds.Top) / scale;
                    return;
                }
            }
            catch { }

            Left = work.Left;
            Top = work.Top;
            Width = work.Width;
            Height = work.Height;
        }

        // ---- keyboard ------------------------------------------------------

        private const int VK_ESCAPE = 0x1B, VK_RETURN = 0x0D;
        private const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;

        /// <summary>Runs on the hook thread; every branch marshals to the UI thread.</summary>
        private bool OnKey(int vk)
        {
            // The chord that opened the picker ends on a Space key-up; ignoring the
            // tail of it keeps the picker from applying a theme the instant it opens.
            if ((DateTime.UtcNow - _openedAt).TotalMilliseconds < 450) return false;

            ConfigService.Log($"ThemePicker: key {vk}.");
            switch (vk)
            {
                case VK_ESCAPE:
                    Dispatcher.BeginInvoke(new Action(Close));
                    return true;
                case VK_LEFT:
                case VK_UP:
                    Dispatcher.BeginInvoke(new Action(() => Move(-1)));
                    return true;
                case VK_RIGHT:
                case VK_DOWN:
                    Dispatcher.BeginInvoke(new Action(() => Move(1)));
                    return true;
                case VK_RETURN:
                    Dispatcher.BeginInvoke(new Action(Apply));
                    return true;
            }
            return false;
        }

        private void Move(int delta)
        {
            if (_themes.Count == 0) return;
            _index = ((_index + delta) % _themes.Count + _themes.Count) % _themes.Count;
            Layout(true);
            UpdateCaption();
        }

        private void Apply()
        {
            if (_index < 0 || _index >= _themes.Count) return;
            string name = _themes[_index].Name;
            Close();
            App.SetTheme(name);
        }

        // ---- cards ---------------------------------------------------------

        private Border Card(OmarchyTheme theme)
        {
            var body = new StackPanel();

            var frame = new Border
            {
                Height = 196,
                CornerRadius = new CornerRadius(8),
                Background = ShibumiPanelWindow.Solid(Parse(theme.Background)),
                ClipToBounds = true
            };
            var photo = ThemePhoto.PathFor(theme.Name);
            if (photo != null && File.Exists(photo))
            {
                try
                {
                    frame.Background = new ImageBrush(new BitmapImage(new Uri(photo)))
                    {
                        Stretch = Stretch.UniformToFill,
                        Opacity = 0.94
                    };
                }
                catch { /* a broken image keeps the flat theme colour */ }
            }
            body.Children.Add(frame);

            body.Children.Add(new TextBlock
            {
                Text = theme.Name,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 11, 0, 2),
                Foreground = ShibumiPanelWindow.Solid(Parse(theme.Foreground)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            body.Children.Add(new TextBlock
            {
                Text = theme.Tagline,
                FontSize = 11,
                Foreground = ShibumiPanelWindow.Solid(Parse(theme.InactiveBorder)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 34
            });

            var swatches = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            for (int i = 0; i < 8; i++)
            {
                swatches.Children.Add(new Border
                {
                    Width = 16, Height = 8,
                    Margin = new Thickness(0, 0, 4, 4),
                    CornerRadius = new CornerRadius(2),
                    Background = ShibumiPanelWindow.Solid(Parse(theme.Color(i)))
                });
            }
            body.Children.Add(swatches);

            var face = new Border
            {
                Width = CardWidth,
                Height = CardHeight,
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Background = ShibumiPanelWindow.Solid(Color.FromArgb(0xF2,
                    Parse(theme.Background).R, Parse(theme.Background).G, Parse(theme.Background).B)),
                BorderBrush = ShibumiPanelWindow.Solid(Parse(theme.InactiveBorder)),
                Child = body,
                RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = new TransformGroup
                {
                    Children = { new ScaleTransform(1, 1), new RotateTransform(0) }
                },
                Cursor = Cursors.Hand,
                Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 12, Opacity = 0.55, Color = Colors.Black }
            };
            face.MouseLeftButtonUp += (_, _) => Select(_cards.IndexOf(face));
            return face;
        }

        private void Select(int index)
        {
            if (index < 0) return;
            _index = index;
            Layout(true);
            UpdateCaption();
        }

        /// <summary>Arranges the fan: the focused card upright in the middle, the rest receding.</summary>
        private void Layout(bool animate)
        {
            double mid = _stage.ActualWidth / 2;
            if (mid <= 0) return;

            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                int distance = i - _index;
                double left = mid - CardWidth / 2 + distance * Step;
                double top = 16 + Math.Abs(distance) * 20;
                double scale = Math.Max(0.58, 1 - Math.Abs(distance) * 0.12);
                double angle = distance * 3.4;

                if (animate && card.IsVisible)
                {
                    card.BeginAnimation(Canvas.LeftProperty,
                        new DoubleAnimation(left, TimeSpan.FromMilliseconds(190))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    card.BeginAnimation(Canvas.TopProperty,
                        new DoubleAnimation(top, TimeSpan.FromMilliseconds(190))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
                else
                {
                    card.BeginAnimation(Canvas.LeftProperty, null);
                    card.BeginAnimation(Canvas.TopProperty, null);
                    Canvas.SetLeft(card, left);
                    Canvas.SetTop(card, top);
                }

                var group = (TransformGroup)card.RenderTransform;
                ((ScaleTransform)group.Children[0]).ScaleX = scale;
                ((ScaleTransform)group.Children[0]).ScaleY = scale;
                ((RotateTransform)group.Children[1]).Angle = angle;

                card.Opacity = Math.Abs(distance) >= 4 ? 0 : 1 - Math.Abs(distance) * 0.18;
                card.BorderThickness = new Thickness(distance == 0 ? 2 : 1);
                if (distance == 0)
                {
                    card.BorderBrush = ShibumiPanelWindow.Solid(Parse(_themes[i].Accent));
                    Panel.SetZIndex(card, 100);
                }
                else
                {
                    card.BorderBrush = ShibumiPanelWindow.Solid(Parse(_themes[i].InactiveBorder));
                    Panel.SetZIndex(card, 100 - Math.Abs(distance));
                }
            }
        }

        private void UpdateCaption()
        {
            if (_themes.Count == 0) return;
            var theme = _themes[_index];
            _caption.Text = theme.Name;
            _tagline.Text = $"{theme.Tagline}   ·   {_index + 1} / {_themes.Count}";
        }

        private static Color Parse(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? "#808080" : hex); }
            catch { return Color.FromRgb(0x80, 0x80, 0x80); }
        }
    }
}
