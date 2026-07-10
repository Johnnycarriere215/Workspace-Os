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
    }
}
