using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Tiling;

namespace WorkspaceOS.Linux
{
    /// <summary>
    /// WorkspaceOS Linux daemon — native, no Wine.
    ///
    /// Owns the same config.json as the Windows build (shared model +
    /// ConfigService), drives the X11 session via wmctrl/xdotool/xprop and
    /// applies the hotkey map automatically to the desktop environment.
    /// Control protocol matches the Windows named-pipe verbs (ws:3, action X,
    /// focus:left …) over a unix socket at $XDG_RUNTIME_DIR/workspaceos.sock.
    /// </summary>
    internal static class Program
    {
        private static ConfigService _configs;
        private static X11Backend _x;
        private static LinuxWorkspaceManager _workspaces;
        private static LinuxTilingEngine _tiling;
        private static KeybindingApplier _keymap;
        private static FsWatcher _watcher;

        public static string SocketPath =>
            Path.Combine(
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"))
                    ? "/tmp"
                    : Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
                "workspaceos.sock");

        private static int Main(string[] args)
        {
            var cmd = args.FirstOrDefault(a => !a.StartsWith("-")) ?? "daemon";
            if (cmd == "help" || args.Any(a => a is "--help" or "-h"))
            {
                Console.WriteLine("usage: workspaceos [daemon|action <Name>|status|keys|retile|bind-session]");
                Console.WriteLine("  daemon        run the WorkspaceOS daemon (started by the systemd user unit)");
                Console.WriteLine("  action NAME   invoke a hotkey action, e.g. CloseWindow, ToggleTiling, Workspace3");
                Console.WriteLine("  status        show daemon state (tiling on/off)");
                Console.WriteLine("  keys          show the active keymap as X11 combos");
                Console.WriteLine("  retile        force a re-tile of the current workspace");
                Console.WriteLine("  bind-session  register the keymap with the desktop environment (no daemon needed)");
                return 2;
            }
            switch (cmd)
            {
                case "daemon": return RunDaemon();
                case "action": return CliAction(args);
                case "status": return CliStatus();
                case "keys": return CliKeys();
                case "retile": return CliRetile();
                case "start": return CliStart();
                case "bind-session": return CliBindSession();
                default:
                    Console.WriteLine("usage: workspaceos [daemon|start|action <Name>|status|keys|retile|bind-session]");
                    return 2;
            }
        }

        // ------------------------------------------------------------- daemon

