using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkspaceOS.Core;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.UI
{
    public partial class LauncherWindow : Window
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        public LauncherWindow()
        {
            InitializeComponent();
        }

        public void ShowAndFocus()
        {
            QueryBox.Text = "";
            RunSearch("");
            Show();
            ForceForeground();
            QueryBox.Focus();
            Keyboard.Focus(QueryBox);

            // Win+Space can leave the Start menu opening on the Win key-up; a
            // short re-assert dismisses it and keeps typing in the query box.
            Dispatcher.BeginInvoke(new Action(ForceForeground), DispatcherPriority.Input);
            var refocus = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            refocus.Tick += (_, _) =>
            {
                refocus.Stop();
                if (!IsVisible) Show();
                ForceForeground();
                QueryBox.Focus();
                Keyboard.Focus(QueryBox);
            };
            refocus.Start();
        }

        /// <summary>Brings the launcher forward even when the shell owns the foreground.</summary>
        private void ForceForeground()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                if (!IsVisible) Show();

                var foreground = GetForegroundWindow();
                uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
                uint currentThread = GetCurrentThreadId();
                bool attached = foregroundThread != 0 && foregroundThread != currentThread;
                if (attached) AttachThreadInput(foregroundThread, currentThread, true);
                SetForegroundWindow(hwnd);
                Activate();
                if (attached) AttachThreadInput(foregroundThread, currentThread, false);
            }
            catch { }
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
