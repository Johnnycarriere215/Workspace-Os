using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.Metrics;

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
        private string _clockKey = "";
        private IntPtr _calendarHook;
        private NativeMethods.LowLevelMouseProc _calendarHookProc;

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

            // Smoothly interpolated numeric display (percent, GB, bytes/s).
            public double Current;
            public double Target;
            public bool HasNumber;
            public bool Initialized;
            public Func<double, string> Format;
        }

        private readonly List<WorkspaceChip> _workspaceChips = new();
        private readonly List<ModuleChip> _moduleChips = new();
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
            SizeChanged += (_, _) => UpdateClockLayout();

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
            Root.Background = Brush(a.BarBackground);
            Background = Root.Background;

            var font = UiTheme.ResolveFont(a.FontFamily);
            ClockText.FontFamily = font;
            ClockText.FontSize = Math.Max(9, a.FontSize);
            ClockText.Foreground = Brush(a.BarForeground);
            BarHairline.Fill = Brush(a.SeparatorColor);

            _metricsTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(500, App.Configs.Config.Bar.RefreshMs));

            _chipCount = -1;          // force a chip rebuild on the next render
            _moduleKey = "";

            RenderWorkspaces();
            UpdateMetrics();
            Dispatcher.BeginInvoke(UpdateClockLayout, DispatcherPriority.Loaded);
            if (_appBarRegistered) PositionBar();
        }

        private static SolidColorBrush Brush(string hex) => new(ColorOf(hex));

        private static Color ColorOf(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return UiTheme.Accent; }
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
            int barHeight = App.Configs.Config.Appearance.BarHeight;
            // Reserve physical pixels for this monitor's own DPI, not the primary's.
            int dpi = NativeMethods.GetMonitorDpi(_monitorHandle);
            int barPx = Math.Max(20, (int)Math.Round(barHeight * dpi / 96.0));
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
            int activeIndex = App.Workspaces.ActiveWorkspace;
            var occupancy = App.Workspaces.GetOccupancy();

            if (_chipCount != count)
            {
                BuildWorkspaceChips(count);
                _chipCount = count;
                _lastActiveIndex = -1;   // place the pill without animating after a rebuild
            }

            var font = UiTheme.ResolveFont(a.FontFamily);
            var accent = Brush(a.ActiveWorkspaceBackground);
            var onAccent = Brush(a.ActiveWorkspaceForeground);
            var occupiedBrush = Brush(a.BarForeground);
            var emptyBrush = Brush(a.AccentColor);
            ActivePill.Background = accent;
            if (ActivePill.Effect is DropShadowEffect glow) glow.Color = accent.Color;

            foreach (var chip in _workspaceChips)
            {
                int i = chip.Index;
                bool active = i == activeIndex;
                bool pending = i > openCount;            // slot exists visually, desktop does not yet
                bool occupied = i - 1 < occupancy.Length && occupancy[i - 1];
                var wsCfg = cfg.Workspaces.FirstOrDefault(w => w.Index == i);
                string label = string.IsNullOrWhiteSpace(wsCfg?.Name) ? i.ToString() : wsCfg.Name;

                chip.Text.Text = label;
                chip.Text.FontFamily = font;
                chip.Text.FontSize = Math.Max(9, a.FontSize - 1);
                chip.Text.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                chip.Root.Background = Brushes.Transparent;
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
                UpdateActivePill(activeIndex, animate: _lastActiveIndex >= 0);
                _lastActiveIndex = activeIndex;
            }
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
                    VerticalAlignment = VerticalAlignment.Center
                };
                var chip = new Border
                {
                    Child = text,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(1, 0, 1, 0),
                    Background = Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };
                chip.MouseEnter += (_, _) =>
                {
                    if (chip.Tag is bool active && active) return;
                    chip.Background = UiTheme.Brush(UiTheme.SurfaceRaised);
                };
                chip.MouseLeave += (_, _) =>
                {
                    if (chip.Tag is bool active && active) return;
                    chip.Background = Brushes.Transparent;
                };

                int idx = i;
                chip.MouseLeftButtonUp += (_, _) => App.Workspaces.SwitchTo(idx);

                _workspaceChips.Add(new WorkspaceChip { Index = i, Root = chip, Text = text, Foreground = fg });
                WorkspacePanel.Children.Add(chip);
            }
        }

        // ---- center: clock -------------------------------------------------

        private void UpdateClock()
        {
            var now = DateTime.Now;
            var ci = CultureInfo.InvariantCulture;
            string time = App.Configs.Config.Bar.ShowSeconds ? now.ToString("HH:mm:ss", ci) : now.ToString("HH:mm", ci);
            // "14:37  Thu 9 Jul"
            string text = $"{time}  {now.ToString("ddd", ci)} {now.Day} {now.ToString("MMM", ci)}";
            if (text == ClockText.Text) return;

            bool animate = _clockKey.Length > 0;
            _clockKey = text;
            ClockText.Text = text;
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
            var modules = EffectiveModules();
            bool compact = _compact;

            string key = string.Join("|", modules) + (compact ? "|compact" : "");
            if (key != _moduleKey)
            {
                BuildModuleChips(modules);
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
        private List<string> EffectiveModules()
        {
            var configured = App.Configs.Config.Bar.Modules;
            _compact = ActualWidth > 0 && ActualWidth < 1700;
            if (!_compact) return configured;

            var priority = new[] { "CPU", "RAM", "GPU", "NetDown", "Battery", "Volume", "Disk", "NetUp" };
            var keep = priority.Take(6).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var filtered = configured.Where(m => keep.Contains(m)).ToList();
            return filtered.Count > 0 ? filtered : configured;
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

        private void BuildModuleChips(List<string> modules)
        {
            _moduleKey = string.Join("|", modules);
            _moduleChips.Clear();
            ModulePanel.Children.Clear();

            var a = App.Configs.Config.Appearance;
            var font = UiTheme.ResolveFont(a.FontFamily);
            bool compact = _compact;

            foreach (var name in modules.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var label = new TextBlock
                {
                    FontFamily = font,
                    FontSize = compact ? Math.Max(8, a.FontSize - 2.5) : Math.Max(8.5, a.FontSize - 2),
                    Foreground = Brush(a.ModuleLabelColor),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var value = new TextBlock
                {
                    FontFamily = font,
                    FontSize = compact ? Math.Max(8.5, a.FontSize - 1.5) : Math.Max(9, a.FontSize - 1),
                    Foreground = Brush(a.BarForeground),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(label);
                stack.Children.Add(value);

                var chip = new Border
                {
                    Child = stack,
                    Background = UiTheme.Brush(UiTheme.Surface),
                    BorderBrush = UiTheme.Brush(UiTheme.Line),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = compact ? new Thickness(6, 2, 6, 2) : new Thickness(9, 3, 9, 3),
                    Margin = compact ? new Thickness(2, 0, 2, 0) : new Thickness(3, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                _moduleChips.Add(new ModuleChip { Name = name, Root = chip, Label = label, Value = value });
                ModulePanel.Children.Add(chip);
            }
        }

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
                    label = "CPU"; target = s.CpuPercent; format = v => $"{v:0}%";
                    value = format(target); return true;
                case "RAM":
                {
                    double total = s.RamTotal / 1024.0 / 1024 / 1024;
                    target = (s.RamTotal - s.RamAvailable) / 1024.0 / 1024 / 1024;
                    label = "RAM"; format = v => $"{v:0.0}/{total:0.0}G";
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
                case "NetUp":
                    label = "↑"; target = Math.Max(0, s.NetUpBps); format = FormatSpeed;
                    value = format(target); return true;
                case "NetDown":
                    label = "↓"; target = Math.Max(0, s.NetDownBps); format = FormatSpeed;
                    value = format(target); return true;
                case "Battery":
                    if (s.BatteryPercent < 0) { label = value = ""; return false; }
                    label = "BAT"; target = s.BatteryPercent;
                    format = v => $"{v:0}%{(s.OnAc ? " +" : "")}";
                    value = format(target);
                    if (s.OnAc) tint = UiTheme.Green;
                    else if (s.BatteryPercent <= 10) tint = UiTheme.Accent;
                    else if (s.BatteryPercent <= 20) tint = UiTheme.Yellow;
                    return true;
                case "Volume":
                    if (s.VolumePercent < 0) { label = value = ""; return false; }
                    label = "VOL"; target = s.VolumePercent;
                    format = s.VolumeMuted ? (v => "muted") : (v => $"{v:0}%");
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
        }

        private void StopCalendarDismissHook()
        {
            if (_calendarHook == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_calendarHook);
            _calendarHook = IntPtr.Zero;
            _calendarHookProc = null;
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
                        Dispatcher.BeginInvoke(new Action(() => CalendarPopup.IsOpen = false));
                }
            }
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private bool IsOverCalendarOrClock(int x, int y)
        {
            try
            {
                if (InScreenRect(ClockText, x, y)) return true;   // clicking the clock toggles
                if (CalendarPopup.IsOpen && CalendarPopup.Child is FrameworkElement child && child.ActualWidth > 0)
                    return InScreenRect(child, x, y);
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
