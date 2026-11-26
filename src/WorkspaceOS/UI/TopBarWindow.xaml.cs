using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.Metrics;
using WorkspaceOS.Core.Shibumi;
using WorkspaceOS.Core.Taskbar;
using WorkspaceOS.Core.Themes;
using WorkspaceOS.Core.Updates;
using System.IO;
using System.Windows.Shapes;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Waybar/Quickshell-style top bar, registered as a Win32 appbar so it reserves
    /// screen space and maximized windows never cover it.
    /// </summary>
    public partial class TopBarWindow : Window
    {
        private IntPtr _hwnd;
        private uint _appBarMessage;
        private bool _appBarRegistered;
        private uint _taskbarCreatedMsg;
        private bool _gameMode;

        // Monitor this bar belongs to (set by BarManager before Show).
        private readonly string _device;
        private IntPtr _monitorHandle;
        private int _monLeft, _monTop, _monRight, _monBottom;
        private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer _metricsTimer = new();
        private readonly DispatcherTimer _numberTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private int _lastActiveIndex = -1;

        // Dev-only slow motion for the workspace-number pop, so the harness can
        // capture the transition mid-flight (WORKSPACEOS_ANIM_SCALE=12 → 12×).
        private static readonly double AnimationScale = ReadAnimationScale();

        private static double ReadAnimationScale()
        {
            string raw = Environment.GetEnvironmentVariable("WORKSPACEOS_ANIM_SCALE");
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
                   && scale > 0 ? Math.Min(scale, 40) : 1.0;
        }
        private string _clockKey = "";
        private IntPtr _calendarHook;
        private NativeMethods.LowLevelMouseProc _calendarHookProc;
        private readonly GlobalKeyHook _menuKeys = new();

        private sealed class WorkspaceChip
        {
            public int Index;
            public Border Root;
            public TextBlock Text;
            public SolidColorBrush Foreground;
        }

        private sealed class ModuleChip
        {
            public string Name;
            public Border Root;
            public TextBlock Label;
            public TextBlock Value;
            public TextBlock Detail;
            public Action<double> SetProgress;

            // Smoothly interpolated numeric display (percent, GB, bytes/s).
            public double Current;
            public double Target;
            public bool HasNumber;
            public bool Initialized;
            public Func<double, string> Format;
        }

        private readonly List<WorkspaceChip> _workspaceChips = new();
        private readonly List<ModuleChip> _moduleChips = new();
        private Border _updateChip;
        private bool _updateNoticeLogged;
        private string _moduleKey = "";
        private int _chipCount = -1;
        private bool _compact;

        public TopBarWindow() : this("") { }

        public TopBarWindow(string device)
        {
            _device = device ?? "";
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            Closing += (_, _) => UnregisterAppBar();
            Closed += (_, _) => { _numberTimer.Stop(); StopCalendarDismissHook(); };
            CalendarPopup.Closed += (_, _) => StopCalendarDismissHook();
            _numberTimer.Tick += (_, _) => TickNumbers();
            _cavaTimer.Tick += (_, _) => UpdateCavaVisual();
            _cavaTimer.Start();
            SizeChanged += (_, _) => UpdateClockLayout();
            MenuPopup.Closed += (_, _) => StopCalendarDismissHook();
            _menuKeys.KeyDown = OnMenuKey;
            // G8: the clock pill is the calendar's anchor, like the Shibumi centre.
            CenterPill.MouseLeftButtonUp += (_, e) => ClockText_Click(null, e);

            // Soft entrance so a new bar eases in instead of popping.
            Loaded += (_, _) =>
            {
                Opacity = 1;
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                UpdateMetrics();   // ActualWidth is known now: pick compact vs full modules
                UpdateClockLayout();
            };
        }

        /// <summary>
        /// Opens one widget's panel exactly as clicking its chip does. This is the
        /// hotkey path (Ctrl+Win+A opens Audio); when the widget is hidden from the
        /// bar the centre pill is used as the anchor so the panel still appears
        /// under the bar instead of at a stale position.
        /// </summary>
        public void OpenWidgetPanel(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var chip = _moduleChips.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            ShibumiWidgetPanels.Toggle(name, chip?.Root ?? (FrameworkElement)CenterPill, this);
        }

        /// <summary>Assigns the monitor this bar covers; call before Show().</summary>
        public void SetMonitor(IntPtr handle, int left, int top, int right, int bottom)
        {
            _monitorHandle = handle;
            _monLeft = left; _monTop = top; _monRight = right; _monBottom = bottom;
        }

        /// <summary>
        /// Fullscreen games get the whole monitor: remove the appbar reservation
        /// and hide the bar without activating it. Restore both when the game ends.
        /// </summary>
        public void SetGameMode(bool gameMode)
        {
            if (_gameMode == gameMode) return;
            _gameMode = gameMode;

            if (gameMode)
            {
                UnregisterAppBar();
                Hide();
                return;
            }

            if (_hwnd != IntPtr.Zero)
            {
                Show();
                RegisterAppBar();
            }
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            _hwnd = source.Handle;
            source.AddHook(WndProc);

            // Defensive fallback if no monitor was assigned by BarManager.
            if (_monRight <= _monLeft)
            {
                var mon = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (NativeMethods.GetMonitorRect(mon, out var b, out _, out _))
                {
                    _monitorHandle = mon;
                    _monLeft = b.Left; _monTop = b.Top; _monRight = b.Right; _monBottom = b.Bottom;
                }
            }

            // Never activate the bar; keep focus in user apps.
            long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));

            _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");

            ApplyConfig();
            RegisterAppBar();

            _clockTimer.Tick += (_, _) => UpdateClock();
            _clockTimer.Start();
            UpdateClock();

            _metricsTimer.Tick += (_, _) => UpdateMetrics();
            _metricsTimer.Start();
            UpdateMetrics();

            App.Workspaces.ActiveWorkspaceChanged += _ => Dispatcher.Invoke(RenderWorkspaces);

            // The bar must be visible on every native virtual desktop.
            App.Workspaces.PinAppWindow(_hwnd);
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public void ApplyConfig()
        {
            var a = App.Configs.Config.Appearance;

            // Every WorkspaceOS surface reads UiTheme, so one refresh recolours
            // the bar, the settings window and the popups from the same theme.
            UiTheme.Refresh(a);

            var barTint = ColorOf(a.BarBackground);
            ApplyProfile();

            // Acrylic tints the whole appbar strip, which is right for the
            // edge-to-edge form and wrong for an inset pill: the floating forms
            // paint the surface themselves so their margins stay transparent.
            bool floating = !string.Equals(App.Configs.Config.Bar.Profile, "Full",
                StringComparison.OrdinalIgnoreCase);
            bool acrylic = !floating && _hwnd != IntPtr.Zero
                && UiTheme.TryEnableAcrylic(_hwnd, barTint, 0xB4);
            Background = Brushes.Transparent;
            Root.Background = Brushes.Transparent;
            System.Windows.Media.Brush surfaceBrush = floating
                ? GlassBrush(barTint)
                : acrylic ? GlassHighlightBrush(a) : GlassBrush(barTint);

            var tokens = ShibumiTokens.From(a, App.Configs.Config.Bar.Shibumi);
            // Shibumi's surface is one flat paper sheet at the contract alpha,
            // not a gradient: frost is the 0.68 paper, no-frost the quiet 0.94.
            if (IsShibumi) surfaceBrush = SolidColor(tokens.BarBackground);

            // Shibumi's single run owns the surface. Every other form paints the
            // edge-to-edge strip as before.
            if (IsShibumi)
            {
                BarSurface.Background = SolidColor(tokens.BarBackground);
                BarSurface.BorderBrush = SolidColor(tokens.IslandBorder);
                BarSurface.BorderThickness = new Thickness(tokens.BorderEnabled ? 1 : 0);
            }
            else
            {
                BarSurface.Background = surfaceBrush;
                BarSurface.BorderBrush = Brushes.Transparent;
                BarSurface.BorderThickness = new Thickness(0);
            }
            LeftIsland.Background = CenterIsland.Background = RightIsland.Background = Brushes.Transparent;

            var font = UiTheme.ResolveFont(a.FontFamily);
            ClockText.FontFamily = font;
            // G8 uses the contract's 12px label size, normal weight (the time is
            // not a heading), and the date is sumi.
            ClockText.FontSize = IsShibumi ? 12 : Math.Max(9, a.FontSize);
            ClockText.FontWeight = IsShibumi ? FontWeights.Normal : FontWeights.SemiBold;
            ClockText.Foreground = Brush(a.BarForeground);
            ControlCenterGlyph.Foreground = Brush(a.ModuleLabelColor);

            // The Shibumi centre is plain type on the island, not a capsule: the
            // clock is the anchor, and a second filled pill there reads as a
            // widget competing with the workspaces.
            CenterPill.Background = Brushes.Transparent;
            CenterPill.BorderThickness = new Thickness(0);
            CenterPill.Padding = new Thickness(2, 0, 2, 0);
            CenterPill.CornerRadius = new CornerRadius(0);
            LeftModulePanel.Visibility = IsShibumi ? Visibility.Visible : Visibility.Collapsed;
            BarHairline.Fill = Brush(a.SeparatorColor);
            LogoButton.ToolTip = $"Bar menu — {App.Configs.Config.Bar.Profile} bar, {a.BarTheme}";
            MenuBorder.Background = Brush(ThemeColor.Alpha(a.BarBackground, 0xF2));
            MenuBorder.BorderBrush = Brush(a.SeparatorColor);
            CalendarBorder.Background = Brush(ThemeColor.Alpha(a.BarBackground, 0xF7));
            CalendarBorder.BorderBrush = Brush(a.SeparatorColor);
            RenderBarIcon(a);

            _metricsTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(500, App.Configs.Config.Bar.RefreshMs));

            _chipCount = -1;          // force a chip rebuild on the next render
            _moduleKey = "";

            RenderWorkspaces();
            UpdateMetrics();
            UpdateCavaVisual();
            // Force the clock to rebuild its runs: the date's colour changed with
            // the theme, and the minute guard would otherwise keep the old one
            // until the next minute tick.
            _clockKey = "";
            UpdateClock();
            Dispatcher.BeginInvoke(UpdateClockLayout, DispatcherPriority.Loaded);
            if (_appBarRegistered) PositionBar();
        }

        /// <summary>True for the Shibumi bar form (its own geometry tokens).</summary>
        // Shibumi pill tokens (V1 contract: 24px tall, 12px radius, 9px padding);
        // the other bar forms keep their own metrics.
        private int PillRadius =>
            Math.Clamp(App.Configs.Config.Bar.Shibumi.PillRadius, 4, 20);
        private double PillPadX =>
            Math.Clamp(App.Configs.Config.Bar.Shibumi.PillPaddingX, 2, 20);
        private double PillPadY =>
            Math.Max(1, (Math.Clamp(App.Configs.Config.Bar.Shibumi.PillHeight, 16, 40) - 15) / 2.0);

        private static bool IsShibumi =>
            string.Equals((App.Configs.Config.Bar.Profile ?? "").Trim(), "Shibumi",
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Bar forms, switchable from the logo menu. Every form reserves the same
        /// appbar strip; the difference is the surface inside it, so maximized
        /// windows never cover the bar whatever the form.
        /// </summary>
        private void ApplyProfile()
        {
            string profile = (App.Configs.Config.Bar.Profile ?? "Full").Trim();
            BarSurface.HorizontalAlignment = HorizontalAlignment.Stretch;
            BarSurface.Width = double.NaN;
            BarSurface.MinWidth = 0;
            BarSurface.MaxWidth = double.PositiveInfinity;
            BarSurface.Height = double.NaN;
            BarSurface.VerticalAlignment = VerticalAlignment.Stretch;
            BarSurface.Padding = new Thickness(0);
            BarSurface.BorderThickness = new Thickness(0);

            switch (profile.ToLowerInvariant())
            {
                case "fit":
                    BarSurface.CornerRadius = new CornerRadius(10);
                    BarSurface.Margin = new Thickness(10, 4, 10, 4);
                    break;
                case "dock":
                    BarSurface.CornerRadius = new CornerRadius(16);
                    BarSurface.Margin = new Thickness(20, 5, 20, 5);
                    break;
                case "shibumi":
                {
                    // V1's default is one run: all splits are off, so this is a
                    // single inset island across the strip, 32 px high inside the
                    // 35 px band with a 5 px gap beneath it. Geometry comes from
                    // the tokens (the QML's own numbers), not local magic values.
                    var tokens = ShibumiTokens.From(App.Configs.Config.Appearance,
                        App.Configs.Config.Bar.Shibumi);
                    BarSurface.CornerRadius = new CornerRadius(tokens.IslandRadius);
                    BarSurface.Margin = new Thickness(tokens.IslandInsetX, tokens.IslandOffsetY,
                        tokens.IslandInsetX, 0);
                    BarSurface.Padding = new Thickness(tokens.IslandContentInsetX, 0,
                        tokens.IslandContentInsetX, 0);
                    BarSurface.Height = tokens.IslandHeight;
                    BarSurface.VerticalAlignment = VerticalAlignment.Top;
                    break;
                }
                case "notch":
                    // A centred pill sized by its content. Its cap has to clear
                    // workspaces + clock + cava + modules; the clock sits in the
                    // middle column here (see UpdateClockLayout) so the pill grows
                    // around it instead of drawing it under the modules.
                    BarSurface.CornerRadius = new CornerRadius(15);
                    BarSurface.Margin = new Thickness(0, 4, 0, 4);
                    BarSurface.HorizontalAlignment = HorizontalAlignment.Center;
                    BarSurface.MinWidth = 520;
                    BarSurface.MaxWidth = 1400;
                    break;
                default:
                    BarSurface.CornerRadius = new CornerRadius(0);
                    BarSurface.Margin = new Thickness(0);
                    break;
            }

            ApplyIslands();
        }

        /// <summary>
        /// Shibumi's single run. Every other form leaves the docks transparent and
        /// unpadded, so the docks look exactly as they did on the single surface.
        /// </summary>
        private void ApplyIslands()
        {
            // V1 default is ONE island (BarSurface). The three dock hosts are
            // layout containers only; giving each its own fill was the mistake
            // that made this look like three widget slabs instead of Shibumi.
            var docks = new[] { LeftIsland, CenterIsland, RightIsland };
            foreach (var island in docks)
            {
                island.Background = Brushes.Transparent;
                island.BorderThickness = new Thickness(0);
                island.Padding = new Thickness(0);
                island.Margin = new Thickness(0);
                island.Height = double.NaN;
                island.CornerRadius = new CornerRadius(0);
            }

            LeftDock.Margin = IsShibumi ? new Thickness(0) : new Thickness(10, 0, 0, 0);
            RightDock.Margin = IsShibumi ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 6, 0);
            BarHairline.Visibility = IsShibumi ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>cava is optional and can die: hide the widget rather than break the bar.</summary>
        private void UpdateCavaVisual()
        {
            if (CavaCanvas == null) return;

            var bar = App.Configs.Config.Bar;
            var levels = App.Cava?.Bars ?? Array.Empty<double>();
            int count = Math.Min(bar.CavaBars <= 0 ? 18 : bar.CavaBars, levels.Length);

            // The widget stays put when nothing is playing: silence renders as the
            // flat baseline dashes, which is the bar at rest. Only a cava process
            // that is not running (count == 0) or a hidden widget collapses it.
            bool show = bar.ShowCava && count > 0 && ActualWidth >= 760;
            CavaCanvas.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show)
            {
                if (_cavaBars.Count > 0) { _cavaBars.Clear(); CavaCanvas.Children.Clear(); }
                return;
            }

            const double slot = 3.4, gap = 1.3;
            double height = Math.Max(8, CavaCanvas.Height);
            if (_cavaBars.Count != count)
            {
                CavaCanvas.Children.Clear();
                _cavaBars.Clear();
                for (int i = 0; i < count; i++)
                {
                    var rect = new Rectangle { Width = slot, RadiusX = 1.5, RadiusY = 1.5, Height = 1 };
                    Canvas.SetLeft(rect, i * (slot + gap));
                    Canvas.SetBottom(rect, 0);
                    CavaCanvas.Children.Add(rect);
                    _cavaBars.Add(rect);
                }
                CavaCanvas.Width = count * (slot + gap);
            }

            var a = App.Configs.Config.Appearance;
            var low = ColorOf(a.AccentColor);
            var high = ColorOf(a.ActiveWorkspaceBackground);
            for (int i = 0; i < count; i++)
            {
                double level = Math.Clamp(levels[i], 0, 1);
                var rect = _cavaBars[i];
                rect.Height = Math.Max(1.5, level * height);
                rect.Fill = new SolidColorBrush(Mix(low, high, 0.25 + level * 0.75));
            }
        }

        private static Color Mix(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// Draws the selected logo (or the user's own image) at the configured
        /// width. Custom image paths win over the built-in marks.
        /// </summary>
        private void RenderBarIcon(AppearanceConfig a)
        {
            if (BarIconView == null || BarIconCanvas == null) return;

            if (!a.ShowBarIcon)
            {
                BarIconView.Visibility = Visibility.Collapsed;
                BarIconCanvas.Children.Clear();
                return;
            }

            BarIconView.Visibility = Visibility.Visible;

            BarIconCatalog.Render(BarIconCanvas, a.BarIcon, a.BarIconPath,
                ColorOf(a.ActiveWorkspaceBackground),
                ColorOf(a.BarForeground),
                ColorOf(a.ModuleBorder));

            // The catalogue sets the canvas to the asset's natural box: 100x24
            // for a wordmark, 24x24 for a glyph. Scale that box uniformly so a
            // wordmark is never squeezed into the old 46x24 icon slot.
            double maxWidth = Math.Clamp(a.BarIconWidth, 12, 140);
            double maxHeight = Math.Clamp(a.BarHeight - 12, 12, 22);
            double canvasW = Math.Max(1, BarIconCanvas.Width);
            double canvasH = Math.Max(1, BarIconCanvas.Height);
            double scale = Math.Min(maxWidth / canvasW, maxHeight / canvasH);
            BarIconView.Width = canvasW * scale;
            BarIconView.Height = canvasH * scale;
        }

        private static SolidColorBrush Brush(string hex) => new(ColorOf(hex));

        private static Color ColorOf(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return UiTheme.Accent; }
        }

        /// <summary>
        /// A translucent tint that lets the wallpaper read through the strip.
        /// Used when Windows' acrylic compositor is unavailable, so the bar is
        /// still glassy rather than an opaque black block.
        /// </summary>
        private static LinearGradientBrush GlassBrush(Color tint)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0xD2, tint.R, tint.G, tint.B), 0));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x8E, tint.R, tint.G, tint.B), 1));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Acrylic already supplies the dark tint and wallpaper blur; this adds
        /// only a faint top sheen so the surface still has a glass edge.
        /// </summary>
        private static LinearGradientBrush GlassHighlightBrush(AppearanceConfig a)
        {
            var ink = ColorOf(a.BarForeground);
            var accent = ColorOf(a.ActiveWorkspaceBackground);
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, ink.R, ink.G, ink.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x0A, accent.R, accent.G, accent.B), 1));
            brush.Freeze();
            return brush;
        }

        // ---- appbar ------------------------------------------------------

        private void RegisterAppBar()
        {
            if (_gameMode) return;
            _appBarMessage = NativeMethods.RegisterWindowMessage("WorkspaceOS.AppBar");
            var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = _appBarMessage };
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd);
            _appBarRegistered = true;
            PositionBar();
        }

        private void PositionBar()
        {
            if (_gameMode) return;
            // The band is the surface plus Shibumi's breathing gap. ExclusiveHeight
            // owns that arithmetic, so the gap can never drift from the island.
            var appearance = App.Configs.Config.Appearance;
            int band = IsShibumi
                ? ShibumiTokens.From(appearance, App.Configs.Config.Bar.Shibumi).ExclusiveHeight
                : Math.Max(20, appearance.BarHeight);
            // Reserve physical pixels for this monitor's own DPI, not the primary's.
            int dpi = NativeMethods.GetMonitorDpi(_monitorHandle);
            int barPx = Math.Max(20, (int)Math.Round(band * dpi / 96.0));
            bool assigned = _monRight > _monLeft;
            int left = assigned ? _monLeft : 0;
            int top = assigned ? _monTop : 0;
            int right = assigned ? _monRight : 1920;

            var abd = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = _hwnd,
                uEdge = NativeMethods.ABE_TOP,
                rc = new RECT { Left = left, Top = top, Right = right, Bottom = top + barPx }
            };
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);
            abd.rc.Bottom = abd.rc.Top + barPx;
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
                abd.rc.Left, abd.rc.Top, abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        private void UnregisterAppBar()
        {
            if (!_appBarRegistered) return;
            var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
            _appBarRegistered = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_DISPLAYCHANGE)
            {
                // Add/remove/reposition one bar per monitor.
                Dispatcher.BeginInvoke(() => App.Bars?.Reconcile());
            }
            else if (msg == NativeMethods.WM_DPICHANGED)
            {
                Dispatcher.BeginInvoke(PositionBar); // bar height follows the new DPI
            }
            else if (_taskbarCreatedMsg != 0 && msg == (int)_taskbarCreatedMsg)
            {
                // Explorer restarted — re-register the appbar, reconnect the
                // virtual-desktop COM services, and re-pin the bar.
                _appBarRegistered = false;
                Dispatcher.BeginInvoke(() =>
                {
                    RegisterAppBar();
                    App.Workspaces.ReconnectShell();
                    App.Workspaces.PinAppWindow(_hwnd);
                    RenderWorkspaces();
                });
            }
            else if (msg == (int)_appBarMessage && (uint)wParam.ToInt64() == NativeMethods.ABN_FULLSCREENAPP)
            {
                bool fullscreen = lParam != IntPtr.Zero;
                Dispatcher.BeginInvoke(() => App.Bars?.ReportShellFullscreen(fullscreen));
            }
            else if (msg == (int)_appBarMessage && (uint)wParam.ToInt64() == NativeMethods.ABM_WINDOWPOSCHANGED)
            {
                Dispatcher.BeginInvoke(PositionBar);
            }
            return IntPtr.Zero;
        }

        // ---- left: workspaces ---------------------------------------------

        private void RenderWorkspaces()
        {
            var cfg = App.Configs.Config;
            var a = cfg.Appearance;
            // Slots are drawn even before their native desktop exists: the bar
            // advertises the full strip, and an unused slot opens on first click.
            int openCount = App.Workspaces.WorkspaceCount;
            int count = Math.Max(Math.Max(1, cfg.Bar.WorkspaceSlots),
                                 Math.Max(cfg.Workspaces.Count, Math.Max(1, openCount)));
            // The native desktops are the only workspace layer.
            int activeIndex = App.Workspaces.ActiveWorkspace;
            var occupancy = App.Workspaces.GetOccupancy();

            if (_chipCount != count)
            {
                // A rebuild must not forget the previous index: it is what decides
                // whether the pill slides and the number pops. Every config apply
                // (including a workspace switch) rebuilds the chips, so resetting
                // it here swallowed the switch animation entirely.
                BuildWorkspaceChips(count);
                _chipCount = count;
            }

            var font = UiTheme.ResolveFont(a.FontFamily);
            var tokens = ShibumiTokens.From(a, cfg.Bar.Shibumi);
            var accent = Brush(a.ActiveWorkspaceBackground);
            // V1 "numbers" is monochrome: the focused badge is ink at 30%, occupied
            // at 12%, empty at 4%; the digits are ink / 50% / 28%. No seal ring.
            var onAccent = IsShibumi ? SolidColor(tokens.Ink) : Brush(a.ActiveWorkspaceForeground);
            var occupiedBrush = IsShibumi
                ? SolidColor(ShibumiTokens.WithAlpha(tokens.Ink, 0.5))
                : Brush(a.BarForeground);
            var emptyBrush = IsShibumi
                ? SolidColor(ShibumiTokens.WithAlpha(tokens.Ink, 0.28))
                : Brush(a.AccentColor);
            // One sliding pill belongs to the other bar forms. Shibumi badges
            // carry their own fill, so the overlay must not draw at all.
            ActivePill.Visibility = IsShibumi ? Visibility.Collapsed : Visibility.Visible;
            ActivePill.Background = IsShibumi ? Brushes.Transparent : accent;
            ActivePill.BorderBrush = Brushes.Transparent;
            ActivePill.BorderThickness = new Thickness(0);
            ActivePill.CornerRadius = new CornerRadius(IsShibumi ? tokens.PillRadius : 6);

            foreach (var chip in _workspaceChips)
            {
                int i = chip.Index;
                bool active = i == activeIndex;
                bool pending = i > openCount;            // slot exists visually, desktop does not yet
                bool occupied = i - 1 < occupancy.Length && occupancy[i - 1];
                var wsCfg = cfg.Workspaces.FirstOrDefault(w => w.Index == i);
                string label = string.IsNullOrWhiteSpace(wsCfg?.Name) ? i.ToString() : wsCfg.Name;
                if (IsShibumi && string.Equals(App.Configs.Config.Bar.Shibumi.WorkspaceMarker,
                        "Dots", StringComparison.OrdinalIgnoreCase))
                    label = "●";

                chip.Text.Text = label;
                chip.Text.FontFamily = font;
                chip.Text.FontSize = IsShibumi ? (active ? 13 : 12) : Math.Max(9, a.FontSize - 1);
                chip.Text.FontWeight = active
                    ? (IsShibumi ? FontWeights.Bold : FontWeights.SemiBold)
                    : FontWeights.Normal;
                if (IsShibumi)
                {
                    chip.Root.Background = SolidColor(ShibumiTokens.WithAlpha(tokens.Ink,
                        active ? 0.30 : occupied ? 0.12 : 0.04));
                    chip.Root.BorderThickness = new Thickness(0);
                }
                else chip.Root.Background = Brushes.Transparent;
                chip.Root.Tag = active;
                chip.Root.ToolTip = pending
                    ? $"Workspace {label} — click to open it"
                    : $"Workspace {label}" + (occupied ? " · occupied" : "");

                // Cross-fade the label colour instead of snapping it.
                var target = active ? onAccent.Color : occupied ? occupiedBrush.Color : emptyBrush.Color;
                if (chip.Foreground.Color != target)
                {
                    chip.Foreground.BeginAnimation(SolidColorBrush.ColorProperty,
                        new ColorAnimation(target, TimeSpan.FromMilliseconds(160))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                }

                double targetOpacity = pending ? 0.5 : 1.0;
                if (Math.Abs(chip.Root.Opacity - targetOpacity) > 0.01)
                {
                    chip.Root.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(200))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                }
            }

            // The pill slides only when the selection actually changes.
            if (_lastActiveIndex != activeIndex)
            {
                bool moved = _lastActiveIndex >= 0;
                if (!IsShibumi) UpdateActivePill(activeIndex, animate: moved);
                if (moved) PopChipLabel(activeIndex);
                _lastActiveIndex = activeIndex;
            }
        }

        /// <summary>The newly active number pops once, so a switch reads as a move.</summary>
        private void PopChipLabel(int index)
        {
            var text = _workspaceChips.FirstOrDefault(c => c.Index == index)?.Text;
            if (text == null) return;

            if (text.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1, 1);
                text.RenderTransform = scale;
                text.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var pop = new DoubleAnimation(1.0, 1.28,
                TimeSpan.FromMilliseconds(110 * AnimationScale))
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        /// <summary>Moves the single active pill to a chip, easing between slots.</summary>
        private void UpdateActivePill(int activeIndex, bool animate)
        {
            var chip = _workspaceChips.FirstOrDefault(c => c.Index == activeIndex);
            if (chip == null) { ActivePill.Opacity = 0; return; }

            if (chip.Root.ActualWidth <= 0)
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateActivePill(activeIndex, animate)),
                    DispatcherPriority.Loaded);
                return;
            }

            Point pos;
            try { pos = chip.Root.TransformToAncestor(WorkspacePanel).Transform(new Point(0, 0)); }
            catch { return; }

            double w = chip.Root.ActualWidth, h = chip.Root.ActualHeight;
            ActivePill.Opacity = 1;

            if (!animate)
            {
                ActivePillTransform.BeginAnimation(TranslateTransform.XProperty, null);
                ActivePill.BeginAnimation(FrameworkElement.WidthProperty, null);
                ActivePill.BeginAnimation(FrameworkElement.HeightProperty, null);
                ActivePillTransform.X = pos.X;
                ActivePill.Width = w;
                ActivePill.Height = h;
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(260);
            ActivePillTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(pos.X, duration) { EasingFunction = ease });
            ActivePill.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(w, duration) { EasingFunction = ease });
            ActivePill.BeginAnimation(FrameworkElement.HeightProperty,
                new DoubleAnimation(h, duration) { EasingFunction = ease });
        }

        private void BuildWorkspaceChips(int count)
        {
            _workspaceChips.Clear();
            WorkspacePanel.Children.Clear();

            for (int i = 1; i <= count; i++)
            {
                var fg = new SolidColorBrush(UiTheme.Sumi);
                var text = new TextBlock
                {
                    Foreground = fg,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    LineHeight = 20,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                };
                // Shibumi's marker is a 20 px badge (10 px radius) with a 3 px
                // gap; the other forms keep the older padded pill.
                var sh = App.Configs.Config.Bar.Shibumi;
                double padX = IsShibumi ? 0 : 10;
                double padY = IsShibumi ? 0 : 3;
                var chip = new Border
                {
                    Child = text,
                    CornerRadius = new CornerRadius(IsShibumi ? 10 : Math.Clamp(sh.PillRadius, 4, 20)),
                    Padding = new Thickness(padX, padY, padX, padY),
                    Margin = new Thickness(IsShibumi ? 3 : 1, 0, IsShibumi ? 3 : 1, 0),
                    Background = Brushes.Transparent,
                    Width = IsShibumi ? 20 : double.NaN,
                    Height = IsShibumi ? 20 : double.NaN,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var hover = IsShibumi
                    ? SolidColor(ShibumiTokens.From(App.Configs.Config.Appearance,
                        App.Configs.Config.Bar.Shibumi).FillHover)
                    : UiTheme.Brush(UiTheme.SurfaceRaised);
                chip.MouseEnter += (_, _) =>
                {
                    if (chip.Tag is bool active && active) return;
                    chip.Background = hover;
                };
                chip.MouseLeave += (_, _) =>
                {
                    if (chip.Tag is bool active && active) return;
                    chip.Background = Brushes.Transparent;
                };

                int idx = i;
                chip.MouseLeftButtonUp += (_, _) => App.SwitchWorkspace(idx);

                _workspaceChips.Add(new WorkspaceChip { Index = i, Root = chip, Text = text, Foreground = fg });
                WorkspacePanel.Children.Add(chip);
            }
        }

        // ---- center: clock -------------------------------------------------

        private void UpdateClock()
        {
            var now = DateTime.Now;
            var ci = CultureInfo.InvariantCulture;
            var barCfg = App.Configs.Config.Bar;
            // Standard 12-hour with AM/PM unless the config asks for 24-hour.
            string time = barCfg.Use24Hour
                ? (barCfg.ShowSeconds ? now.ToString("HH:mm:ss", ci) : now.ToString("HH:mm", ci))
                : (barCfg.ShowSeconds ? now.ToString("h:mm:ss tt", ci) : now.ToString("h:mm tt", ci));
            // "2:37 PM  Thu 9 Jul" — time in ink, date in sumi, like Shibumi's G8.
            string dateText = $"  {now.ToString("ddd", ci)} {now.Day} {now.ToString("MMM", ci)}";
            string text = time + dateText;
            if (text == _clockKey) return;

            bool animate = _clockKey.Length > 0;
            _clockKey = text;
            var a = App.Configs.Config.Appearance;
            ClockText.Inlines.Clear();
            ClockText.Inlines.Add(new Run(time)
            {
                FontWeight = IsShibumi ? FontWeights.Normal : FontWeights.SemiBold,
                Foreground = Brush(a.BarForeground)
            });
            ClockText.Inlines.Add(new Run(dateText)
            {
                // Secondary ink, not the accent: the accent is for state, and on
                // a light theme it can sit too close to the paper.
                Foreground = IsShibumi
                    ? SolidColor(ShibumiTokens.From(a, App.Configs.Config.Bar.Shibumi).Sumi)
                    : Brush(a.AccentColor)
            });
            UpdateClockLayout();
            if (animate)
            {
                ClockText.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(240))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        // ---- right: modules -------------------------------------------------

        private void UpdateMetrics()
        {
            RenderWorkspaces();

            var s = App.Metrics.Poll();
            var left = EffectiveModules("left");
            var right = EffectiveModules("right");
            bool compact = _compact;

            string key = string.Join("|", left) + "#" + string.Join("|", right)
                         + (compact ? "|compact" : "");
            if (key != _moduleKey)
            {
                BuildModuleChips(left, right);
                // Store the compact-aware key: otherwise a narrow bar rebuilds its
                // chips on every refresh and the eased values keep resetting.
                _moduleKey = key;
                // Module widths changed: re-evaluate the clock once layout has run.
                Dispatcher.BeginInvoke(UpdateClockLayout, DispatcherPriority.Loaded);
            }

            foreach (var chip in _moduleChips)
            {
                if (!TryFormatModule(chip.Name, s, out string label, out string value, out Color? tint,
                        out double target, out Func<double, string> format))
                    continue;

                chip.Label.Text = label;
                chip.Value.Foreground = new SolidColorBrush(tint ?? UiTheme.Ink);
                chip.Root.ToolTip = DescribeModule(chip.Name);
                chip.SetProgress?.Invoke(chip.Name switch
                {
                    "CPU" => Math.Clamp(s.CpuPercent / 100.0, 0, 1),
                    "RAM" => Math.Clamp(s.RamPercent / 100.0, 0, 1),
                    "Volume" => Math.Clamp(s.VolumePercent / 100.0, 0, 1),
                    "Battery" => Math.Clamp(s.BatteryPercent / 100.0, 0, 1),
                    _ => 0
                });
                if (chip.Detail != null && chip.Name == "Net")
                    chip.Detail.Text = $"↓{FormatSpeed(s.NetDownBps)} ↑{FormatSpeed(s.NetUpBps)}";

                if (format == null)
                {
                    chip.HasNumber = false;
                    chip.Value.Text = value;
                    continue;
                }

                chip.HasNumber = true;
                chip.Format = format;
                chip.Target = target;

                if (!chip.Initialized)
                {
                    chip.Initialized = true;
                    chip.Current = target;
                    chip.Value.Text = value;
                }
            }

            if (!_numberTimer.IsEnabled &&
                _moduleChips.Any(c => c.HasNumber && Math.Abs(c.Target - c.Current) > 0.01))
                _numberTimer.Start();
        }

        /// <summary>
        /// Full configured modules on wide bars; a smaller priority-ordered set on
        /// narrow ones (e.g. a 1536px secondary at 125%) so the right side never clips.
        /// </summary>
        private List<string> EffectiveModules(string section)
        {
            _compact = ActualWidth > 0 && ActualWidth < 1700;

            List<string> modules;
            if (IsShibumi)
            {
                // Sections come from the Shibumi manifests: G1-G7 left, G8 centre,
                // G9-G18 right, with the user's overrides from the Shibumi page.
                modules = ShibumiSuite.KeysFor(section, App.Configs.Config.Bar.Shibumi)
                    .Where(k => !string.Equals(k, "ControlCenter", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(k, "Workspaces", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(k, "Clock", StringComparison.OrdinalIgnoreCase)
                             // G9 is the centre's now-playing pill beside cava, not a
                             // right-dock chip; drawing it twice was the old bug.
                             && !string.Equals(k, "Media", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // The suite is the source of truth in the Shibumi form. Legacy
                // Bar.Modules entries (NetUp/NetDown, …) would duplicate a suite
                // widget as a bare arrow, so they are only used by the other forms.

            }
            else
            {
                if (section == "left") return new List<string>();
                modules = App.Configs.Config.Bar.Modules.ToList();
            }

            if (!_compact) return modules;

            var priority = section == "left"
                ? new[] { "Volume", "CPU", "RAM", "Bluetooth", "Status" }
                : new[] { "Net", "Battery", "Quick", "Bluetooth", "Power", "Brightness", "GPU", "Disk", "Temp" };
            var keep = priority.Take(section == "left" ? 3 : 6).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var filtered = modules.Where(m => keep.Contains(m)).ToList();
            return filtered.Count > 0 ? filtered : modules;
        }

        /// <summary>Eases each module value toward its new reading (~0.5 s).</summary>
        private void TickNumbers()
        {
            bool busy = false;
            foreach (var chip in _moduleChips)
            {
                if (!chip.HasNumber || chip.Format == null) continue;

                double delta = chip.Target - chip.Current;
                if (Math.Abs(delta) < 0.01)
                {
                    chip.Current = chip.Target;
                    chip.Value.Text = chip.Format(chip.Current);
                    continue;
                }

                chip.Current += delta * 0.3;
                if (Math.Abs(chip.Target - chip.Current) < 0.01) chip.Current = chip.Target;
                chip.Value.Text = chip.Format(chip.Current);
                busy = true;
            }

            if (!busy) _numberTimer.Stop();
        }

        /// <summary>
        /// Builds the Shibumi groups: left-section widgets beside the workspaces,
        /// right-section widgets before the control centre. Each is a pill with the
        /// contract's 1 px border, seal hover fill and click-to-panel behaviour.
        /// </summary>
        private void BuildModuleChips(List<string> left, List<string> right)
        {
            _moduleChips.Clear();
            ModulePanel.Children.Clear();
            LeftModulePanel.Children.Clear();

            var a = App.Configs.Config.Appearance;
            var font = UiTheme.ResolveFont(a.FontFamily);
            bool compact = _compact;
            var tokens = ShibumiTokens.From(a, App.Configs.Config.Bar.Shibumi);
            LeftModulePanel.Margin = new Thickness(IsShibumi ? 8 : 0, 0, 0, 0);
            // V1 groups are separated by space, not by a permanent vertical rule.
            // The rule only appears for an explicit split, which is off by default.

            foreach (var name in left.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var chip = CreateModuleChip(name, font, a, tokens, compact);
                if (chip != null) LeftModulePanel.Children.Add(chip);
            }

            // Shibumi splits the right side into status (network/power/audio) and
            // telemetry (GPU/storage/temperature) groups. Quick Access is an
            // affordance, so it belongs last — right beside the control centre.
            var ordered = right.Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => string.Equals(m, "Quick", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            foreach (var name in ordered)
            {
                var chip = CreateModuleChip(name, font, a, tokens, compact);
                if (chip != null) ModulePanel.Children.Add(chip);
            }

            ApplyUpdateNotice();
        }

        /// <summary>Re-renders the quiet "update available" hint in the right dock.</summary>
        public void RefreshUpdateNotice()
        {
            try { ApplyUpdateNotice(); } catch { }
        }

        /// <summary>
        /// The only update UI on the bar: a single small chip naming a newer release,
        /// and only while one exists. It installs nothing - a click opens the command
        /// in Settings, which is where the user decides to run it.
        /// </summary>
        private void ApplyUpdateNotice()
        {
            if (_updateChip != null)
            {
                RightDock.Children.Remove(_updateChip);
                _updateChip = null;
            }

            string latest = UpdateNotice.Latest;
            if (string.IsNullOrEmpty(latest)) return;

            var a = App.Configs.Config.Appearance;
            var font = UiTheme.ResolveFont(a.FontFamily);
            var tokens = ShibumiTokens.From(a, App.Configs.Config.Bar.Shibumi);

            var text = new TextBlock
            {
                Text = "v" + latest + " available",
                FontFamily = font,
                FontSize = IsShibumi ? 11 : Math.Max(9, a.FontSize - 2.5),
                Foreground = IsShibumi
                    ? SolidColor(ShibumiTokens.WithAlpha(tokens.Ink, 0.72))
                    : Brush(a.ModuleLabelColor),
                VerticalAlignment = VerticalAlignment.Center
            };

            var chip = new Border
            {
                Child = text,
                Background = IsShibumi ? Brushes.Transparent : Brush(a.ModuleBackground),
                BorderBrush = IsShibumi ? Brushes.Transparent : Brush(a.ModuleBorder),
                BorderThickness = new Thickness(IsShibumi ? 0 : 1),
                CornerRadius = new CornerRadius(IsShibumi ? 10 : 6),
                Height = IsShibumi ? 24 : double.NaN,
                Padding = IsShibumi ? new Thickness(4, 0, 4, 0) : new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"WorkspaceOS {latest} is available (you have {UpdateNotice.Current}). " +
                          "Click for the command that installs it."
            };
            if (IsShibumi)
            {
                chip.MouseEnter += (_, _) => chip.Background = SolidColor(tokens.FillHover);
                chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
            }
            chip.MouseLeftButtonUp += (_, _) => SettingsWindow.ShowSettings("Advanced");

            if (!_updateNoticeLogged)
            {
                _updateNoticeLogged = true;
                ConfigService.Log($"Update: v{latest} available (this copy is v{UpdateNotice.Current}).");
            }

            int index = RightDock.Children.IndexOf(ControlCenterButton);
            if (index < 0) RightDock.Children.Add(chip);
            else RightDock.Children.Insert(index, chip);
            _updateChip = chip;
        }

        private Border CreateModuleChip(string name, FontFamily font, AppearanceConfig a,
            ShibumiTokens tokens, bool compact)
        {
            if (string.Equals(name, "ControlCenter", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Workspaces", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Clock", StringComparison.OrdinalIgnoreCase))
                return null;

            // A widget with no sensor on this machine is not drawn at all, which is
            // how Shibumi hides batteryless / displayless widgets.
            if (!TryFormatModule(name, App.Metrics?.Latest ?? new SystemSnapshot(),
                    out string label, out string value, out _, out _, out _))
                return null;

            // V1: label is ink at 68%, 12px; the value is full ink, 12px; both use
            // the theme font. Hover is a seal wash, never a bordered dashboard chip.
            var labelBlock = new TextBlock
            {
                Text = label,
                FontFamily = font,
                FontSize = IsShibumi ? (compact ? 10.5 : 12) : compact ? Math.Max(8, a.FontSize - 2.5) : Math.Max(8.5, a.FontSize - 2),
                Foreground = IsShibumi
                    ? SolidColor(ShibumiTokens.WithAlpha(tokens.Ink, 0.68))
                    : Brush(a.ModuleLabelColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            var valueBlock = new TextBlock
            {
                Text = value,
                FontFamily = font,
                FontSize = IsShibumi ? (compact ? 10.5 : 12) : compact ? Math.Max(8.5, a.FontSize - 1.5) : Math.Max(9, a.FontSize - 1),
                Foreground = IsShibumi ? SolidColor(tokens.Ink) : Brush(a.BarForeground),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(IsShibumi ? 5 : string.IsNullOrEmpty(value) ? 0 : 4, 0, 0, 0)
            };

            // The network widget is a glyph plus the interface name: no "NET"
            // label. The wifi glyph's ink is about 8 px wider than its layout
            // box in the embedded Nerd Font, so it paints over the margin; 14 px
            // leaves the same ~6 px gap the other widgets use.
            if (IsShibumi && name == "Net") valueBlock.Margin = new Thickness(14, 0, 0, 0);

            // The V1 widgets are not label/value text: CPU has its wave, MEM a
            // ring, VOL a capsule, NET a glyph and rates. These are the pieces
            // that make the bar read as Shibumi instead of a generic strip.
            FrameworkElement visual = null;
            TextBlock detail = null;
            Action<double> setProgress = null;
            if (IsShibumi)
            {
                switch (name)
                {
                    case "CPU":
                        visual = BuildSparkline(tokens, 24, 9, out setProgress);
                        break;
                    case "RAM":
                        visual = BuildRing(tokens, 12, out setProgress);
                        break;
                    case "Battery":
                        visual = BuildRing(tokens, 12, out setProgress);
                        break;
                    case "Volume":
                        visual = BuildMeter(tokens, 24, 4, out setProgress);
                        break;
                    case "Net":
                        visual = new TextBlock
                        {
                            Text = "\uf1eb",
                            FontFamily = font,
                            FontSize = 12.5,
                            Foreground = SolidColor(tokens.Ink),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        detail = new TextBlock
                        {
                            FontFamily = font,
                            FontSize = 9.5,
                            Foreground = SolidColor(ShibumiTokens.WithAlpha(tokens.Ink, 0.68)),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(8, 0, 0, 0)
                        };
                        break;
                }
            }

            var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            // An empty label (Net) should not occupy a slot at all.
            if (!string.IsNullOrEmpty(label)) stack.Children.Add(labelBlock);
            if (visual != null)
            {
                if (visual.Margin == default) visual.Margin = new Thickness(5, 0, 0, 0);
                stack.Children.Add(visual);
            }
            stack.Children.Add(valueBlock);
            if (detail != null) stack.Children.Add(detail);

            var fill = IsShibumi ? Brushes.Transparent : Brush(a.ModuleBackground);
            var chip = new Border
            {
                Child = stack,
                Background = fill,
                BorderBrush = IsShibumi ? Brushes.Transparent : Brush(a.ModuleBorder),
                BorderThickness = new Thickness(IsShibumi ? 0 : 1),
                CornerRadius = new CornerRadius(IsShibumi ? 10 : 6),
                Height = IsShibumi ? 24 : double.NaN,
                                Padding = IsShibumi
                    ? new Thickness(4, 0, 4, 0)
                    : compact ? new Thickness(6, 2, 6, 2) : new Thickness(9, 3, 9, 3),
                // Shibumi's right group keeps a clear gutter between widgets;
                // anything tighter reads as one run of bunched glyphs.
                Margin = IsShibumi
                    ? new Thickness(0, 0, 10, 0)
                    : compact ? new Thickness(2, 0, 2, 0) : new Thickness(3, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = DescribeModule(name)
            };
            if (IsShibumi)
            {
                chip.MouseEnter += (_, _) => chip.Background = SolidColor(tokens.FillHover);
                chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
            }
            chip.MouseLeftButtonUp += (_, _) => ShibumiWidgetPanels.Toggle(name, chip, this);

            _moduleChips.Add(new ModuleChip
            {
                Name = name, Root = chip, Label = labelBlock, Value = valueBlock,
                Detail = detail, SetProgress = setProgress
            });
            return chip;
        }

        private static SolidColorBrush SolidColor(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>V1 CPU wave: an ink polyline over the last twelve readings.</summary>
        private static FrameworkElement BuildSparkline(ShibumiTokens tokens, double width, double height,
            out Action<double> update)
        {
            var line = new Polyline
            {
                Stroke = SolidColor(tokens.Ink),
                StrokeThickness = 1,
                StrokeLineJoin = PenLineJoin.Round
            };
            var canvas = new Canvas
            {
                Width = width,
                Height = height,
                VerticalAlignment = VerticalAlignment.Center
            };
            canvas.Children.Add(line);
            var history = new List<double>(Enumerable.Repeat(0.0, 12));
            update = value =>
            {
                value = Math.Clamp(value, 0, 1);
                // Seed a new graph at the current level instead of drawing a
                // line pinned to the floor while the history fills.
                if (value > 0 && history.All(v => v == 0))
                    for (int i = 0; i < history.Count; i++) history[i] = value;
                history.Add(value);
                if (history.Count > 12) history.RemoveAt(0);
                var points = new PointCollection(history.Count);
                for (int i = 0; i < history.Count; i++)
                {
                    double x = history.Count == 1 ? 0 : i * (width / (history.Count - 1));
                    double y = height - 1 - history[i] * (height - 2);
                    points.Add(new Point(x, y));
                }
                line.Points = points;
            };
            update(0);
            return canvas;
        }

        /// <summary>V1 memory/battery ring: a dashed ellipse, rotated to start at 12 o'clock.</summary>
        private static FrameworkElement BuildRing(ShibumiTokens tokens, double size,
            out Action<double> update)
        {
            double radius = (size - 1.5) / 2;
            double circumference = Math.PI * 2 * radius / 1.5;
            var track = new Ellipse
            {
                Width = size,
                Height = size,
                Stroke = SolidColor(ShibumiTokens.WithAlpha(tokens.Ink, 0.18)),
                StrokeThickness = 1.5
            };
            var arc = new Ellipse
            {
                Width = size,
                Height = size,
                Stroke = SolidColor(tokens.Ink),
                StrokeThickness = 1.5,
                StrokeDashCap = PenLineCap.Round,
                RenderTransform = new RotateTransform(-90),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(track);
            grid.Children.Add(arc);
            update = value =>
            {
                double filled = Math.Clamp(value, 0, 1) * circumference;
                arc.StrokeDashArray = new DoubleCollection { Math.Max(0.001, filled), circumference };
            };
            update(0);
            return grid;
        }

        /// <summary>V1 audio capsule: a 24×4 track with an ink fill.</summary>
        private static FrameworkElement BuildMeter(ShibumiTokens tokens, double width, double height,
            out Action<double> update)
        {
            var track = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(height / 2),
                Background = SolidColor(ShibumiTokens.WithAlpha(tokens.Ink, 0.18))
            };
            var fill = new Border
            {
                Height = height,
                Width = 0,
                CornerRadius = new CornerRadius(height / 2),
                Background = SolidColor(tokens.Ink),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var grid = new Grid { Width = width, Height = height, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(track);
            grid.Children.Add(fill);
            update = value => fill.Width = Math.Max(0, Math.Clamp(value, 0, 1) * width);
            update(0);
            return grid;
        }

        /// <summary>The V1 hairline between two widget groups.</summary>
        private static Border GroupRule(ShibumiTokens tokens) => new()
        {
            Width = 1,
            Height = 13,
            Margin = new Thickness(5, 0, 5, 0),
            Background = SolidColor(tokens.Separator),
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>
        /// Formats one module. Returns false when the machine has no such sensor.
        /// Also returns the raw numeric target and a formatter so the bar can
        /// interpolate instead of jumping between readings.
        /// </summary>
        private static bool TryFormatModule(string module, SystemSnapshot s,
            out string label, out string value, out Color? tint,
            out double target, out Func<double, string> format)
        {
            tint = null;
            target = 0;
            format = null;

            switch (module)
            {
                case "CPU":
                    label = "CPU"; target = s.CpuPercent; format = v => $"{v:00}%";
                    value = format(target); return true;
                case "RAM":
                {
                    // Shibumi shows the used figure as a two-digit GiB badge
                    // ("07G"); the full used/total string is the tooltip.
                    target = (s.RamTotal - s.RamAvailable) / 1024.0 / 1024 / 1024;
                    label = "MEM"; format = v => $"{v:00}G";
                    value = format(target); return true;
                }
                case "GPU":
                    label = "GPU";
                    if (s.GpuPercent < 0) { value = "--"; return true; }
                    target = s.GpuPercent; format = v => $"{v:0}%";
                    value = format(target); return true;
                case "Disk":
                    label = "DSK";
                    if (s.DiskTotalBytes <= 0) { value = "--"; return true; }
                    target = s.DiskFreeBytes / 1024.0 / 1024 / 1024;
                    format = v => $"{v:0}G"; value = format(target); return true;
                case "Temp":
                {
                    float temp = s.CpuTempC > 0 ? s.CpuTempC : s.GpuTempC;
                    if (temp <= 0) { label = value = ""; return false; }
                    label = "TMP"; target = temp; format = v => $"{v:0}°";
                    value = format(target); return true;
                }
                case "Net":
                {
                    string name = PlatformInfo.PrimaryNetwork();
                    if (string.IsNullOrWhiteSpace(name) || name == "Offline") { label = value = ""; return false; }
                    label = ""; value = name.Length > 18 ? name.Substring(0, 17) + "…" : name;
                    return true;
                }
                case "Brightness":
                    if (!PlatformInfo.HasBrightness) { label = value = ""; return false; }
                    label = "BRI"; target = PlatformInfo.Brightness;
                    format = v => $"{v:0}%"; value = format(target); return true;
                case "Power":
                {
                    string scheme = PlatformInfo.ActivePowerScheme();
                    if (string.IsNullOrWhiteSpace(scheme)) { label = value = ""; return false; }
                    label = "PWR";
                    // V1's short names: Bal / Perf / Sav.
                    value = scheme.Contains("High", StringComparison.OrdinalIgnoreCase) ? "Perf"
                        : scheme.Contains("Saver", StringComparison.OrdinalIgnoreCase) ? "Sav"
                        : "Bal";
                    return true;
                }
                case "Bluetooth":
                {
                    string radio = PlatformInfo.BluetoothRadio();
                    if (string.IsNullOrWhiteSpace(radio)) { label = value = ""; return false; }
                    label = "BT";
                    value = radio.Length > 12 ? radio.Substring(0, 11) + "…" : radio;
                    return true;
                }
                case "Quick":
                    // G10 is an affordance, not a decorative star.
                    label = "\uf013"; value = ""; return true;
                case "Status":
                    label = "\u25CF"; value = ""; return true;
                case "Update":
                    label = "UPD"; value = ""; return true;
                case "NetUp":
                    label = "↑"; target = Math.Max(0, s.NetUpBps); format = FormatSpeed;
                    value = format(target); return true;
                case "NetDown":
                    label = "↓"; target = Math.Max(0, s.NetDownBps); format = FormatSpeed;
                    value = format(target); return true;
                case "Battery":
                    if (s.BatteryPercent < 0) { label = value = ""; return false; }
                    label = "BAT"; target = s.BatteryPercent;
                    format = v => $"{v:0}%{(s.OnAc ? " \uf0e7" : "")}";
                    value = format(target);
                    if (s.OnAc) tint = UiTheme.Green;
                    else if (s.BatteryPercent <= 10) tint = UiTheme.Accent;
                    else if (s.BatteryPercent <= 20) tint = UiTheme.Yellow;
                    return true;
                case "Volume":
                    if (s.VolumePercent < 0) { label = value = ""; return false; }
                    label = "VOL"; target = s.VolumePercent;
                    format = s.VolumeMuted ? (v => "mute") : (v => $"{v:00}%");
                    value = format(target);
                    if (s.VolumeMuted) tint = UiTheme.Sumi;
                    return true;
                default:
                    label = value = "";
                    return false;
            }
        }

        private static string DescribeModule(string module) => module switch
        {
            "CPU" => "Processor load",
            "RAM" => "Memory used / total",
            "GPU" => "GPU engine utilization",
            "Disk" => "Free space on the system drive",
            "NetUp" => "Upload rate",
            "NetDown" => "Download rate",
            "Battery" => "Battery charge (＋ = on AC)",
            "Volume" => "Master volume",
            "Net" => "Network interface (click for details)",
            "Brightness" => "Display brightness",
            "Power" => "Active Windows power scheme",
            "Bluetooth" => "Bluetooth radio",
            "Temp" => "CPU / GPU temperature",
            "Quick" => "Quick access — themes, wallpaper, settings",
            "Status" => "Notifications",
            "Update" => "Windows Update",
            _ => module
        };

        private static string FormatRam(SystemSnapshot s)
        {
            if (s.RamTotal == 0) return "--";
            double usedGb = (s.RamTotal - s.RamAvailable) / 1024.0 / 1024 / 1024;
            double totalGb = s.RamTotal / 1024.0 / 1024 / 1024;
            return $"{usedGb:0.0}/{totalGb:0.0}G";
        }

        private static string FormatSpeed(double bps)
        {
            if (bps < 0) return "--";
            if (bps < 1024 * 1024) return $"{bps / 1024:0}K";
            if (bps < 1024L * 1024 * 1024) return $"{bps / (1024.0 * 1024):0.0}M";
            return $"{bps / (1024.0 * 1024 * 1024):0.0}G";
        }

        private static string FormatDisk(SystemSnapshot s)
        {
            if (s.DiskTotalBytes <= 0) return "--";
            double freeGb = s.DiskFreeBytes / 1024.0 / 1024 / 1024;
            return $"{freeGb:0}G";
        }

        /// <summary>
        /// Keeps the clock readable on every monitor: centre it across the whole
        /// bar only when that clear space exists; otherwise let the middle column
        /// hold (and trim) it, so it can never run into the module chips.
        /// </summary>
        private void UpdateClockLayout()
        {
            if (ClockText == null || LeftDock == null || RightDock == null) return;
            double width = ActualWidth;
            if (width <= 0) return;

            ClockText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double clock = ClockText.DesiredSize.Width;
            if (clock <= 0) return;

            // A notch is a pill that hugs its content, so the clock cannot be
            // centred across the window: the whole centre dock goes in the middle
            // column between the workspace chips and the modules. That column is
            // measured, so the pill grows around the clock instead of drawing it
            // underneath the modules.
            if (string.Equals(App.Configs.Config.Bar.Profile, "Notch", StringComparison.OrdinalIgnoreCase))
            {
                if (Grid.GetColumn(CenterDock) != 1) Grid.SetColumn(CenterDock, 1);
                if (Grid.GetColumnSpan(CenterDock) != 1) Grid.SetColumnSpan(CenterDock, 1);
                ClockText.TextTrimming = TextTrimming.CharacterEllipsis;
                return;
            }

            // Other forms centre the clock across the whole bar again.
            if (Grid.GetColumn(CenterDock) != 0) Grid.SetColumn(CenterDock, 0);
            if (Grid.GetColumnSpan(CenterDock) != 3) Grid.SetColumnSpan(CenterDock, 3);

            double left = LeftDock.ActualWidth + LeftDock.Margin.Left;
            double right = RightDock.ActualWidth + RightDock.Margin.Right;
            double center = width / 2;
            bool fitsCentered = left > 0 && right > 0
                && center - clock / 2 >= left + 12
                && center + clock / 2 <= width - right - 12;

            int column = fitsCentered ? 0 : 1;
            int span = fitsCentered ? 3 : 1;
            if (Grid.GetColumn(ClockText) != column) Grid.SetColumn(ClockText, column);
            if (Grid.GetColumnSpan(ClockText) != span) Grid.SetColumnSpan(ClockText, span);
            ClockText.TextTrimming = fitsCentered ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        }

        // ---- logo menu: bar form, themes, logos, actions -------------------

        private readonly DispatcherTimer _cavaTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly List<Rectangle> _cavaBars = new();

        /// <summary>
        /// Renders the whole bar surface to a PNG for visual checks. The bar is a
        /// layered appbar and the desktop may be locked, so WPF draws its own
        /// visual tree instead of a screen grab. Dev-only, like the menu snapshot.
        /// </summary>
        public bool SnapshotFull(string path)
        {
            try
            {
                UpdateLayout();
                int width = (int)Math.Ceiling(Math.Max(1, ActualWidth));
                int height = (int)Math.Ceiling(Math.Max(1, ActualHeight));
                var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    width, height, 96, 96, PixelFormats.Pbgra32);
                target.Render(Root);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
                using var stream = File.Create(path);
                encoder.Save(stream);
                return true;
            }
            catch (Exception ex)
            {
                ConfigService.Log("Snapshot: bar render failed (" + ex.Message + ")");
                return false;
            }
        }

        /// <summary>
        /// Renders the open logo menu to a PNG. Visual checks cannot use a screen
        /// grab when the desktop is locked, and the popup is a layered window, so
        /// WPF draws it itself. Returns false when there is nothing to render.
        /// </summary>
        public bool SnapshotMenu(string path)
        {
            bool ok = false;
            Dispatcher.Invoke(() =>
            {
                BuildMenu();
                MenuPopup.IsOpen = true;
                MenuPopup.Child?.UpdateLayout();
                MenuBorder.UpdateLayout();

                int w = (int)Math.Ceiling(MenuBorder.ActualWidth);
                int h = (int)Math.Ceiling(MenuBorder.ActualHeight);
                if (w > 0 && h > 0)
                {
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(MenuBorder);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using (var fs = System.IO.File.Create(path)) enc.Save(fs);
                    ok = true;
                }
                MenuPopup.IsOpen = false;
            });
            return ok;
        }

        /// <summary>
        /// Renders the workspace chips to a PNG and reports the active chip's
        /// current pop scale. The padding keeps the 1.28× label from being
        /// clipped, so a switch can be checked frame by frame.
        /// </summary>
        public bool SnapshotWorkspaces(string path, out double scale)
        {
            double captured = 0;
            bool ok = false;
            Dispatcher.Invoke(() =>
            {
                var text = _workspaceChips.FirstOrDefault(c => c.Index == _lastActiveIndex)?.Text;
                captured = text?.RenderTransform is ScaleTransform s ? s.ScaleX : 1;

                WorkspaceHost.UpdateLayout();
                double width = WorkspaceHost.ActualWidth, height = WorkspaceHost.ActualHeight;
                if (width <= 0 || height <= 0) return;

                const int pad = 8;
                var drawing = new DrawingVisual();
                using (var dc = drawing.RenderOpen())
                    dc.DrawRectangle(new VisualBrush(WorkspaceHost), null,
                        new Rect(pad, pad, width, height));

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(width) + pad * 2, (int)Math.Ceiling(height) + pad * 2,
                    96, 96, PixelFormats.Pbgra32);
                rtb.Render(drawing);

                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using (var fs = File.Create(path)) enc.Save(fs);
                ok = true;
            });
            scale = captured;
            return ok;
        }

        private void ControlCenterButton_Click(object sender, RoutedEventArgs e)
            => ControlCenterWindow.Toggle();

        private void LogoButton_Click(object sender, RoutedEventArgs e)
        {
            if (MenuPopup.IsOpen) { MenuPopup.IsOpen = false; return; }
            BuildMenu();
            MenuPopup.IsOpen = true;
            StartCalendarDismissHook();
        }

        private void BuildMenu()
        {
            var cfg = App.Configs.Config;
            var a = cfg.Appearance;
            var font = UiTheme.ResolveFont(a.FontFamily);
            MenuHost.Children.Clear();

            MenuHost.Children.Add(MenuHeading("BAR FORM", font, a, () => App.SetBarProfile("Full")));
            var forms = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
            // Shibumi first: it is the default form and the one this port is about.
            foreach (var profile in new[] { "Shibumi", "Full", "Fit", "Dock", "Notch" })
            {
                string name = profile;
                forms.Children.Add(MenuChip(name,
                    string.Equals(cfg.Bar.Profile, name, StringComparison.OrdinalIgnoreCase),
                    font, a, () => { App.SetBarProfile(name); BuildMenu(); }));
            }
            MenuHost.Children.Add(forms);

            var theme = ThemeLibrary.Find(a.BarTheme);
            MenuHost.Children.Add(MenuHeading("THEME — " + (theme?.Name ?? "Custom").ToUpperInvariant(), font, a,
                () => { App.CycleTheme(1); BuildMenu(); }));

            var themeList = new StackPanel();
            foreach (var item in ThemeLibrary.All)
            {
                var row = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(7, 5, 7, 5),
                    Background = Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                var swatch = new Border
                {
                    Width = 11, Height = 11, CornerRadius = new CornerRadius(3),
                    Background = Brush(item.Accent), Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                bool active = string.Equals(item.Name, a.BarTheme, StringComparison.OrdinalIgnoreCase);
                var label = new TextBlock
                {
                    Text = item.Name,
                    FontFamily = font,
                    FontSize = Math.Max(10, a.FontSize - 1.5),
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = active ? Brush(item.Accent) : Brush(a.BarForeground),
                    VerticalAlignment = VerticalAlignment.Center
                };
                line.Children.Add(swatch);
                line.Children.Add(label);
                row.Child = line;
                string themeName = item.Name;
                row.MouseEnter += (_, _) => row.Background = Brush(ThemeColor.Alpha(a.ModuleBackground, 0xCC));
                row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
                row.MouseLeftButtonUp += (_, _) => { App.SetTheme(themeName); BuildMenu(); };
                themeList.Children.Add(row);
            }
            var themeScroll = new ScrollViewer
            {
                MaxHeight = 232,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = themeList,
                Margin = new Thickness(0, 0, 0, 8)
            };
            // The stock WPF scrollbar is a light system control and reads as a
            // mistake on a dark Shibumi surface. Six pixels, theme ink, no arrows.
            var scrollStyle = MenuScrollBar(a);
            if (scrollStyle != null)
                themeScroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), scrollStyle);
            MenuHost.Children.Add(themeScroll);

            MenuHost.Children.Add(MenuHeading("LOGO", font, a, null));
            var logos = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
            foreach (var logo in BarIconCatalog.Names)
            {
                string name = logo;
                logos.Children.Add(MenuChip(name,
                    string.Equals(cfg.Appearance.BarIcon, name, StringComparison.OrdinalIgnoreCase),
                    font, a, () =>
                    {
                        cfg.Appearance.BarIcon = name;
                        App.Configs.Save();
                        App.Bars?.ApplyConfig();
                        BuildMenu();
                    }));
            }
            MenuHost.Children.Add(logos);

            var actions = new WrapPanel();
            actions.Children.Add(MenuChip("Shibumi shell", false, font, a,
                () => { ShibumiSettingsWindow.ShowWindow(); BuildMenu(); }));
            actions.Children.Add(MenuChip("Copy taskbar preset", false, font, a, CopyTaskbarPreset));
            actions.Children.Add(MenuChip("Settings", false, font, a, SettingsWindow.ShowSettings));
            actions.Children.Add(MenuChip("Task Manager", false, font, a, MonitorWindow.OpenTaskManager));
            actions.Children.Add(MenuChip("Display", false, font, a,
                () => Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true })));
            MenuHost.Children.Add(actions);
        }

        /// <summary>
        /// Six-pixel, theme-inked scrollbar for the menu's theme list. The stock
        /// WPF scrollbar is a light system control that looks pasted on.
        /// </summary>
        private static Style MenuScrollBar(AppearanceConfig a)
        {
            string thumb = ThemeColor.Alpha(a.BarForeground, 0x55);
            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>" +
                "<Setter Property='Width' Value='6'/>" +
                "<Setter Property='MinWidth' Value='6'/>" +
                "<Setter Property='Background' Value='Transparent'/>" +
                "<Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='ScrollBar'><Grid Background='Transparent'>" +
                "<Track x:Name='PART_Track' IsDirectionReversed='True' Focusable='False' " +
                "Maximum='{TemplateBinding Maximum}' Minimum='{TemplateBinding Minimum}' " +
                "Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>" +
                "<Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>" +
                "<Track.Thumb><Thumb MinHeight='26'><Thumb.Template><ControlTemplate TargetType='Thumb'>" +
                "<Border CornerRadius='3' Background='" + thumb + "'/>" +
                "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>" +
                "</Track></Grid></ControlTemplate></Setter.Value></Setter></Style>";
            try { return (Style)System.Windows.Markup.XamlReader.Parse(xaml); }
            catch (Exception ex)
            {
                // A cosmetic scrollbar must never take the whole menu down.
                ConfigService.Log("Menu: scrollbar style failed (" + ex.Message + ")");
                return null;
            }
        }

        private FrameworkElement MenuHeading(string text, FontFamily font, AppearanceConfig a, Action onCycle)
        {
            var grid = new Grid { Margin = new Thickness(2, 6, 2, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = text,
                FontFamily = font,
                FontSize = 9,
                Foreground = Brush(a.AccentColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            if (onCycle != null)
            {
                var next = new TextBlock
                {
                    Text = "next \u203A",
                    FontFamily = font,
                    FontSize = 9,
                    Foreground = Brush(a.ModuleLabelColor),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                next.MouseLeftButtonUp += (_, _) => onCycle();
                Grid.SetColumn(next, 1);
                grid.Children.Add(next);
            }
            return grid;
        }

        private Border MenuChip(string text, bool active, FontFamily font, AppearanceConfig a, Action onClick)
        {
            var label = new TextBlock
            {
                Text = text,
                FontFamily = font,
                FontSize = Math.Max(10, a.FontSize - 1.5),
                Foreground = active ? Brush(a.ActiveWorkspaceForeground) : Brush(a.BarForeground),
                VerticalAlignment = VerticalAlignment.Center
            };
            var chip = new Border
            {
                Child = label,
                CornerRadius = new CornerRadius(IsShibumi ? PillRadius : 7),
                Padding = IsShibumi
                    ? new Thickness(PillPadX, PillPadY, PillPadX, PillPadY)
                    : new Thickness(9, 4, 9, 4),
                Margin = IsShibumi ? new Thickness(0, 2, 5, 2) : new Thickness(0, 2, 4, 2),
                Background = active ? Brush(a.ActiveWorkspaceBackground) : Brush(ThemeColor.Alpha(a.ModuleBackground, 0xCC)),
                BorderBrush = Brush(a.ModuleBorder),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            chip.MouseLeftButtonUp += (_, _) => onClick();
            return chip;
        }

        private void CopyTaskbarPreset()
        {
            try
            {
                string path = WindhawkPreset.Write(App.Configs.Config.Appearance);
                System.Windows.Clipboard.SetText(File.ReadAllText(path));
                ConfigService.Log($"Taskbar: preset copied and saved to {path}.");
            }
            catch (Exception ex)
            {
                ConfigService.Log("Taskbar: could not copy the preset (" + ex.Message + ").");
            }
        }

        private void DisplaySettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ConfigService.Log($"Display settings: could not open ({ex.Message}).");
            }
        }

        private void TaskManagerBtn_Click(object sender, RoutedEventArgs e) => MonitorWindow.OpenTaskManager();

        private void SettingsBtn_Click(object sender, RoutedEventArgs e) => SettingsWindow.ShowSettings();

        // ---- calendar popover ----------------------------------------------

        private DateTime _calMonth = DateTime.Today;
        private static readonly string[] DayHeaders = { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };

        private void ClockText_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (CalendarPopup.IsOpen) { CalendarPopup.IsOpen = false; return; }
            _calMonth = DateTime.Today;
            BuildCalendar();
            CalendarPopup.IsOpen = true;
            StartCalendarDismissHook();
        }

        // ---- click-away dismiss -------------------------------------------
        // The bar is WS_EX_NOACTIVATE, so WPF's popup capture is unreliable;
        // a low-level mouse hook makes any click outside the calendar close it.

        private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;

        private void StartCalendarDismissHook()
        {
            if (_calendarHook != IntPtr.Zero) return;
            _calendarHookProc = CalendarMouseHook;
            _calendarHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _calendarHookProc, IntPtr.Zero, 0);
            if (_calendarHook == IntPtr.Zero)
                ConfigService.Log("Calendar: click-away hook could not be installed.");

            _menuKeys.Install();
        }

        private void StopCalendarDismissHook()
        {
            _menuKeys.Dispose();

            if (_calendarHook == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_calendarHook);
            _calendarHook = IntPtr.Zero;
            _calendarHookProc = null;
        }

        private const int VK_ESCAPE = 0x1B;

        /// <summary>
        /// Escape closes the bar's own popups even when focus moved to the app
        /// underneath: the bar never activates, so WPF key events miss it.
        /// </summary>
        private bool OnMenuKey(int vk)
        {
            if (vk != VK_ESCAPE || (!CalendarPopup.IsOpen && !MenuPopup.IsOpen)) return false;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CalendarPopup.IsOpen = false;
                MenuPopup.IsOpen = false;
            }));
            return true;
        }

        private IntPtr CalendarMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    if (!IsOverCalendarOrClock(info.X, info.Y))
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            CalendarPopup.IsOpen = false;
                            MenuPopup.IsOpen = false;
                        }));
                }
            }
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private bool IsOverCalendarOrClock(int x, int y)
        {
            try
            {
                if (InScreenRect(ClockText, x, y)) return true;   // clicking the clock toggles
                if (InScreenRect(LogoButton, x, y)) return true;  // clicking the logo toggles
                if (CalendarPopup.IsOpen && CalendarPopup.Child is FrameworkElement child && child.ActualWidth > 0)
                    return InScreenRect(child, x, y);
                if (MenuPopup.IsOpen && MenuBorder.ActualWidth > 0 && InScreenRect(MenuBorder, x, y))
                    return true;
            }
            catch { }
            return false;
        }

        private static bool InScreenRect(FrameworkElement el, int x, int y)
        {
            if (!el.IsVisible) return false;
            var topLeft = el.PointToScreen(new Point(0, 0));
            var bottomRight = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            return x >= topLeft.X && x <= bottomRight.X && y >= topLeft.Y && y <= bottomRight.Y;
        }

        private void BuildCalendar()
        {
            var a = App.Configs.Config.Appearance;
            var font = UiTheme.ResolveFont(a.FontFamily);
            var panel = (StackPanel)CalendarHost;
            panel.Children.Clear();

            // Header: ◀ Month YYYY ▶
            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var prev = CalNavButton("\u25C0", () => { _calMonth = _calMonth.AddMonths(-1); BuildCalendar(); });
            Grid.SetColumn(prev, 0);
            var next = CalNavButton("\u25B6", () => { _calMonth = _calMonth.AddMonths(1); BuildCalendar(); });
            Grid.SetColumn(next, 2);
            var title = new TextBlock
            {
                Text = _calMonth.ToString("MMMM yyyy"),
                FontFamily = font, FontSize = a.FontSize, FontWeight = FontWeights.SemiBold,
                Foreground = Brush(a.BarForeground), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(prev); header.Children.Add(title); header.Children.Add(next);
            panel.Children.Add(header);

            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            for (int c = 0; c < 7; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            for (int r = 0; r < 6; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(25) });

            var dow = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            for (int c = 0; c < 7; c++)
            {
                var t = new TextBlock
                {
                    Text = DayHeaders[c], FontFamily = font, FontSize = 10,
                    Foreground = Brush(a.ModuleLabelColor), HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(t, c);
                grid.Children.Add(t);
            }

            var first = new DateTime(_calMonth.Year, _calMonth.Month, 1);
            int lead = ((int)first.DayOfWeek + 6) % 7;   // Monday-first offset
            int days = DateTime.DaysInMonth(_calMonth.Year, _calMonth.Month);
            for (int d = 0; d < days; d++)
            {
                int cell = lead + d, row = cell / 7 + 1, col = cell % 7;
                bool isToday = first.AddDays(d) == DateTime.Today;
                var tile = new Border
                {
                    Width = 23, Height = 23, CornerRadius = new CornerRadius(4),
                    Background = isToday ? new SolidColorBrush(ColorOf(a.ActiveWorkspaceBackground)) : Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = (d + 1).ToString(), FontFamily = font, FontSize = 10.5,
                        FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = isToday ? new SolidColorBrush(UiTheme.Paper) : Brush(a.BarForeground),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Grid.SetRow(tile, row); Grid.SetColumn(tile, col);
                grid.Children.Add(tile);
            }
            panel.Children.Add(grid);
        }

        private Button CalNavButton(string glyph, Action onClick)
        {
            var a = App.Configs.Config.Appearance;
            var tb = new TextBlock
            {
                Text = glyph,
                FontFamily = UiTheme.ResolveFont(a.FontFamily),
                FontSize = 9,
                Foreground = Brush(a.ModuleLabelColor)
            };
            var b = new Button
            {
                Content = tb, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(8, 3, 8, 3),
                Focusable = false
            };
            b.Template = CalNavTemplate();
            b.Click += (_, _) => onClick();
            return b;
        }

        private static ControlTemplate CalNavTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, UiTheme.Brush(UiTheme.SurfaceRaised)));
            template.Triggers.Add(hover);
            return template;
        }
    }
}
