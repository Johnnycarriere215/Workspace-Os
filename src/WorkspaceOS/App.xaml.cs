using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using WorkspaceOS.Core;
using WorkspaceOS.Core.AutoHotkey;
using WorkspaceOS.Core.Clip;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Focus;
using WorkspaceOS.Core.Hotkeys;
using WorkspaceOS.Core.Ipc;
using WorkspaceOS.Core.Metrics;
using WorkspaceOS.Core.Tiling;
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
        public static TilingEngine Tiling { get; private set; }
        public static AutoHotkeyBridge Ahk { get; private set; }
        private static CommandServer _commands;

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
                Tiling?.ApplyConfigChange();
                Ahk?.EnsureRunning();
            };

            // Tiling engine (opt-in via Settings → Tiling).
            Tiling = new TilingEngine(Configs, Workspaces);
            Tiling.Initialize();

            // AutoHotkey bridge + IPC command server.
            _commands = new CommandServer(HandleIpcCommand);
            _commands.Start();
            Ahk = new AutoHotkeyBridge(Configs);
            Ahk.Start();
            StartAhkWatchdog();

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
                Hotkeys.RegisterAction($"Workspace{i}", () =>
                {
                    if (Tiling.Enabled) Tiling.PreAdoptForSwitch(ws);
                    Workspaces.SwitchTo(ws);
                });
                Hotkeys.RegisterAction($"SendToWorkspace{i}", () =>
                {
                    if (Tiling.Enabled) Tiling.SendFocusedToWorkspace(ws);
                    else Workspaces.SendForegroundToWorkspace(ws);
                });
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

            // --- tiling commands (Hyprland-inspired defaults) ---
            Hotkeys.RegisterAction("TileFocusLeft", () => Tiling.FocusDirection(Direction.Left));
            Hotkeys.RegisterAction("TileFocusDown", () => Tiling.FocusDirection(Direction.Down));
            Hotkeys.RegisterAction("TileFocusUp", () => Tiling.FocusDirection(Direction.Up));
            Hotkeys.RegisterAction("TileFocusRight", () => Tiling.FocusDirection(Direction.Right));
            Hotkeys.RegisterAction("TileMoveLeft", () => Tiling.MoveDirection(Direction.Left));
            Hotkeys.RegisterAction("TileMoveDown", () => Tiling.MoveDirection(Direction.Down));
            Hotkeys.RegisterAction("TileMoveUp", () => Tiling.MoveDirection(Direction.Up));
            Hotkeys.RegisterAction("TileMoveRight", () => Tiling.MoveDirection(Direction.Right));
            Hotkeys.RegisterAction("TileResizeLeft", () => Tiling.ResizeDirection(Direction.Left));
            Hotkeys.RegisterAction("TileResizeDown", () => Tiling.ResizeDirection(Direction.Down));
            Hotkeys.RegisterAction("TileResizeUp", () => Tiling.ResizeDirection(Direction.Up));
            Hotkeys.RegisterAction("TileResizeRight", () => Tiling.ResizeDirection(Direction.Right));
            Hotkeys.RegisterAction("TileToggleFloating", Tiling.ToggleFloat);
            Hotkeys.RegisterAction("TileToggleSplit", Tiling.ToggleSplit);
            Hotkeys.RegisterAction("TileTogglePseudotile", Tiling.TogglePseudotile);
            Hotkeys.RegisterAction("TilePreselectLeft", () => Tiling.PreselectSplit(Direction.Left));
            Hotkeys.RegisterAction("TilePreselectRight", () => Tiling.PreselectSplit(Direction.Right));
            Hotkeys.RegisterAction("TilePreselectUp", () => Tiling.PreselectSplit(Direction.Up));
            Hotkeys.RegisterAction("TilePreselectDown", () => Tiling.PreselectSplit(Direction.Down));
            Hotkeys.RegisterAction("TileScratchpadToggle", Tiling.ToggleScratchpad);
            Hotkeys.RegisterAction("TileScratchpadSend", Tiling.ToggleScratchpadWindow);
            Hotkeys.RegisterAction("ToggleTiling", ToggleTiling);
        }

        private void ToggleTiling()
        {
            Configs.Config.Tiling.EnableTiling = !Configs.Config.Tiling.EnableTiling;
            Configs.NotifyChanged();   // ConfigChanged handler calls Tiling.ApplyConfigChange()
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
            if (cmd == "state") return Tiling.Enabled ? "tiling" : "plain";
            if (cmd == "reload") { Ahk.EnsureRunning(); return "ok"; }

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
                        Run(() =>
                        {
                            if (Tiling.Enabled) Tiling.PreAdoptForSwitch(ws);
                            Workspaces.SwitchTo(ws);
                        });
                    return "ok";
                case "send":
                    if (int.TryParse(arg, out int sws) && sws >= 1 && sws <= 9)
                        Run(() =>
                        {
                            if (Tiling.Enabled) Tiling.SendFocusedToWorkspace(sws);
                            else Workspaces.SendForegroundToWorkspace(sws);
                        });
                    return "ok";
                case "action":
                {
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
                    // Direct tiling verbs: focus:left, move:right, resize:up, float, split, …
                    if (TryTilingCommand(cmd, arg)) return "ok";
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

        private bool TryTilingCommand(string cmd, string arg)
        {
            switch (cmd)
            {
                case "focus": RunOnUi(() => Tiling.FocusDirection(ParseDir(arg))); return true;
                case "move": RunOnUi(() => Tiling.MoveDirection(ParseDir(arg))); return true;
                case "swap": RunOnUi(() => Tiling.SwapDirection(ParseDir(arg))); return true;
                case "resize": RunOnUi(() => Tiling.ResizeDirection(ParseDir(arg))); return true;
                case "float": RunOnUi(Tiling.ToggleFloat); return true;
                case "split": RunOnUi(Tiling.ToggleSplit); return true;
                case "pseudo": RunOnUi(Tiling.TogglePseudotile); return true;
                case "pre": RunOnUi(() => Tiling.PreselectSplit(ParseDir(arg))); return true;
                case "scratch": RunOnUi(Tiling.ToggleScratchpad); return true;
                case "scratchsend": RunOnUi(Tiling.ToggleScratchpadWindow); return true;
                case "tiling": RunOnUi(ToggleTiling); return true;
                default: return false;
            }
        }

        private void RunOnUi(Action a) => Dispatcher.BeginInvoke(() =>
        {
            try { a(); }
            catch (Exception ex) { ConfigService.Log("IPC action failed: " + ex.Message); }
        });

        private static Direction ParseDir(string arg) => arg?.ToLowerInvariant() switch
        {
            "l" or "left" => Direction.Left,
            "r" or "right" => Direction.Right,
            "u" or "up" => Direction.Up,
            "d" or "down" => Direction.Down,
            _ => Direction.Right
        };

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
            Add("Pin window to all workspaces", "Workspace command", () => Workspaces.SendForegroundToWorkspace(0));
            Add("Toggle tiling window manager", "WorkspaceOS", ToggleTiling);
            Add("Open Tiling Settings", "WorkspaceOS", () => SettingsWindow.ShowSettings());
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
            menu.Items.Add("System Monitor", null, (_, _) => Dispatcher.Invoke(() => MonitorWindow.ShowMonitor()));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => SettingsWindow.ShowSettings());
        }

        public void ExitApp()
        {
            ConfigService.Log("WorkspaceOS exiting…");
            try { Ahk?.Dispose(); } catch { }                    // stop AHK first so no orphaned hooks
            try { _commands?.Dispose(); } catch { }
            try { _ahkTimer?.Stop(); _ahkTimer?.Dispose(); } catch { }
            try { Tiling?.Dispose(); } catch { }
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
