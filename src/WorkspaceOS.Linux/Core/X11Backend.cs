using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Linux
{
    /// <summary>
    /// Shared plumbing for shelling out to the standard X11 userland
    /// (wmctrl / xprop / xdotool). Everything runs synchronously with short
    /// timeouts; callers treat "tool missing" as a soft failure so the daemon
    /// degrades gracefully on minimal installs.
    /// </summary>
    internal static class XTool
    {
        public static bool DisplayAvailable =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));

        /// <summary>Runs a tool once; returns the exit code and stdout (empty on failure).</summary>
        public static (int Code, string Output) Exec(string file, string args, int timeoutMs = 4000, bool quiet = false)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return (-1, "");
                string so = p.StandardOutput.ReadToEnd(), se = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return (-1, ""); }
                if (!quiet && p.ExitCode != 0 && !string.IsNullOrWhiteSpace(se))
                    Log($"xtool: {file} exit {p.ExitCode}: {se.Trim()}");
                return (p.ExitCode, p.ExitCode == 0 ? so : "");
            }
            catch (Exception ex)
            {
                if (!quiet) Log($"xtool: failed to run {file}: {ex.Message}");
                return (-1, "");
            }
        }

        /// <summary>Stdout of a successful run, or "" when the tool failed/missing.</summary>
        public static string Run(string file, string args, int timeoutMs = 4000, bool quiet = false) =>
            Exec(file, args, timeoutMs, quiet).Output;

        /// <summary>True when the tool ran and exited 0.</summary>
        public static bool Ok(string file, string args, int timeoutMs = 4000, bool quiet = true) =>
            Exec(file, args, timeoutMs, quiet).Code == 0;

        public static bool ToolExists(string file)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                try { if (File.Exists(Path.Combine(dir, file))) return true; }
                catch { }
            }
            return false;
        }

        public static void Log(string message) => Core.Config.ConfigService.Log(message);
    }

    /// <summary>One managed X11 client window.</summary>
    public sealed class XWindow
    {
        public IntPtr Id;
        public string WmClass = "";     // e.g. "code.Code" — first field of WM_CLASS
        public string Title = "";
        public int Desktop = -1;        // -1 = sticky/undefined (0xFFFFFFFF)
        public long Pid = -1;
        public bool Hidden;             // iconified
        public int X, Y, W, H;          // from xwininfo (outer frame where available)
        public bool MaximizedVert, MaximizedHorz;

        public override string ToString() => $"0x{Id.ToInt64():x} [{WmClass}] {Title}";
    }

    /// <summary>
    /// X11 backend: window enumeration, desktop (workspace) switching and
    /// window commands via wmctrl/xdotool/xprop. Read/write via EWMH, which
    /// is what Cinnamon/MATE/XFCE implement.
    /// </summary>
    internal sealed class X11Backend
    {
        private static int Hex(IntPtr id) => (int)id.ToInt64();

        public bool HasWmctrl { get; private set; }
        public bool HasXdotool { get; private set; }

        public void Probe()
        {
            HasWmctrl = XTool.ToolExists("wmctrl");
            HasXdotool = XTool.ToolExists("xdotool");
            if (!HasWmctrl) XTool.Log("x11: wmctrl not found — window listing/tiling disabled");
            if (!HasXdotool) XTool.Log("x11: xdotool not found — window activation disabled");
        }

        /// <summary>True when an X display is reachable (DISPLAY is set).</summary>
        public bool DisplayAvailable => XTool.DisplayAvailable;

        // ------------------------------------------------------------- windows

        /// <summary>Lists all managed client windows on the current display.</summary>
        public List<XWindow> ListWindows()
        {
            var result = new List<XWindow>();
            if (!HasWmctrl || !XTool.DisplayAvailable) return result;

            // -l -x: "0x03000007  1 hostname Title" (desktop -1 = sticky)
            foreach (var raw in XTool.Run("wmctrl", "-l -x").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length < 30) continue;
                var parts = line.Split(new[] { ' ' }, 5);
                if (parts.Length < 5 || !parts[0].StartsWith("0x")) continue;

                var w = new XWindow
                {
                    Id = new IntPtr(Convert.ToInt64(parts[0], 16)),
                    Desktop = parts[1] == "-1" ? -1 : (int.TryParse(parts[1], out var d) ? d : -1),
                    WmClass = parts[2].Split('.')[0],
                    Title = parts.Length > 4 ? parts[4] : "",
                };
                Enrich(w);
                result.Add(w);
            }
            return result;
        }

        private static void Enrich(XWindow w)
        {
            // _NET_WM_STATE: maximized hints + hidden
            var state = XTool.Run("xprop", $"-id 0x{Hex(w.Id):x} -notype -f _NET_WM_STATE '32x' '$0\\n' _NET_WM_STATE", quiet: true);
            if (!string.IsNullOrWhiteSpace(state))
            {
                if (state.Contains("_NET_WM_STATE_MAXIMIZED_VERT")) w.MaximizedVert = true;
                if (state.Contains("_NET_WM_STATE_MAXIMIZED_HORZ")) w.MaximizedHorz = true;
                if (state.Contains("_NET_WM_STATE_HIDDEN")) w.Hidden = true;
            }
            var geo = XTool.Run("xwininfo", $"-id 0x{Hex(w.Id):x} -stats", quiet: true);
            foreach (var l in geo.Split('\n'))
            {
                var t = l.Trim();
                if (t.StartsWith("Absolute upper-left X:")) int.TryParse(t.Split(':')[1].Trim(), out w.X);
                else if (t.StartsWith("Absolute upper-left Y:")) int.TryParse(t.Split(':')[1].Trim(), out w.Y);
                else if (t.StartsWith("Width:")) int.TryParse(t.Split(':')[1].Trim(), out w.W);
                else if (t.StartsWith("Height:")) int.TryParse(t.Split(':')[1].Trim(), out w.H);
            }
        }

        // ------------------------------------------------------------- workspaces

        /// <summary>Number of desktops the WM reports, or a fallback of 4.</summary>
        public int DesktopCount()
        {
            var outp = XTool.Run("wmctrl", "-d", quiet: true);
            int count = outp.Split('\n').Count(l => l.Trim().Length > 0);
            return count > 0 ? count : 4;
        }

        public int CurrentDesktop()
        {
            var outp = XTool.Run("wmctrl", "-d", quiet: true);
            foreach (var l in outp.Split('\n'))
            {
                var t = l.Trim();
                if (t.Length == 0) continue;
                // "*" marks the current desktop as the first field separator
                int i = t.IndexOf('*');
                if (i >= 0) return int.TryParse(t.Substring(0, i).Trim(), out var c) ? c : 0;
            }
            return 0;
        }

        public bool SwitchDesktop(int d) =>
            HasWmctrl && XTool.Ok("wmctrl", $"-s {d}");

        /// <summary>Moves a window to a desktop (and optionally follows focus there).</summary>
        public bool SendToDesktop(IntPtr id, int d, bool activate)
        {
            if (!HasWmctrl) return false;
            XTool.Ok("wmctrl", $"-i -r 0x{Hex(id):x} -b remove,sticky");
            return XTool.Ok("wmctrl", $"-i -r 0x{Hex(id):x} -b add,_NET_WM_DESKTOP,{d}" + (activate ? ",current" : ""));
        }

        /// <summary>Pins a window to all desktops (EWMH sticky).</summary>
        public bool Sticky(IntPtr id)
        {
            if (!HasWmctrl) return false;
            return XTool.Ok("wmctrl", $"-i -r 0x{Hex(id):x} -b add,sticky");
        }

        // ------------------------------------------------------------- window commands

        public void Activate(IntPtr id)
        {
            if (HasWmctrl) XTool.Run("wmctrl", $"-i -a 0x{Hex(id):x}", quiet: true);
            if (HasXdotool) XTool.Run("xdotool", $"windowactivate --sync 0x{Hex(id):x}", quiet: true);
        }

        public void Close(IntPtr id) { if (HasWmctrl) XTool.Ok("wmctrl", $"-i -c 0x{Hex(id):x}"); }

        public void Kill(IntPtr id) { if (HasXdotool) XTool.Ok("xdotool", $"windowkill 0x{Hex(id):x}"); }

        public void Unmaximize(IntPtr id) { if (HasWmctrl) XTool.Ok("wmctrl", $"-i -r 0x{Hex(id):x} -b remove,maximized_vert,maximized_horz,fullscreen"); }

        public void Maximize(IntPtr id) { if (HasWmctrl) XTool.Ok("wmctrl", $"-i -r 0x{Hex(id):x} -b add,maximized_vert,maximized_horz"); }

        public IntPtr FocusedWindow()
        {
            var outp = XTool.Run("xdotool", "getactivewindow", quiet: true);
            if (long.TryParse(outp.Trim(), out var v) && v > 0) return new IntPtr(v);
            return IntPtr.Zero;
        }

        /// <summary>
        /// Geometry change: unmaximize first, then move/resize. Returns false
        /// when the window refused the new geometry (used to detect floaters
        /// the WM won't let us manage).
        /// </summary>
        public bool MoveResize(IntPtr id, int x, int y, int w, int h)
        {
            if (!HasWmctrl) return false;
            Unmaximize(id);
            XTool.Ok("wmctrl", $"-i -r 0x{Hex(id):x} -e 0,{x},{y},{w},{h}");
            var after = new XWindow { Id = id };
            Enrich(after);
            return after.W > 0 && Math.Abs(after.W - w) <= Math.Max(8, w / 20);
        }

        public bool SetBorderless(IntPtr id, bool borderless)
        {
            // 2 = decorations flag; 0 disables all decorations.
            string value = borderless ? "0x2, 0, 0, 0, 0" : "0x2, 1, 1, 0, 0";
            return XTool.Ok("xprop", $"-id 0x{Hex(id):x} -f _MOTIF_WM_HINTS 32x -set _MOTIF_WM_HINTS \"{value}\"");
        }

        /// <summary>Centers the focused window in its monitor's work area.</summary>
        public void CenterWindow(IntPtr id)
        {
            var area = GetWorkArea();
            var w = new XWindow { Id = id };
            Enrich(w);
            if (w.W <= 0 || w.H <= 0) return;
            Unmaximize(id);
            int x = area.Left + Math.Max(0, ((area.Right - area.Left) - w.W) / 2);
            int y = area.Top + Math.Max(0, ((area.Bottom - area.Top) - w.H) / 2);
            XTool.Ok("wmctrl", $"-i -r 0x{Hex(id):x} -e 0,{x},{y},{w.W},{w.H}");
        }

        /// <summary>Borderless-fullscreen toggle: removes decorations and covers the work area.</summary>
        public void ToggleFullscreen(IntPtr id)
        {
            var state = XTool.Run("xprop", $"-id 0x{Hex(id):x} -notype -f _NET_WM_STATE '32x' '$0\\n' _NET_WM_STATE", quiet: true);
            if (state.Contains("_NET_WM_STATE_FULLSCREEN"))
                XTool.Ok("wmctrl", $"-i -r 0x{Hex(id):x} -b remove,fullscreen");
            else
                XTool.Ok("wmctrl", $"-i -r 0x{Hex(id):x} -b add,fullscreen");
        }

        /// <summary>_NET_WORKAREA of the root window (single-monitor approximation).</summary>
        public RECT GetWorkArea()
        {
            var r = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
            var outp = XTool.Run("xprop", "-root -notype -f _NET_WORKAREA '32x' '$0\\n' _NET_WORKAREA", quiet: true);
            var m = outp.Split(':');
            if (m.Length == 2)
            {
                var v = m[1].Trim().Split(',').Select(s => s.Trim()).ToArray();
                if (v.Length >= 4 && int.TryParse(v[0], out var px) && int.TryParse(v[1], out var py)
                    && int.TryParse(v[2], out var pw) && int.TryParse(v[3], out var ph))
                    r = new RECT { Left = px, Top = py, Right = px + pw, Bottom = py + ph };
            }
            return r;
        }
    }
}
