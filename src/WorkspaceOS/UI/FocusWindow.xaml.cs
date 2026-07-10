using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkspaceOS.Core.Focus;

namespace WorkspaceOS.UI
{
    public partial class FocusWindow : Window
    {
        private FocusService Svc => App.FocusServiceInstance;

        public FocusWindow()
        {
            InitializeComponent();
            DurationBox.Text = App.Configs.Config.Focus.LastDurationMinutes.ToString();
            BuildPresets();
            Svc.StateChanged += OnStateChanged;
            Svc.Tick += OnTick;
            Closed += (_, _) => { Svc.StateChanged -= OnStateChanged; Svc.Tick -= OnTick; };
            Render();
        }

        private void BuildPresets()
        {
            PresetPanel.Children.Clear();
            foreach (var m in App.Configs.Config.Focus.PresetMinutes)
            {
                var btn = new Button
                {
                    Content = $"{m} min",
                    Style = (Style)FindResource("FlatButton"),
                    Margin = new Thickness(4, 0, 4, 0),
                    Width = 70
                };
                int minutes = m;
                btn.Click += (_, _) => DurationBox.Text = minutes.ToString();
                PresetPanel.Children.Add(btn);
            }
            var blocked = App.Configs.Config.Focus.BlockedApps;
            BlockedInfo.Text = blocked.Count == 0
                ? "No blocked apps configured (Settings → Focus Mode)."
                : "Blocked during focus: " + string.Join(", ", blocked);
        }

        private void OnStateChanged() => Dispatcher.Invoke(() =>
        {
            Render();
            if (Svc.State is FocusState.Failed or FocusState.Succeeded)
            {
                Show();
                Activate();
                Topmost = true; Topmost = false;
            }
        });

        private void OnTick() => Dispatcher.Invoke(UpdateCountdown);

        private void Render()
        {
            SetupPanel.Visibility = Svc.State == FocusState.Idle ? Visibility.Visible : Visibility.Collapsed;
            ActivePanel.Visibility = Svc.State == FocusState.Running ? Visibility.Visible : Visibility.Collapsed;
            FailPanel.Visibility = Svc.State == FocusState.Failed ? Visibility.Visible : Visibility.Collapsed;
            SuccessPanel.Visibility = Svc.State == FocusState.Succeeded ? Visibility.Visible : Visibility.Collapsed;

            switch (Svc.State)
            {
                case FocusState.Running:
                    UpdateCountdown();
                    break;
                case FocusState.Failed:
                    FailDetails.Text =
                        $"Completed: {Svc.Elapsed.TotalMinutes:0.0} min\n" +
                        $"Planned:   {Svc.Planned.TotalMinutes:0} min\n\n" +
                        $"Reason: {Svc.FailReason}";
                    break;
                case FocusState.Succeeded:
                    var (total, wins, minutes, streak) = Svc.Stats();
                    SuccessDetails.Text =
                        $"You stayed focused for {Svc.Planned.TotalMinutes:0} minutes.\n\n" +
                        $"Sessions: {total}   Wins: {wins}   Streak: {streak}\n" +
                        $"Total focused time: {minutes:0} min";
                    HistoryList.ItemsSource = Svc.History.AsEnumerable().Reverse().Take(5)
                        .Select(h => new TextBlock
                        {
                            Text = $"{h.StartedAt:MM-dd HH:mm}  {h.CompletedMinutes,5:0.0}/{h.PlannedMinutes} min  {(h.Success ? "✓" : "✗")}",
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 11,
                            Foreground = new SolidColorBrush(h.Success ? Color.FromRgb(0x7C, 0xE3, 0x8B) : Color.FromRgb(0xFF, 0x6B, 0x6B))
                        }).ToList();
                    break;
                case FocusState.Idle:
                    BuildPresets();
                    break;
            }
        }

        private void UpdateCountdown()
        {
            var r = Svc.Remaining;
            CountdownText.Text = r.TotalHours >= 1 ? $"{(int)r.TotalHours}:{r.Minutes:00}:{r.Seconds:00}" : $"{r.Minutes:00}:{r.Seconds:00}";
            double done = Svc.Planned.TotalSeconds <= 0 ? 0 : (Svc.Elapsed.TotalSeconds / Svc.Planned.TotalSeconds) * 100;
            FocusProgress.Value = Math.Min(100, done);
            FocusProgress.Maximum = 100;
            ProgressLabel.Text = $"{Svc.Elapsed.TotalMinutes:0.0} of {Svc.Planned.TotalMinutes:0} minutes ({done:0}%)";
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(DurationBox.Text.Trim(), out int minutes) || minutes < 1 || minutes > 600)
            {
                DurationBox.Text = "25";
                return;
            }
            Svc.Start(minutes);
        }

        private void Abort_Click(object sender, RoutedEventArgs e) => Svc.Abort();

        private void Reset_Click(object sender, RoutedEventArgs e) => Svc.ResetToIdle();
    }
}
