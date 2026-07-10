using System;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using WorkspaceOS.Core;
using WorkspaceOS.Core.Clip;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Focus;
using WorkspaceOS.Core.Hotkeys;
using WorkspaceOS.Core.Metrics;
using WorkspaceOS.Core.WindowSystem;
using WorkspaceOS.Core.Workspaces;
using WorkspaceOS.UI;

namespace WorkspaceOS
{
    public partial class App : Application
    {
        private static Mutex _singleInstance;

        public static ConfigService Configs { get; private set; }
        public static HotkeyManager Hotkeys { get; private set; }
        public static WorkspaceManager Workspaces { get; private set; }
        public static MetricsService Metrics { get; private set; }
        public static FocusService FocusServiceInstance { get; private set; }
        public static ClipboardService ClipboardHistory { get; private set; }
        public static LauncherIndex Launcher { get; private set; }

        private TopBarWindow _topBar;
        private FocusWindow _focusWindow;
        private LauncherWindow _launcherWindow;
        private ClipboardWindow _clipboardWindow;
        private System.Windows.Forms.NotifyIcon _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstance = new Mutex(true, "WorkspaceOS.SingleInstance", out bool isNew);
            if (!isNew) { Shutdown(); return; }

            base.OnStartup(e);
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                ConfigService.Log("FATAL: " + args.ExceptionObject);
            DispatcherUnhandledException += (_, args) =>
            {
                ConfigService.Log("UI exception: " + args.Exception);
                args.Handled = true;
            };

            Configs = new ConfigService();
            Configs.Load();
            ConfigService.Log("WorkspaceOS starting…");

            Metrics = new MetricsService();
            Metrics.Initialize();

            FocusServiceInstance = new FocusService(Configs);

            Workspaces = new WorkspaceManager(Configs);
            Workspaces.Initialize();

            ClipboardHistory = new ClipboardService();
            ClipboardHistory.Initialize();

            Launcher = new LauncherIndex();
            BuildInternalCommands();
            System.Threading.Tasks.Task.Run(() => Launcher.BuildAppIndex());

            Hotkeys = new HotkeyManager();
            Hotkeys.Initialize();
            WireHotkeys();
            Hotkeys.ApplyBindings(Configs.Config.Hotkeys);
            Configs.ConfigChanged += () =>
            {
                Hotkeys.ApplyBindings(Configs.Config.Hotkeys);
                _topBar?.ApplyConfig();
            };

            ApplyStartupSetting();

            _topBar = new TopBarWindow();
            _topBar.Show();

            CreateTrayIcon();
            ConfigService.Log("WorkspaceOS started.");
        }

        private void WireHotkeys()
        {
            for (int i = 1; i <= 9; i++)
            {
                int ws = i;
                Hotkeys.RegisterAction($"Workspace{i}", () => Workspaces.SwitchTo(ws));
                Hotkeys.RegisterAction($"SendToWorkspace{i}", () => Workspaces.SendForegroundToWorkspace(ws));
            }
            Hotkeys.RegisterAction("MoveWindowLeft", () => WindowCommands.Move(-1, 0));
            Hotkeys.RegisterAction("MoveWindowRight", () => WindowCommands.Move(1, 0));
            Hotkeys.RegisterAction("MoveWindowUp", () => WindowCommands.Move(0, -1));
            Hotkeys.RegisterAction("MoveWindowDown", () => WindowCommands.Move(0, 1));
            Hotkeys.RegisterAction("MaximizeWindow", WindowCommands.Maximize);
            Hotkeys.RegisterAction("RestoreWindow", WindowCommands.Restore);
            Hotkeys.RegisterAction("CenterWindow", WindowCommands.Center);
            Hotkeys.RegisterAction("FullscreenWindow", WindowCommands.ToggleFullscreen);
            Hotkeys.RegisterAction("FocusMode", ToggleFocusWindow);
            Hotkeys.RegisterAction("Launcher", ShowLauncher);
            Hotkeys.RegisterAction("ClipboardManager", ShowClipboard);
            Hotkeys.RegisterAction("Screenshot", () => ScreenshotWindow.StartCapture());
        }

        private void BuildInternalCommands()
        {
            void Add(string title, string subtitle, Action action) =>
                Launcher.InternalCommands.Add(new LaunchEntry
                { Title = title, Subtitle = subtitle, Kind = LaunchKind.Setting, Action = action });

            Add("Open Settings", "WorkspaceOS", () => SettingsWindow.ShowSettings());
            Add("Open System Monitor", "WorkspaceOS", () => MonitorWindow.ShowMonitor());
            Add("Start Focus Mode", "WorkspaceOS", ToggleFocusWindow);
            Add("Take Screenshot", "WorkspaceOS", () => ScreenshotWindow.StartCapture());
            Add("Clipboard History", "WorkspaceOS", ShowClipboard);
            for (int i = 1; i <= 4; i++)
            {
                int ws = i;
                Add($"Go to Workspace {i}", "Workspace command", () => Workspaces.SwitchTo(ws));
                Add($"Send window to Workspace {i}", "Workspace command", () => Workspaces.SendForegroundToWorkspace(ws));
            }
            Add("Restore all hidden windows", "Workspace command", () => Workspaces.RestoreAllWindows());
            Add("Exit WorkspaceOS", "WorkspaceOS", ExitApp);
        }

        public void ToggleFocusWindow()
        {
            if (_focusWindow == null || !_focusWindow.IsLoaded)
            {
                _focusWindow = new FocusWindow();
                _focusWindow.Show();
                _focusWindow.Activate();
            }
            else if (_focusWindow.IsVisible && FocusServiceInstance.State != Core.Focus.FocusState.Running)
            {
                _focusWindow.Close();
                _focusWindow = null;
            }
            else
            {
                _focusWindow.Show();
                _focusWindow.Activate();
            }
        }

        private void ShowLauncher()
        {
            if (_launcherWindow == null || !_launcherWindow.IsLoaded) _launcherWindow = new LauncherWindow();
            _launcherWindow.ShowAndFocus();
        }

        private void ShowClipboard()
        {
            if (_clipboardWindow == null || !_clipboardWindow.IsLoaded) _clipboardWindow = new ClipboardWindow();
            _clipboardWindow.ShowAndFocus();
        }

        private void ApplyStartupSetting()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (Configs.Config.General.StartWithWindows)
                    key?.SetValue("WorkspaceOS", '"' + (Environment.ProcessPath ?? "") + '"');
                else
                    key?.DeleteValue("WorkspaceOS", false);
            }
            catch (Exception ex) { ConfigService.Log("Startup registration failed: " + ex.Message); }
        }

        private void CreateTrayIcon()
        {
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? ""),
                Visible = true,
                Text = "WorkspaceOS"
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => SettingsWindow.ShowSettings()));
            menu.Items.Add("System Monitor", null, (_, _) => Dispatcher.Invoke(() => MonitorWindow.ShowMonitor()));
            menu.Items.Add("Restore all windows", null, (_, _) => Dispatcher.Invoke(() => Workspaces.RestoreAllWindows()));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => SettingsWindow.ShowSettings());
        }

        public void ExitApp()
        {
            ConfigService.Log("WorkspaceOS exiting…");
            try { Workspaces?.Dispose(); } catch { }
            try { Hotkeys?.Dispose(); } catch { }
            try { ClipboardHistory?.Dispose(); } catch { }
            try { Metrics?.Dispose(); } catch { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            base.OnExit(e);
        }
    }
}
