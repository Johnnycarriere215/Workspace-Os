using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WorkspaceOS.Core;
using WorkspaceOS.Core.AutoHotkey;
using WorkspaceOS.Core.Cava;
using WorkspaceOS.Core.Clip;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Focus;
using WorkspaceOS.Core.Hotkeys;
using WorkspaceOS.Core.Ipc;
using WorkspaceOS.Core.Metrics;
using WorkspaceOS.Core.Shibumi;
using WorkspaceOS.Core.Taskbar;
using WorkspaceOS.Core.Themes;
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
        public static WindowLaunchService WindowLauncher { get; private set; }
        public static AutoHotkeyBridge Ahk { get; private set; }
        private static CommandServer _commands;

        public static BarManager Bars { get; private set; }
        public static CavaService Cava { get; private set; }
        private FocusWindow _focusWindow;
        private HoverFocusService _hoverFocus;
        private ScratchpadService _scratchpad;
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
            ConfigService.Log($"OS: {WindowsVersion.Describe()} — desktop interfaces {WindowsVersion.DesktopInterfaceSet(WindowsVersion.Build)}" +
                              (WindowsVersion.PrefersClassicBlur ? ", classic blur (pre-2004 acrylic)" : ""));

            Metrics = new MetricsService();
            Metrics.Initialize();

            FocusServiceInstance = new FocusService(Configs);

            Workspaces = new WorkspaceManager(Configs);
            Workspaces.Initialize();
            _scratchpad = new ScratchpadService(Workspaces);

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
                Bars?.ApplyConfig();
                try { WindhawkPreset.Write(Configs.Config.Appearance); } catch { }
                try { TaskbarBackdrop.Apply(Configs.Config, force: true); } catch { }
                Ahk?.EnsureRunning();
            };

            // AutoHotkey bridge + IPC command server.
            _commands = new CommandServer(HandleIpcCommand);
            _commands.Start();
            Ahk = new AutoHotkeyBridge(Configs);
            Ahk.Start();
            StartAhkWatchdog();

            ApplyStartupSetting();

            Bars = new BarManager();
            Bars.Start();
            // Re-assert the whole theme on launch: wallpaper across every monitor
            // and virtual desktop, Windows accent, dark mode and bar palette. A
            // previous session, Explorer restart or Windows update can reset any
            // of those, and the desktop should always match the bar.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ThemeApplication.Apply(Configs.Config, forceWallpaper: false);
                TaskbarBackdrop.Start(Configs.Config);
            }), DispatcherPriority.ApplicationIdle);
            // Probe the plugin backends off the UI thread: WMI and the Bluetooth
            // radio API can take a moment on first use, and the bar must not wait.
            System.Threading.Tasks.Task.Run(() =>
            {
                try { PlatformInfo.Probe(); } catch { }
            });

            // cava feeds the bar's audio widget.
            Cava = new CavaService(Configs);
            Cava.Start();
            ConfigService.Log($"Theme: {Configs.Config.Appearance.BarTheme}; Shibumi shell {ShibumiSuite.Count} plugins.");

            WindowLauncher = new WindowLaunchService(Configs);
            WindowLauncher.Initialize();

            _hoverFocus = new HoverFocusService(Configs);
            _hoverFocus.Start();
            // A desktop switch reveals a different window without moving the
            // pointer; focus it immediately so the first keystroke follows the
            // mouse instead of staying with the previous desktop's app.
            Workspaces.ActiveWorkspaceChanged += _ => _hoverFocus.OnWorkspaceChanged();

            CreateTrayIcon();

            ConfigService.Log("WorkspaceOS started.");
        }

        private void WireHotkeys()
        {
            for (int i = 1; i <= 9; i++)
            {
                int ws = i;
                Hotkeys.RegisterAction($"Workspace{i}", () => SwitchWorkspace(ws));
                Hotkeys.RegisterAction($"SendToWorkspace{i}", () =>
                {
                    Workspaces.SendForegroundToWorkspace(ws);
                });
            }
            Hotkeys.RegisterAction("MoveWindowLeft", () => WindowCommands.Move(-1, 0));
            Hotkeys.RegisterAction("MoveWindowRight", () => WindowCommands.Move(1, 0));
            Hotkeys.RegisterAction("MoveWindowUp", () => WindowCommands.Move(0, -1));
            Hotkeys.RegisterAction("MoveWindowDown", () => WindowCommands.Move(0, 1));
            Hotkeys.RegisterAction("MoveWindowToMonitorLeft", () => WindowCommands.MoveToMonitor(WindowDirection.Left));
            Hotkeys.RegisterAction("MoveWindowToMonitorRight", () => WindowCommands.MoveToMonitor(WindowDirection.Right));
            Hotkeys.RegisterAction("MoveWindowToMonitorUp", () => WindowCommands.MoveToMonitor(WindowDirection.Up));
            Hotkeys.RegisterAction("MoveWindowToMonitorDown", () => WindowCommands.MoveToMonitor(WindowDirection.Down));
            Hotkeys.RegisterAction("CloseWindow", WindowCommands.Close);
            Hotkeys.RegisterAction("MaximizeWindow", WindowCommands.Maximize);
            Hotkeys.RegisterAction("RestoreWindow", WindowCommands.Restore);
            Hotkeys.RegisterAction("CenterWindow", WindowCommands.Center);
            Hotkeys.RegisterAction("FullscreenWindow", WindowCommands.ToggleFullscreen);
            Hotkeys.RegisterAction("FocusMode", ToggleFocusWindow);
            Hotkeys.RegisterAction("Launcher", ShowLauncher);
            Hotkeys.RegisterAction("LauncherAlt", ShowLauncher);
            Hotkeys.RegisterAction("StartMenu", StartMenuService.Open);
            Hotkeys.RegisterAction("ClipboardManager", ShowClipboard);
            Hotkeys.RegisterAction("ScratchpadSend", () => _scratchpad.StashForeground());
            Hotkeys.RegisterAction("ScratchpadToggle", () => _scratchpad.Toggle());
            Hotkeys.RegisterAction("CycleTheme", () => CycleTheme(1));
            Hotkeys.RegisterAction("ThemePicker", ThemePickerWindow.Open);
            Hotkeys.RegisterAction("VolumePanel", ShowVolumePanel);

        }

        /// <summary>
        /// Named-pipe entry point for the AutoHotkey bridge.
        /// Runs on the IPC thread: returns fast, executes on the UI dispatcher.
        /// </summary>
        private string HandleIpcCommand(string line)
        {
            var parts = line.Split(':', 2);
            string cmd = parts[0].Trim().ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";

            if (cmd == "ping") return "pong";
            if (cmd == "state") return "plain";
            if (cmd == "snapshot-cc")
            {
                if (Environment.GetEnvironmentVariable("WORKSPACEOS_DEV") != "1") return "disabled";
                // The pipe handler is not the UI thread; WPF windows must be built there.
                bool ok = false;
                Dispatcher.Invoke(() => { ok = ControlCenterWindow.Snapshot(arg); });
                return ok ? "saved" : "failed";
            }
            if (cmd == "snapshot-menu")
            {
                // Visual-check hook, not a product feature: it opens the menu and
                // briefly takes the popup's focus, so it stays off unless a
                // developer opts in with WORKSPACEOS_DEV=1.
                if (Environment.GetEnvironmentVariable("WORKSPACEOS_DEV") != "1") return "disabled";
                try { return Bars?.Primary?.SnapshotMenu(arg) == true ? "saved" : "failed"; }
                catch { return "failed"; }
            }
            if (cmd == "snapshot-fullbar")
            {
                if (Environment.GetEnvironmentVariable("WORKSPACEOS_DEV") != "1") return "disabled";
                bool ok = false;
                try { Dispatcher.Invoke(() => ok = Bars?.Primary?.SnapshotFull(arg) == true); }
                catch { return "failed"; }
                return ok ? "saved" : "failed";
            }
            if (cmd == "snapshot-shibumi")
            {
                if (Environment.GetEnvironmentVariable("WORKSPACEOS_DEV") != "1") return "disabled";
                bool ok = false;
                try { Dispatcher.Invoke(() => ok = ShibumiSettingsWindow.Snapshot(arg)); }
                catch { return "failed"; }
                return ok ? "saved" : "failed";
            }
            if (cmd == "snapshot-panel")
            {
                if (Environment.GetEnvironmentVariable("WORKSPACEOS_DEV") != "1") return "disabled";
                var pair = arg.Split('|', 2);
                if (pair.Length != 2) return "usage:snapshot-panel:<key>|<path>";
                bool ok = false;
                try { Dispatcher.Invoke(() => ok = ShibumiWidgetPanels.Snapshot(pair[0], pair[1])); }
                catch { return "failed"; }
                return ok ? "saved" : "failed";
            }
            if (cmd == "snapshot-bar")
            {
                // Visual-check hook for the workspace-number animation; same dev gate.
                if (Environment.GetEnvironmentVariable("WORKSPACEOS_DEV") != "1") return "disabled";
                bool ok = false;
                double scale = 0;
                try
                {
                    Dispatcher.Invoke(() => ok = Bars?.Primary?.SnapshotWorkspaces(arg, out scale) == true);
                }
                catch { return "failed"; }
                return ok ? $"saved scale={scale:0.000}" : "failed";
            }
            if (cmd == "diag")
                return $"desktop-api={Workspaces.NativeMode} " +
                       $"desktops={Workspaces.WorkspaceCount} bars={Bars?.Count ?? 0} " +
                       $"slots={Configs.Config.Bar.WorkspaceSlots} " +
                       $"ahk={(Ahk.Running ? "running" : "off")} " +
                       $"hotkeys={Hotkeys.ActionNames.Count()} " +
                       $"{TaskbarBackdrop.Describe()}";
            if (cmd == "hotkeys")
                return Ahk.Describe();
            if (cmd == "reapply")
            {
                // Re-assert the whole theme on demand (CLI repair, and the check
                // that a theme change paints the wallpaper exactly once).
                Dispatcher.Invoke(() => ThemeApplication.Apply(Configs.Config, forceWallpaper: true));
                return "ok";
            }
            if (cmd == "reload") { Ahk.EnsureRunning(); return "ok"; }
            if (cmd == "settings")
            {
                // Deep link for verification flows: open Settings on a named tab.
                string tab = arg;
                Dispatcher.BeginInvoke(new Action(() => SettingsWindow.ShowSettings(tab)));
                return "ok";
            }
            // Lets the installer shut us down cleanly, so a stashed scratchpad
            // window is put back on the desktop instead of being left hidden.
            if (cmd == "exit")
            {
                var ui = Dispatcher;
                ui.BeginInvoke(new Action(ExitApp));
                return "ok";
            }

            var dispatcher = Dispatcher;
            void Run(Action action)
            {
                dispatcher.BeginInvoke(() =>
                {
                    try { action(); }
                    catch (Exception ex) { ConfigService.Log("IPC action failed: " + ex.Message); }
                });
            }

            switch (cmd)
            {
                case "ws":
                    if (int.TryParse(arg, out int ws) && ws >= 1 && ws <= 9)
                        Run(() => SwitchWorkspace(ws));
                    return "ok";
                case "send":
                    if (int.TryParse(arg, out int sws) && sws >= 1 && sws <= 9)
                        Run(() =>
                        {
                            Workspaces.SendForegroundToWorkspace(sws);
                        });
                    return "ok";
                case "key":
                {
                    // Sent only by the generated AutoHotkey script. This is the
                    // one path that increments the trusted key-press counter.
                    Ahk.NoteCommand(line);
                    var name = arg;
                    Run(() =>
                    {
                        if (!Hotkeys.TryInvoke(name))
                            ConfigService.Log("IPC: unknown key action '" + name + "'");
                    });
                    return "ok";
                }
                case "action":
                {
                    // Manual/automation action: runs, but is deliberately not a
                    // key press and must not show up in the capture counter.
                    var name = arg;
                    Run(() =>
                    {
                        if (!Hotkeys.TryInvoke(name))
                            ConfigService.Log("IPC: unknown action '" + name + "'");
                    });
                    return "ok";
                }
                default:
                {
                    // Fallback: treat the whole line as a registered action name.
                    var whole = line.Trim();
                    Run(() =>
                    {
                        if (!Hotkeys.TryInvoke(whole))
                            ConfigService.Log("IPC: unknown command '" + whole + "'");
                    });
                    return "ok";
                }
            }
        }

        private System.Timers.Timer _ahkTimer;

        /// <summary>Periodic AHK liveness check — hotkeys never silently die.</summary>
        private void StartAhkWatchdog()
        {
            _ahkTimer = new System.Timers.Timer(60000) { AutoReset = true };
            _ahkTimer.Elapsed += (_, _) =>
            {
                try { Ahk.EnsureRunning(); } catch { }
            };
            _ahkTimer.Start();
        }

        private void BuildInternalCommands()
        {
            void Add(string title, string subtitle, Action action) =>
                Launcher.InternalCommands.Add(new LaunchEntry
                { Title = title, Subtitle = subtitle, Kind = LaunchKind.Setting, Action = action });

            Add("Open Settings", "WorkspaceOS", () => SettingsWindow.ShowSettings());
            Add("Open Task Manager", "Windows", () => MonitorWindow.OpenTaskManager());
            Add("Start Focus Mode", "WorkspaceOS", ToggleFocusWindow);
            Add("Clipboard History", "WorkspaceOS", ShowClipboard);
            for (int i = 1; i <= 4; i++)
            {
                int ws = i;
                Add($"Go to Workspace {i}", "Workspace command", () => SwitchWorkspace(ws));
                Add($"Send window to Workspace {i}", "Workspace command", () => Workspaces.SendForegroundToWorkspace(ws));
            }
            Add("Pin window to all workspaces", "Workspace command", () => Workspaces.SendForegroundToWorkspace(0));
            Add("Exit WorkspaceOS", "WorkspaceOS", ExitApp);
        }

        public void ToggleFocusWindow()
        {
            if (_focusWindow == null || !_focusWindow.IsLoaded)
            {
                _focusWindow = new FocusWindow();
                _focusWindow.Show();
                _focusWindow.Activate();
                PinToAllDesktops(_focusWindow);
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
            PinToAllDesktops(_launcherWindow);
        }

        private void ShowClipboard()
        {
            if (_clipboardWindow == null || !_clipboardWindow.IsLoaded) _clipboardWindow = new ClipboardWindow();
            _clipboardWindow.ShowAndFocus();
            PinToAllDesktops(_clipboardWindow);
        }

        /// <summary>Popups must follow the user across native desktops.</summary>
        public static void PinToAllDesktops(Window w)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero) Workspaces.PinAppWindow(hwnd);
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
            menu.Items.Add("Task Manager", null, (_, _) => Dispatcher.Invoke(() => MonitorWindow.OpenTaskManager()));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => SettingsWindow.ShowSettings());
        }

        /// <summary>
        /// Opens the Audio sheet under the bar (Ctrl+Win+A). The panel owns the
        /// arrow-key stepping, so the whole interaction stays on the sheet.
        /// </summary>
        public static void ShowVolumePanel() => Bars?.Primary?.OpenWidgetPanel("Volume");

        /// <summary>Steps to the next Omarchy theme, wrapping at the end.</summary>
        public static void CycleTheme(int direction = 1)
        {
            var theme = ThemeLibrary.Cycle(Configs.Config.Appearance, direction);
            if (theme != null) AfterThemeChange(theme);
        }

        /// <summary>
        /// Switches to a Windows virtual desktop. There is exactly one workspace
        /// layer: the native desktops that Win+1..9, Ctrl+Win+Arrow and Task View
        /// all move, so the bar can never show a different workspace than the
        /// shell does.
        /// </summary>
        public static void SwitchWorkspace(int workspace)
        {
            if (workspace < 1 || workspace > 9) return;
            Workspaces?.SwitchTo(workspace);
        }

        /// <summary>Activates one theme by name (from the bar's logo menu).</summary>
        public static void SetTheme(string name)
        {
            if (!ThemeLibrary.Apply(name, Configs.Config.Appearance)) return;
            AfterThemeChange(ThemeLibrary.Find(name));
        }

        public static void SetBarProfile(string profile)
        {
            Configs.Config.Bar.Profile = profile;
            Configs.Save();
            Bars?.ApplyConfig();
        }

        /// <summary>
        /// One theme change repaints every themed surface and the desktop itself:
        /// bar palette, Windows accent + dark mode, taskbar preset, and the
        /// theme's wallpaper on every monitor and virtual desktop.
        /// </summary>
        private static void AfterThemeChange(OmarchyTheme theme)
        {
            Configs.Save();
            Bars?.ApplyConfig();
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                ThemeApplication.Apply(Configs.Config, forceWallpaper: true)));
            ConfigService.Log($"Theme: {theme?.Name ?? "custom"}.");
        }

        public void ExitApp()
        {
            ConfigService.Log("WorkspaceOS exiting…");
            try { Ahk?.Dispose(); } catch { }                    // stop AHK first so no orphaned hooks
            try { _commands?.Dispose(); } catch { }
            try { _ahkTimer?.Stop(); _ahkTimer?.Dispose(); } catch { }
            try { _hoverFocus?.Dispose(); } catch { }
            try { _scratchpad?.Dispose(); } catch { }
            try { Cava?.Dispose(); } catch { }
            try { Bars?.Dispose(); } catch { }
            try { WindowLauncher?.Dispose(); } catch { }
            try { Workspaces?.Dispose(); } catch { }
            try { Hotkeys?.Dispose(); } catch { }
            try { ClipboardHistory?.Dispose(); } catch { }
            try { Metrics?.Dispose(); } catch { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            try { TaskbarBackdrop.Stop(); TaskbarBackdrop.Reset(); } catch { }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _scratchpad?.Dispose(); } catch { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            base.OnExit(e);
        }
    }
}
