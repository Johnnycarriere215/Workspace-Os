using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WorkspaceOS.Core.Metrics;
using WorkspaceOS.Core.Shibumi;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Shibumi's control center, with Windows behind every control: Core Audio for
    /// volume and mute, the metrics service for load and traffic, and the app's own
    /// windows for settings and task management.
    /// </summary>
    public partial class ControlCenterWindow : Window
    {
        private static ControlCenterWindow _open;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
        private bool _syncing;

        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        private const byte VkVolumeMute = 0xAD;

        // Escape has to work even when the panel could not take focus, so a
        // low-level hook watches for it instead of relying on PreviewKeyDown.
        private const int WhKeyboardLl = 13, WmKeyDown = 0x0100, WmSysKeyDown = 0x0104, VkEscape = 0x1B;
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        private readonly HookProc _hookProc;
        private IntPtr _hook;

        /// <summary>
        /// Developer render of the panel, used when the desktop cannot be captured.
        /// Builds the window without showing it and draws its content.
        /// </summary>
        public static bool Snapshot(string path)
        {
            try
            {
                var w = new ControlCenterWindow();
                w.ApplyTheme();
                w.UpdateReadouts();
                w.Shell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                w.Shell.Arrange(new Rect(w.Shell.DesiredSize));
                w.Shell.UpdateLayout();

                int cw = (int)Math.Ceiling(w.Shell.ActualWidth);
                int ch = (int)Math.Ceiling(w.Shell.ActualHeight);
                if (cw <= 0 || ch <= 0) return false;

                SavePng(w.Shell, cw, ch, path);
                w.Close();
                return true;
            }
            catch { return false; }
        }

        public static void Toggle()
        {
            if (_open != null) { _open.Close(); return; }
            var w = new ControlCenterWindow();
            _open = w;
            w.Closed += (_, _) => { _open = null; };
            w.Show();
            w.Activate();
        }

        public ControlCenterWindow()
        {
            InitializeComponent();
            ApplyTheme();
            Loaded += (_, _) =>
            {
                PositionUnderBar();
                SyncVolume();
                UpdateReadouts();
                _timer.Start();
            };
            _timer.Tick += (_, _) => UpdateReadouts();
            // Not Deactivated: when activation fails (a locked or unfocused
            // session) the panel would close the instant it opened.
            _hookProc = (code, wParam, lParam) =>
            {
                if (code >= 0 && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown)
                    && Marshal.ReadInt32(lParam) == VkEscape)
                    Dispatcher.BeginInvoke(new Action(Close));
                return CallNextHookEx(_hook, code, wParam, lParam);
            };
            Loaded += (_, _) => _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, IntPtr.Zero, 0);
            Closed += (_, _) =>
            {
                _timer.Stop();
                if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            };
        }

        private void ApplyTheme()
        {
            var a = App.Configs.Config.Appearance;
            var tokens = ShibumiTokens.From(a, App.Configs.Config.Bar.Shibumi);
            var dim = new SolidColorBrush(Hex(a.ModuleLabelColor, 0xB0));
            var tile = new SolidColorBrush(ShibumiTokens.WithAlpha(tokens.Paper, 0.10));
            var edge = new SolidColorBrush(tokens.PanelBorderWidth > 0
                ? tokens.IslandBorder
                : Colors.Transparent);

            Shell.Background = new SolidColorBrush(tokens.PanelBackground);
            Shell.BorderBrush = edge;
            Shell.BorderThickness = new Thickness(tokens.PanelBorderWidth);
            Shell.CornerRadius = new CornerRadius(tokens.PillRadius);
            // Inherited by every TextBlock that does not set its own colour.
            Shell.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,
                new SolidColorBrush(tokens.Ink));

            Heading.Foreground = VolumeLabel.Foreground = DateText.Foreground = CloseButton.Foreground = dim;

            foreach (var panel in new[] { CpuTile, RamTile, DownTile, UpTile }) panel.Background = tile;
            foreach (var button in new[] { MuteButton, LockButton, SettingsButton, TasksButton,
                         ThemeButton, BluetoothButton, DisplayButton })
            {
                button.Background = tile;
                button.BorderBrush = edge;
                button.Foreground = new SolidColorBrush(tokens.Ink);
            }
        }

        private void PositionUnderBar()
        {
            var wa = SystemParameters.WorkArea;
            int barHeight = string.Equals(App.Configs.Config.Bar.Profile, "Shibumi",
                StringComparison.OrdinalIgnoreCase)
                ? App.Configs.Config.Bar.Shibumi.BarHeight
                : App.Configs.Config.Appearance.BarHeight;
            // The appbar already removed the bar's band from the work area, so
            // the panel hangs just below the bar instead of another bar-height
            // further down.
            Left = Math.Max(wa.Left + 8, wa.Right - ActualWidth - 8);
            Top = wa.Top + (wa.Top > 0 ? 6 : Math.Max(28, barHeight) + 8);
        }

        private void SyncVolume()
        {
            _syncing = true;
            try
            {
                float v = CoreAudio.GetMasterVolume();
                VolumeSlider.Value = Math.Clamp(v * 100, 0, 100);
                MuteButton.Content = CoreAudio.GetMasterMute() ? "Unmute" : "Mute";
            }
            catch { }
            finally { _syncing = false; }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;
            try
            {
                CoreAudio.SetMasterVolume((float)Math.Clamp(e.NewValue / 100.0, 0, 1));
                VolumeValue.Text = $"{e.NewValue:0}%";
            }
            catch { }
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            keybd_event(VkVolumeMute, 0, 0, UIntPtr.Zero);
            keybd_event(VkVolumeMute, 0, 2, UIntPtr.Zero);
            Dispatcher.BeginInvoke(new Action(SyncVolume), DispatcherPriority.Background);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            System.Diagnostics.Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            SettingsWindow.ShowSettings();
        }

        private void TasksButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            MonitorWindow.OpenTaskManager();
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            App.CycleTheme(1);
            ApplyTheme();
        }

        private void BluetoothButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "ms-settings:bluetooth") { UseShellExecute = true });
            }
            catch { }
        }

        private void DisplayButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "ms-settings:display") { UseShellExecute = true });
            }
            catch { }
        }

        private void UpdateReadouts()
        {
            var now = DateTime.Now;
            var ci = CultureInfo.InvariantCulture;
            ClockText.Text = App.Configs.Config.Bar.Use24Hour
                ? now.ToString("HH:mm", ci)
                : now.ToString("h:mm tt", ci);
            DateText.Text = now.ToString("dddd d MMMM", ci);
            VolumeValue.Text = $"{VolumeSlider.Value:0}%";

            var s = App.Metrics?.Latest;
            if (s == null) return;
            CpuValue.Text = $"{s.CpuPercent:0}%";
            RamValue.Text = $"{s.RamPercent:0}%";
            DownValue.Text = Rate(s.NetDownBps);
            UpValue.Text = Rate(s.NetUpBps);
        }

        private static void SavePng(System.Windows.Media.Visual visual, int width, int height, string path)
        {
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var stream = System.IO.File.Create(path);
            encoder.Save(stream);
        }

        private static string Rate(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024 * 1024) return $"{bytesPerSecond / 1024 / 1024:0.0} MB/s";
            if (bytesPerSecond >= 1024) return $"{bytesPerSecond / 1024:0} KB/s";
            return $"{bytesPerSecond:0} B/s";
        }

        private static Color Hex(string hex, byte alpha)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                return Color.FromArgb(alpha, c.R, c.G, c.B);
            }
            catch { return Color.FromArgb(alpha, 18, 18, 18); }
        }
    }
}