        private static int RunDaemon()
        {
            SingleInstance();

            _configs = new ConfigService();
            _configs.Load();
            _x = new X11Backend();
            _x.Probe();
            _workspaces = new LinuxWorkspaceManager(_configs, _x);
            _tiling = new LinuxTilingEngine(_configs, _x);
            _keymap = new KeybindingApplier(_configs);

            ConfigService.Log($"daemon: starting (display={Environment.GetEnvironmentVariable("DISPLAY") ?? "none"}, " +
                              $"desktop={KeybindingApplier.DetectDesktop()})");

            if (!_x.DisplayAvailable)
            {
                ConfigService.Log("daemon: DISPLAY is not set — waiting (started before the session?)");
                // Keep waiting rather than giving up: under systemd the unit may
                // race the session, and the daemon must survive until X is up.
                while (!_x.DisplayAvailable) Thread.Sleep(1000);
                _x.Probe();
            }

            // The whole point on Linux: the keymap is registered with the
            // desktop environment automatically — no AutoHotkey equivalent.
            int bound = _keymap.ApplyAll();
            ConfigService.Log($"daemon: keymap applied ({bound} bindings)");

            if (_configs.Config.Tiling.EnableTiling)
                _tiling.AdoptCurrentDesktop();

            StartIpcServer();
            StartWindowWatcher();
            WatchConfigFile();

            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        private static void SingleInstance()
        {
            try
            {
                if (File.Exists(SocketPath)) File.Delete(SocketPath);
            }
            catch { }
        }

        /// <summary>
        /// Polls the window list the same way the Windows engine debounces
        /// WinEvents: every 800 ms, reacting to openings and closures so new
        /// windows tile and closed ones free their slot.
        /// </summary>
        private static void StartWindowWatcher()
        {
            new Thread(() =>
            {
                var last = new System.Collections.Generic.HashSet<long>();
                while (true)
                {
                    Thread.Sleep(800);
                    try
                    {
                        var windows = _workspaces.Refresh();
                        var now = windows.Select(w => w.Id.ToInt64()).ToHashSet();

                        foreach (var w in windows)
                            if (!last.Contains(w.Id.ToInt64()))
                            {
                                int? rule = _workspaces.MatchRule(w);
                                if (rule is int t && t > 0 && w.Desktop != t - 1)
                                    _x.SendToDesktop(w.Id, t - 1, activate: false);
                                if (_tiling.Enabled) _tiling.AdoptWindow(w.Id);
                            }

                        foreach (var gone in last.Where(id => !now.Contains(id)))
                            _tiling.RemoveWindow(new IntPtr(gone));

                        last = now;
                    }
                    catch (Exception ex) { ConfigService.Log("watcher: " + ex.Message); }
                }
            }) { IsBackground = true, Name = "window-watcher" }.Start();
        }

        /// <summary>Re-applies keymap when config.json changes, like the Windows app.</summary>
        private static void WatchConfigFile()
        {
            _watcher = new FsWatcher(ConfigService.ConfigPath, () =>
            {
                try { _keymap.ApplyAll(); if (_tiling.Enabled) _tiling.RetileAll(); }
                catch (Exception ex) { ConfigService.Log("config-watch: " + ex.Message); }
            });
        }

        // ------------------------------------------------------------- IPC

        private static void StartIpcServer()
        {
            new Thread(() =>
            {
                // Prefer the unix socket; fall back to a TCP socket on
                // localhost so `socat` users and the CLI always have a path.
                while (true)
                {
                    try { ServeUnix(); }
                    catch (Exception ex) { ConfigService.Log("ipc: " + ex.Message); Thread.Sleep(1000); }
                }
            }) { IsBackground = true, Name = "ipc" }.Start();
        }

        private static void ServeUnix()
        {
            var path = SocketPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(4);

            while (true)
            {
                using var client = listener.Accept();
                var buf = new byte[4096];
                int n = client.Receive(buf);
                if (n <= 0) continue;
                string line = Encoding.UTF8.GetString(buf, 0, n).Trim();
                string reply = HandleIpcCommand(line);
                var outb = Encoding.UTF8.GetBytes(reply + "\n");
                try { client.Send(outb); } catch { }
            }
        }

        /// <summary>Same verb surface as the Windows named-pipe server.</summary>
        public static string HandleIpcCommand(string line)
        {
            var parts = line.Split(':', 2);
            string cmd = parts[0].Trim().ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";

            switch (cmd)
            {
                case "ping": return "pong";
                case "state": return _tiling.Enabled ? "tiling" : "plain";
                case "ws":
                    if (int.TryParse(arg, out var ws) && ws >= 1 && ws <= 9) Safe(() => _workspaces.SwitchTo(ws));
                    return "ok";
                case "send":
                    if (int.TryParse(arg, out var sws) && sws >= 1 && sws <= 9)
                        Safe(() =>
                        {
                            if (_tiling.Enabled) _tiling.SendFocusedToWorkspace(sws);
                            else _workspaces.SendFocusedToWorkspace(sws, _configs.Config.Tiling.FollowMovedWindow);
                        });
                    return "ok";
                case "action":
                    Safe(() => ExecuteAction(arg));
                    return "ok";
                default:
                    if (TryTilingCommand(cmd, arg)) return "ok";
                    Safe(() => ExecuteAction(line.Trim()));
                    return "ok";
            }
        }

        private static bool TryTilingCommand(string cmd, string arg)
        {
            switch (cmd)
            {
                case "focus": Safe(() => _tiling.FocusDirection(ParseDir(arg))); return true;
                case "move": Safe(() => _tiling.MoveDirection(ParseDir(arg))); return true;
                case "resize": Safe(() => _tiling.ResizeDirection(ParseDir(arg))); return true;
                case "float": Safe(_tiling.ToggleFloat); return true;
                case "split": Safe(_tiling.ToggleSplit); return true;
                case "pseudo": Safe(_tiling.TogglePseudotile); return true;
                case "pre": Safe(() => _tiling.PreselectSplit(ParseDir(arg))); return true;
                case "tiling": Safe(ToggleTiling); return true;
                case "retile": Safe(_tiling.RetileAll); return true;
                default: return false;
            }
        }

        /// <summary>Runs a named HotkeyConfig action — the same names as Windows.</summary>
        private static void ExecuteAction(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            switch (name)
            {
                case "CloseWindow": SendTilingVerb("close"); break;
                case "MaximizeWindow": SendTilingVerb("maximize"); break;
                case "RestoreWindow": SendTilingVerb("restore"); break;
                case "CenterWindow": SendTilingVerb("center"); break;
                case "FullscreenWindow": SendTilingVerb("fullscreen"); break;
                case "ToggleTiling": ToggleTiling(); break;
                case var n when n.StartsWith("TileFocus"): _tiling.FocusDirection(DirectionFromAction(n, "TileFocus")); break;
                case var n when n.StartsWith("TileMove"): _tiling.MoveDirection(DirectionFromAction(n, "TileMove")); break;
                case var n when n.StartsWith("TileResize"): _tiling.ResizeDirection(DirectionFromAction(n, "TileResize")); break;
                case "TileToggleFloating": _tiling.ToggleFloat(); break;
                case "TileToggleSplit": _tiling.ToggleSplit(); break;
                case "TileTogglePseudotile": _tiling.TogglePseudotile(); break;
                case var n when n.StartsWith("TilePreselect"): _tiling.PreselectSplit(DirectionFromAction(n, "TilePreselect")); break;
                case var n when n.StartsWith("SendToWorkspace"):
                    if (int.TryParse(n["SendToWorkspace".Length..], out var s)) HandleIpcCommand($"send:{s}");
                    break;
                case var n when n.StartsWith("Workspace") && int.TryParse(n["Workspace".Length..], out var w):
                    HandleIpcCommand($"ws:{w}");
                    break;
                default:
                    ConfigService.Log($"ipc: no Linux handler for action '{name}'");
                    break;
            }
        }

        private static Direction DirectionFromAction(string name, string prefix) => name.Length > prefix.Length
            ? ParseDir(name[prefix.Length..])
            : Direction.Right;

        /// <summary>Window commands on the focused window (close/maximize/…).</summary>
        private static void SendTilingVerb(string verb)
        {
            var id = _x.FocusedWindow();
            if (id == IntPtr.Zero) return;
            switch (verb)
            {
                case "close": _x.Close(id); break;
                case "maximize": _x.Maximize(id); break;
                case "restore": _x.Unmaximize(id); break;
                case "center": _x.CenterWindow(id); break;
                case "fullscreen": _x.ToggleFullscreen(id); break;
            }
        }

        private static void ToggleTiling()
        {
            _configs.Config.Tiling.EnableTiling = !_configs.Config.Tiling.EnableTiling;
            _configs.NotifyChanged();
            _tiling.ApplyConfigChange();
        }

        private static Direction ParseDir(string arg) => arg?.ToLowerInvariant() switch
        {
            "l" or "left" => Direction.Left,
            "r" or "right" => Direction.Right,
            "u" or "up" => Direction.Up,
            "d" or "down" => Direction.Down,
            _ => Direction.Right,
        };

        private static void Safe(Action a)
        {
            try { a(); }
            catch (Exception ex) { ConfigService.Log("ipc action failed: " + ex.Message); }
        }

        // ------------------------------------------------------------- CLI

        /// <summary>`workspaceos action CloseWindow` — what the registered keybindings run.</summary>
        private static int CliAction(string[] args)
        {
            int i = Array.IndexOf(args, "action");
            if (i < 0 || i + 1 >= args.Length) { Console.Error.WriteLine("usage: workspaceos action <Name>"); return 2; }
            return SendToDaemon(args[i + 1]);
        }

        private static int CliStatus() => SendToDaemon("state");

        private static int CliKeys()
        {
            var configs = new ConfigService();
            configs.Load();
            foreach (var kv in configs.Config.Hotkeys.Bindings.OrderBy(k => k.Key))
            {
                var x11 = KeymapTranslator.ToX11Combo(kv.Value);
                Console.WriteLine($"{kv.Key,-24} {kv.Value,-16} -> {KeymapTranslator.Describe(kv.Value)}{(x11 == null ? "  [unbindable]" : "")}");
            }
            Console.WriteLine($"\ndesktop: {KeybindingApplier.DetectDesktop()}");
            return 0;
        }

        private static int CliRetile() => SendToDaemon("retile");

        /// <summary>
        /// Launch entry point for menu items and autostart: starts the daemon
        /// detached if it is not already running, then exits. Never blocks a
        /// launcher and never opens a window.
        /// </summary>
        private static int CliStart()
        {
            // Already running?
            try
            {
                using (var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
                {
                    probe.Connect(new UnixDomainSocketEndPoint(SocketPath));
                    Console.WriteLine("workspaceos: already running");
                    return 0;
                }
            }
            catch { }

            string self = Path.Combine(AppContext.BaseDirectory, "workspaceos");
            if (!File.Exists(self)) self = System.Reflection.Assembly.GetExecutingAssembly().Location;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = self,
                    Arguments = "daemon",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                };
                System.Diagnostics.Process.Start(psi);
                Console.WriteLine("workspaceos: daemon started");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"workspaceos: failed to start daemon: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Registers the keymap with the desktop environment without the
        /// daemon — what `apt install` runs for logged-in sessions so the
        /// keybindings work immediately after install.
        /// </summary>
        private static int CliBindSession()
        {
            var configs = new ConfigService();
            configs.Load();
            var applier = new KeybindingApplier(configs);
            int n = applier.ApplyAll();
            Console.WriteLine($"workspaceos: applied {n} keybindings for {KeybindingApplier.DetectDesktop()}");
            return n > 0 ? 0 : 1;
        }

        private static int SendToDaemon(string line)
        {
            try
            {
                using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                s.Connect(new UnixDomainSocketEndPoint(SocketPath));
                var payload = Encoding.UTF8.GetBytes(line);
                s.Send(payload);
                var buf = new byte[1024];
                int n = s.Receive(buf);
                Console.WriteLine(Encoding.UTF8.GetString(buf, 0, n).Trim());
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"workspaceos: daemon not reachable ({ex.Message})");
                Console.Error.WriteLine("start it with: systemctl --user start workspaceos");
                return 1;
            }
        }
    }
}
