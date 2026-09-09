using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.Metrics;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Polybar/Waybar-style top bar, registered as a Win32 appbar so it reserves
    /// screen space and maximized windows never cover it.
    /// </summary>
    public partial class TopBarWindow : Window
    {
        private IntPtr _hwnd;
        private uint _appBarMessage;
        private bool _appBarRegistered;
        private uint _taskbarCreatedMsg;
        private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer _metricsTimer = new();

        public TopBarWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            Closing += (_, _) => UnregisterAppBar();
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            _hwnd = source.Handle;
            source.AddHook(WndProc);

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
            ClockText.FontFamily = new FontFamily(a.FontFamily);
            ClockText.FontSize = a.FontSize;
            // The clock keeps its Pokémon gold + glow from XAML — deliberately not config-driven.

            // Bar icon buttons are styled in XAML (BarButton/BarChip) — Pokémon type colors.

            _metricsTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(500, App.Configs.Config.Bar.RefreshMs));

            RenderWorkspaces();
            if (_appBarRegistered) PositionBar();
        }

        private static SolidColorBrush Brush(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.White); }
        }

        // ---- appbar ------------------------------------------------------

        private void RegisterAppBar()
        {
            _appBarMessage = NativeMethods.RegisterWindowMessage("WorkspaceOS.AppBar");
            var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = _appBarMessage };
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd);
            _appBarRegistered = true;
            PositionBar();
        }

        private void PositionBar()
        {
            int barHeight = App.Configs.Config.Appearance.BarHeight;
            var dpi = VisualTreeHelper.GetDpi(this);
            int barPx = (int)Math.Round(barHeight * dpi.DpiScaleY);
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

            var abd = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = _hwnd,
                uEdge = NativeMethods.ABE_TOP,
                rc = new RECT { Left = screen.Left, Top = screen.Top, Right = screen.Right, Bottom = screen.Top + barPx }
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
                Dispatcher.BeginInvoke(PositionBar); // monitor layout changed
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
                });
            }
            else if (msg == (int)_appBarMessage && (uint)wParam.ToInt64() == NativeMethods.ABM_WINDOWPOSCHANGED)
            {
                Dispatcher.BeginInvoke(PositionBar);
            }
            return IntPtr.Zero;
        }

        // ---- left: workspaces ---------------------------------------------

        private static readonly SolidColorBrush GoldBrush = new(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly SolidColorBrush WarmBrush = new(Color.FromArgb(0x99, 0xDC, 0xB4, 0x78));  // occupied @0.6
        private static readonly SolidColorBrush DimBrush = new(Color.FromArgb(0x66, 0xDC, 0xB4, 0x78));   // empty
        private static readonly Color HoverFill = Color.FromArgb(0x26, 0xFF, 0x64, 0x00);                 // ember hover
        private static readonly Color ActiveFill = Color.FromArgb(0x33, 0xFF, 0x64, 0x00);                // active tint

        /// <summary>
        /// Workspace buttons replicating the Pokémon waybar's #workspaces: a
        /// Pokéball split-border cluster (red top-left, white bottom-right) of
        /// rounded buttons — the active one glows gold, occupied ones are warm
        /// cream, empty ones dim.
        /// </summary>
        private void RenderWorkspaces()
        {
            var cfg = App.Configs.Config;
            var a = cfg.Appearance;
            WorkspacePanel.Children.Clear();
            int count = Math.Max(cfg.Workspaces.Count, App.Workspaces.WorkspaceCount);
            int activeIndex = App.Workspaces.ActiveWorkspace;
            var occupancy = App.Workspaces.GetOccupancy();

            var cluster = new Border
            {
                BorderBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0xCC, 0x22, 0x00), 0),   // Pokéball red
                        new GradientStop(Color.FromRgb(0xE8, 0xE8, 0xE8), 1)    // Pokéball white
                    }
                },
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 2, 0)
            };
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            cluster.Child = strip;

            for (int i = 1; i <= count; i++)
            {
                bool active = i == activeIndex;
                var wsCfg = cfg.Workspaces.FirstOrDefault(w => w.Index == i);
                var label = string.IsNullOrWhiteSpace(wsCfg?.Name) ? i.ToString() : wsCfg.Name;
                bool occupied = i - 1 < occupancy.Length && occupancy[i - 1];

                var txt = new TextBlock
                {
                    Text = label,
                    FontFamily = new FontFamily(a.FontFamily),
                    FontSize = 13,
                    FontWeight = active ? FontWeights.ExtraBold : FontWeights.Bold,
                    Foreground = active ? GoldBrush : occupied ? WarmBrush : DimBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (active)
                {
                    txt.Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(0xFF, 0xB0, 0x00),
                        BlurRadius = 8, ShadowDepth = 0, Opacity = 0.7
                    };
                }

                var chip = new Border
                {
                    Child = txt,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(9, 3, 9, 3),
                    Margin = new Thickness(1, 4, 1, 4),
                    Background = active ? new SolidColorBrush(ActiveFill) : System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Workspace " + label
                };
                chip.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(HoverFill);
                chip.MouseLeave += (_, _) => chip.Background = active ? new SolidColorBrush(ActiveFill) : System.Windows.Media.Brushes.Transparent;

                int idx = i;
                chip.MouseLeftButtonUp += (_, _) => App.Workspaces.SwitchTo(idx);
                strip.Children.Add(chip);
            }
            WorkspacePanel.Children.Add(cluster);
        }

        // ---- center: clock -------------------------------------------------

        private void UpdateClock()
        {
            var now = DateTime.Now;
            var ci = CultureInfo.InvariantCulture;
            string time = App.Configs.Config.Bar.ShowSeconds ? now.ToString("HH:mm:ss", ci) : now.ToString("HH:mm", ci);
            // "14:37 Thu 9 Jul"
            ClockText.Text = $"{time} {now.ToString("ddd", ci)} {now.Day} {now.ToString("MMM", ci)}";
        }

        // ---- right: modules -------------------------------------------------

        /// <summary>
        /// Pokémon-type-colored module pills, one rounded chip per module like
        /// the theme's waybar: CPU electric yellow, GPU Mewtwo blue, Disk grass
        /// green, ↑ fire orange, ↓ ice cyan, Battery water teal (gold when
        /// charging, orange/red when low), Volume fairy pink.
        /// </summary>
        private void UpdateMetrics()
        {
            // Buttons must track windows moving between desktops; the 2 s cadence
            // matches the occupancy cache in WorkspaceManager.
            RenderWorkspaces();

            var s = App.Metrics.Poll();
            var a = App.Configs.Config.Appearance;
            ModulePanel.Children.Clear();

            foreach (var module in App.Configs.Config.Bar.Modules)
            {
                (string label, string value, Color color) = module switch
                {
                    "CPU" => ("CPU ", $"{s.CpuPercent:0}%", Color.FromRgb(0xFF, 0xE4, 0x4D)),          // Electric
                    "RAM" => ("RAM ", FormatRam(s), Color.FromRgb(0x9A, 0x98, 0xBE)),                  // theme blue
                    "GPU" => ("GPU ", s.GpuPercent < 0 ? "--" : $"{s.GpuPercent:0}%", Color.FromRgb(0x89, 0xB4, 0xFA)), // Psychic
                    "Disk" => ("DSK ", FormatDisk(s), Color.FromRgb(0x50, 0xE8, 0x90)),                // Grass
                    "NetUp" => ("↑ ", FormatSpeed(s.NetUpBps), Color.FromRgb(0xFF, 0x8C, 0x35)),       // Fire
                    "NetDown" => ("↓ ", FormatSpeed(s.NetDownBps), Color.FromRgb(0x7E, 0xE8, 0xFF)),   // Ice
                    "Battery" => s.BatteryPercent < 0 ? ("", "", default(Color)) : ("BAT ", $"{s.BatteryPercent}%{(s.OnAc ? "+" : "")}", BatteryColor(s)),
                    "Volume" => s.VolumePercent < 0 ? ("", "", default(Color)) : ("VOL ", s.VolumeMuted ? "muted" : $"{s.VolumePercent}%", Color.FromRgb(0xFF, 0x9E, 0xCD)), // Fairy
                    _ => ("", "", default(Color))
                };
                if (label.Length == 0 && value.Length == 0) continue;

                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xB2, 0x14, 0x2C, 0x4A)),
                    BorderBrush = new SolidColorBrush(color),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 0, 10, 0),
                    Margin = new Thickness(3, 4, 3, 4),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var text = new TextBlock
                {
                    FontFamily = new FontFamily(a.FontFamily),
                    FontSize = a.FontSize - 1,
                    VerticalAlignment = VerticalAlignment.Center
                };
                text.Inlines.Add(new System.Windows.Documents.Run(label) { Foreground = Brush(a.ModuleLabelColor) });
                text.Inlines.Add(new System.Windows.Documents.Run(value) { Foreground = new SolidColorBrush(color) });
                chip.Child = text;
                ModulePanel.Children.Add(chip);
            }
        }

        /// <summary>Dynamic battery color: charging gold, warning orange, critical red, water teal otherwise.</summary>
        private static Color BatteryColor(SystemSnapshot s)
        {
            if (s.OnAc) return Color.FromRgb(0xFF, 0xD7, 0x00);
            if (s.BatteryPercent <= 10) return Color.FromRgb(0xFF, 0x22, 0x00);
            if (s.BatteryPercent <= 20) return Color.FromRgb(0xFF, 0x8C, 0x35);
            return Color.FromRgb(0x5E, 0xE8, 0xE8);
        }

        private static string FormatRam(SystemSnapshot s)
        {
            if (s.RamTotal == 0) return "--";
            double usedGb = (s.RamTotal - s.RamAvailable) / 1024.0 / 1024 / 1024;
            double totalGb = s.RamTotal / 1024.0 / 1024 / 1024;
            return $"{usedGb:0.0}/{totalGb:0.0}GB";
        }

        private static string FormatSpeed(double bps)
        {
            if (bps < 0) return "--";
            if (bps < 1024 * 1024) return $"{bps / 1024:0.0}KB/s";
            if (bps < 1024L * 1024 * 1024) return $"{bps / (1024.0 * 1024):0.0}MB/s";
            return $"{bps / (1024.0 * 1024 * 1024):0.0}GB/s";
        }

        private static string FormatDisk(SystemSnapshot s)
        {
            if (s.DiskTotalBytes <= 0) return "--";
            double freeGb = s.DiskFreeBytes / 1024.0 / 1024 / 1024;
            return $"{freeGb:0}G";
        }

        private void MonitorBtn_Click(object sender, RoutedEventArgs e) => MonitorWindow.ShowMonitor();
        private void SettingsBtn_Click(object sender, RoutedEventArgs e) => SettingsWindow.ShowSettings();

        // ---- calendar popover (Quickshell CalendarPopup recipe) -------------

        private DateTime _calMonth = DateTime.Today;
        private static readonly string[] DayHeaders = { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };

        private void ClockText_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (CalendarPopup.IsOpen) { CalendarPopup.IsOpen = false; return; }
            _calMonth = DateTime.Today;
            BuildCalendar();
            CalendarPopup.IsOpen = true;
        }

        private void BuildCalendar()
        {
            var a = App.Configs.Config.Appearance;
            var panel = (StackPanel)CalendarHost;
            panel.Children.Clear();

            // Header: ◀ Month YYYY ▶
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
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
                FontFamily = new FontFamily(a.FontFamily), FontSize = 13, FontWeight = FontWeights.DemiBold,
                Foreground = Brush(a.BarForeground), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(prev); header.Children.Add(title); header.Children.Add(next);
            panel.Children.Add(header);

            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            for (int c = 0; c < 7; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            for (int r = 0; r < 6; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

            var dow = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            for (int c = 0; c < 7; c++)
            {
                var t = new TextBlock
                {
                    Text = DayHeaders[c], FontFamily = new FontFamily(a.FontFamily), FontSize = 10,
                    Foreground = Brush(a.ModuleLabelColor), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(t, c);
                grid.Children.Add(t);
            }

            var first = new DateTime(_calMonth.Year, _calMonth.Month, 1);
            int lead = ((int)dow[(int)first.DayOfWeek + 6] + 6) % 7;   // Monday-first offset
            int days = DateTime.DaysInMonth(_calMonth.Year, _calMonth.Month);
            for (int d = 0; d < days; d++)
            {
                int cell = lead + d, row = cell / 7 + 1, col = cell % 7;
                bool isToday = first.AddDays(d) == DateTime.Today;
                var tile = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(6),
                    Background = isToday ? new SolidColorBrush(Color.FromArgb(0x33, 0xC5, 0x63, 0x63)) : System.Windows.Media.Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = (d + 1).ToString(), FontFamily = new FontFamily(a.FontFamily), FontSize = 11,
                        FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = Brush(isToday ? a.ActiveWorkspaceBackground : a.BarForeground),
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
            var tb = new TextBlock { Text = glyph, FontFamily = new FontFamily(a.FontFamily), FontSize = 11, Foreground = Brush(a.ModuleLabelColor) };
            var b = new Button { Content = tb, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(6, 2, 6, 2) };
            b.Click += (_, _) => onClick();
            return b;
        }
    }
}
