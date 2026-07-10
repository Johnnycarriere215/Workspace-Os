using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkspaceOS.Core;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.UI
{
    public partial class LauncherWindow : Window
    {
        public LauncherWindow()
        {
            InitializeComponent();
        }

        public void ShowAndFocus()
        {
            QueryBox.Text = "";
            RunSearch("");
            Show();
            Activate();
            QueryBox.Focus();
        }

        private void RunSearch(string q) => Results.ItemsSource = App.Launcher.Search(q);

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => RunSearch(QueryBox.Text);

        private void QueryBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Hide();
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (Results.Items.Count > 0)
                        Results.SelectedIndex = Math.Min(Results.SelectedIndex + 1, Results.Items.Count - 1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (Results.Items.Count > 0)
                        Results.SelectedIndex = Math.Max(Results.SelectedIndex - 1, 0);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Execute(Results.SelectedItem as LaunchEntry ?? (Results.Items.Count > 0 ? Results.Items[0] as LaunchEntry : null));
                    e.Handled = true;
                    break;
            }
        }

        private void Results_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => Execute(Results.SelectedItem as LaunchEntry);

        private void Execute(LaunchEntry entry)
        {
            if (entry == null) return;
            Hide();
            try
            {
                switch (entry.Kind)
                {
                    case LaunchKind.App:
                    case LaunchKind.File:
                        Process.Start(new ProcessStartInfo(entry.Payload) { UseShellExecute = true });
                        break;
                    case LaunchKind.Command:
                        Process.Start(new ProcessStartInfo("cmd.exe", "/c start \"\" " + entry.Payload)
                        { UseShellExecute = false, CreateNoWindow = true });
                        break;
                    case LaunchKind.Calculator:
                        Clipboard.SetText(entry.Payload);
                        break;
                    case LaunchKind.Setting:
                    case LaunchKind.WorkspaceAction:
                        entry.Action?.Invoke();
                        break;
                }
            }
            catch (Exception ex) { ConfigService.Log("Launcher execute failed: " + ex.Message); }
        }

        private void Window_Deactivated(object sender, EventArgs e) => Hide();
    }
}
