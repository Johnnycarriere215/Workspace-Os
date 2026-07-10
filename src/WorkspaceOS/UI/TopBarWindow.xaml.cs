using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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
        private TextBlock[] _moduleBlocks = Array.Empty<TextBlock>();

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
            ClockText.Foreground = Brush(a.BarForeground);

            _metricsTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(500, App.Configs.Config.Bar.RefreshMs));

            BuildModules();
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
                // Explorer restarted — re-register the appbar.
                _appBarRegistered = false;
                Dispatcher.BeginInvoke(RegisterAppBar);
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
            WorkspacePanel.Children.Clear();
            foreach (var ws in cfg.Workspaces.OrderBy(w => w.Index))
            {
                bool active = ws.Index == App.Workspaces.ActiveWorkspace;
                var label = string.IsNullOrWhiteSpace(ws.Name) ? ws.Index.ToString() : ws.Name;
                var border = new Border
                {
                    Background = active ? Brush(a.ActiveWorkspaceBackground) : System.Windows.Media.Brushes.Transparent,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(2, 3, 2, 3),
                    Padding = new Thickness(9, 1, 9, 1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = label,
                        FontFamily = new FontFamily(a.FontFamily),
                        FontSize = a.FontSize,
                        FontWeight = active ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = active ? Brush(a.ActiveWorkspaceForeground) : Brush(a.AccentColor),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                int idx = ws.Index;
                border.MouseLeftButtonUp += (_, _) => App.Workspaces.SwitchTo(idx);
                WorkspacePanel.Children.Add(border);
            }
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

        private void BuildModules()
        {
            var a = App.Configs.Config.Appearance;
            var modules = App.Configs.Config.Bar.Modules;
            ModulePanel.Children.Clear();
            _moduleBlocks = new TextBlock[modules.Count];
            for (int i = 0; i < modules.Count; i++)
            {
                var tb = new TextBlock
                {
                    FontFamily = new FontFamily(a.FontFamily),
                    FontSize = a.FontSize - 1,
                    Foreground = Brush(a.BarForeground),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(7, 0, 7, 0),
                    Text = ""
                };
                _moduleBlocks[i] = tb;
                ModulePanel.Children.Add(tb);
            }
        }

        private void UpdateMetrics()
        {
            var s = App.Metrics.Poll();
            var modules = App.Configs.Config.Bar.Modules;
            for (int i = 0; i < modules.Count && i < _moduleBlocks.Length; i++)
            {
                _moduleBlocks[i].Text = modules[i] switch
                {
                    "CPU" => $"CPU {s.CpuPercent,3:0}%",
                    "RAM" => $"RAM {s.RamPercent,3:0}%",
                    "GPU" => s.GpuPercent < 0 ? "GPU --" : $"GPU {s.GpuPercent,3:0}%",
                    "Disk" => $"DSK {FormatDisk(s)}",
                    "NetUp" => $"↑{MetricsService.FormatBytes(s.NetUpBps)}",
                    "NetDown" => $"↓{MetricsService.FormatBytes(s.NetDownBps)}",
                    "Battery" => s.BatteryPercent < 0 ? "" : $"BAT {s.BatteryPercent}%{(s.OnAc ? "+" : "")}",
                    "Volume" => s.VolumePercent < 0 ? "" : (s.VolumeMuted ? "VOL M" : $"VOL {s.VolumePercent}%"),
                    _ => ""
                };
                _moduleBlocks[i].Visibility = string.IsNullOrEmpty(_moduleBlocks[i].Text) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private static string FormatDisk(SystemSnapshot s)
        {
            if (s.DiskTotalBytes <= 0) return "--";
            double freeGb = s.DiskFreeBytes / 1024.0 / 1024 / 1024;
            return $"{freeGb:0}G free";
        }

        private void MonitorBtn_Click(object sender, RoutedEventArgs e) => MonitorWindow.ShowMonitor();
        private void SettingsBtn_Click(object sender, RoutedEventArgs e) => SettingsWindow.ShowSettings();
    }
}
