using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Metrics;

namespace WorkspaceOS.UI
{
    public partial class MonitorWindow : Window
    {
        private static MonitorWindow _instance;
        private readonly DispatcherTimer _timer = new();
        private TextBlock _cpuVal, _ramVal, _gpuVal, _diskVal, _netVal, _extraVal;

        /// <summary>
        /// Opens the real Windows Task Manager. The bar's monitor chip and the
        /// tray/launcher entries all route here.
        /// </summary>
        public static void OpenTaskManager()
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ConfigService.Log($"Task Manager: could not start ({ex.Message}).");
            }
        }

        public static void ShowMonitor()
        {
            if (_instance == null || !_instance.IsLoaded) _instance = new MonitorWindow();
            _instance.Show();
            _instance.Activate();
        }

        public MonitorWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => UiTheme.ApplyDarkTitleBar(this);
            BuildCards();
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(500, App.Configs.Config.Monitor.RefreshMs));
            _timer.Tick += (_, _) => Refresh();
            Loaded += (_, _) => { Refresh(); _timer.Start(); };
            Closed += (_, _) => _timer.Stop();
        }

        private void BuildCards()
        {
            _cpuVal = AddCard("CPU");
            _ramVal = AddCard("MEMORY");
            _gpuVal = AddCard("GPU");
            _diskVal = AddCard("DISK");
            _netVal = AddCard("NETWORK");
            _extraVal = AddCard("POWER / AUDIO");
        }

        private TextBlock AddCard(string title)
        {
            var value = new TextBlock
            {
                FontFamily = UiTheme.Mono,
                FontSize = 12,
                Foreground = UiTheme.Brush(UiTheme.Ink),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = UiTheme.Mono,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = UiTheme.Brush(UiTheme.Sumi)
            });
            panel.Children.Add(value);
            CardsGrid.Children.Add(new Border
            {
                Background = UiTheme.Brush(UiTheme.Surface),
                BorderBrush = UiTheme.Brush(UiTheme.Line),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(13),
                Margin = new Thickness(0, 0, 8, 8),
                Child = panel
            });
            return value;
        }

        private void Refresh()
        {
            var s = App.Metrics.Latest;

            _cpuVal.Text = $"Usage: {s.CpuPercent:0.0}%\n" +
                           (s.CpuTempC > 0 ? $"Temp:  {s.CpuTempC:0.0} °C" : "Temp:  n/a") +
                           $"\nCores: {Environment.ProcessorCount}";

            double usedGb = (s.RamTotal - s.RamAvailable) / 1024.0 / 1024 / 1024;
            double totalGb = s.RamTotal / 1024.0 / 1024 / 1024;
            _ramVal.Text = $"Usage: {s.RamPercent:0}%\nUsed:  {usedGb:0.1} GB / {totalGb:0.1} GB\nFree:  {s.RamAvailable / 1024.0 / 1024 / 1024:0.1} GB";

            _gpuVal.Text = (s.GpuPercent < 0 ? "Usage: n/a" : $"Usage: {s.GpuPercent:0.0}%") + "\n" +
                           (s.VramUsedMB < 0 ? "VRAM:  n/a" : $"VRAM:  {s.VramUsedMB:0} MB") + "\n" +
                           (s.GpuTempC > 0 ? $"Temp:  {s.GpuTempC:0.0} °C" : "Temp:  n/a");

            double freeGb = s.DiskFreeBytes / 1024.0 / 1024 / 1024;
            double diskTotalGb = s.DiskTotalBytes / 1024.0 / 1024 / 1024;
            _diskVal.Text = $"Free:  {freeGb:0.1} GB / {diskTotalGb:0.1} GB\n" +
                            $"Read:  {MetricsService.FormatBytes(s.DiskReadBps)}/s\n" +
                            $"Write: {MetricsService.FormatBytes(s.DiskWriteBps)}/s";

            _netVal.Text = $"Up:    {MetricsService.FormatBytes(s.NetUpBps)}/s\nDown:  {MetricsService.FormatBytes(s.NetDownBps)}/s";

            _extraVal.Text = (s.BatteryPercent < 0 ? "Battery: none" : $"Battery: {s.BatteryPercent}%{(s.OnAc ? " (AC)" : "")}") + "\n" +
                             (s.VolumePercent < 0 ? "Volume:  n/a" : $"Volume:  {(s.VolumeMuted ? "muted" : s.VolumePercent + "%")}");

            ProcessList.ItemsSource = App.Metrics.TopProcesses(App.Configs.Config.Monitor.ProcessCount);
        }
    }
}
