using System;
using System.Collections.Generic;
using System.IO;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.WindowSystem
{
    public class TrackedWindow
    {
        public IntPtr Hwnd;
        public string Title = "";
        public string ClassName = "";
        public string ExePath = "";
        public string ExeName = "";
        public uint Pid;
    }

    /// <summary>
    /// Enumerates top-level application windows that are safe to manage
    /// (visible or hidden-by-us, has a title, not a tool window, not ours, not the shell).
    /// </summary>
    public static class WindowTracker
    {
        private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "Windows.UI.Core.CoreWindow", "ApplicationFrameWindow_Hidden",
            "MultitaskingViewFrame", "ForegroundStaging", "XamlExplorerHostIslandWindow"
        };

        private static string _selfExe;
        public static string SelfExe => _selfExe ??= Environment.ProcessPath ?? "";

        public static bool IsManageable(IntPtr hwnd, bool requireVisible = true)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return false;
            if (requireVisible && !NativeMethods.IsWindowVisible(hwnd)) return false;

            long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((ex & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (ex & NativeMethods.WS_EX_APPWINDOW) == 0) return false;

            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            if ((style & NativeMethods.WS_CHILD) != 0) return false;

            var title = NativeMethods.GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return false;

            var cls = NativeMethods.GetWindowClass(hwnd);
            if (IgnoredClasses.Contains(cls)) return false;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId) return false; // never manage our own windows

            return true;
        }

        public static TrackedWindow GetInfo(IntPtr hwnd)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            var path = NativeMethods.GetProcessPath(pid);
            return new TrackedWindow
            {
                Hwnd = hwnd,
                Title = NativeMethods.GetWindowTitle(hwnd),
                ClassName = NativeMethods.GetWindowClass(hwnd),
                ExePath = path,
                ExeName = string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path),
                Pid = pid
            };
        }

        public static List<TrackedWindow> EnumerateManageable()
        {
            var result = new List<TrackedWindow>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (IsManageable(hwnd)) result.Add(GetInfo(hwnd));
                return true;
            }, IntPtr.Zero);
            return result;
        }

        // ---- tiling-engine helpers -----------------------------------------

        /// <summary>ApplicationFrameWindow hosting a cloaked UWP core is a shell shell-game; skip those.</summary>
        private static readonly HashSet<string> TileIgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Windows.UI.Core.CoreWindow", "ApplicationFrameWindow_Hidden",
            "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame"
        };

        /// <summary>True for helper windows that must never be tiled (tool/child windows; message-only windows are never enumerated by EnumWindows).</summary>
        public static bool IsHelperWindow(IntPtr hwnd)
        {
            long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((ex & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            return (style & NativeMethods.WS_CHILD) != 0;
        }

        /// <summary>
        /// Heuristic modal-dialog detection: WS_DLGFRAME / DLGMODALFRAME chrome,
        /// no minimize capability, or an owned window with a title.
        /// </summary>
        public static bool LooksLikeDialog(IntPtr hwnd)
        {
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((style & NativeMethods.WS_DLGFRAME) != 0 && (style & NativeMethods.WS_MINIMIZEBOX) == 0) return true;
            if ((ex & NativeMethods.WS_EX_DLGMODALFRAME) != 0) return true;
            // Owned windows with a title bar and no minimize button behave like dialogs.
            IntPtr owner = NativeMethods.GetWindow(hwnd, 4 /* GW_OWNER */);
            if (owner != IntPtr.Zero)
            {
                bool hasMinBox = (style & NativeMethods.WS_MINIMIZEBOX) != 0;
                if (!hasMinBox) return true;
            }
            return false;
        }

        /// <summary>Cloaked UWP apps look visible but are suspended/off-screen — not tileable.</summary>
        public static bool IsReallyVisible(IntPtr hwnd) =>
            NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsCloaked(hwnd);

        /// <summary>A window that the tiling engine may take over. Stricter than IsManageable.</summary>
        public static bool IsTileCandidate(IntPtr hwnd)
        {
            if (!IsManageable(hwnd)) return false;
            if (TileIgnoredClasses.Contains(NativeMethods.GetWindowClass(hwnd))) return false;
            if (IsHelperWindow(hwnd)) return false;
            if (!IsReallyVisible(hwnd)) return false;
            if (NativeMethods.IsIconic(hwnd)) return false;
            return true;
        }

        /// <summary>Enumerates windows the tiling engine may adopt right now.</summary>
        public static List<TrackedWindow> EnumerateTileCandidates()
        {
            var result = new List<TrackedWindow>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (IsTileCandidate(hwnd)) result.Add(GetInfo(hwnd));
                return true;
            }, IntPtr.Zero);
            return result;
        }
    }
}
