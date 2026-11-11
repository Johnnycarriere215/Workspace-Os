using System;
using System.Collections.Generic;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.Workspaces;

namespace WorkspaceOS.Core.WindowSystem
{
    /// <summary>
    /// A one-window scratchpad. The focused window is pinned so it can appear
    /// on any native desktop, then hidden. Win+S toggles it; the scratchpad is
    /// restored (and unpinned when we pinned it) when WorkspaceOS exits.
    /// </summary>
    public sealed class ScratchpadService : IDisposable
    {
        private readonly WorkspaceManager _workspaces;
        private IntPtr _window;
        private bool _pinnedByUs;
        private bool _wasAlreadyPinned;
        private bool _disposed;

        public ScratchpadService(WorkspaceManager workspaces)
        {
            _workspaces = workspaces;
        }

        public bool HasWindow => _window != IntPtr.Zero && NativeMethods.IsWindow(_window);

        /// <summary>Hide the focused window and remember it as the scratchpad.</summary>
        public void StashForeground()
        {
            if (_disposed) return;

            var hwnd = NativeMethods.GetForegroundWindow();
            if (!WindowTracker.IsManageable(hwnd))
            {
                ConfigService.Log("Scratchpad: no manageable foreground window.");
                return;
            }

            if (hwnd == _window)
            {
                // Pressing the stash key while the scratchpad is focused takes
                // the window back out instead of stashing it again.
                ReleaseStashed(focus: false);
                return;
            }

            if (HasWindow) ReleaseStashed(focus: false);
            else _window = IntPtr.Zero;

            _window = hwnd;
            _wasAlreadyPinned = _workspaces.IsWindowPinned(hwnd);
            _pinnedByUs = false;

            if (!_wasAlreadyPinned)
            {
                _workspaces.PinAppWindow(hwnd);
                _pinnedByUs = _workspaces.IsWindowPinned(hwnd);
            }

            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            ConfigService.Log(
                $"Scratchpad: stashed {NativeMethods.GetWindowTitle(hwnd)}" +
                (_wasAlreadyPinned || _pinnedByUs ? "." : " (pin unavailable; opens on its current workspace)."));
        }

        /// <summary>Show the stashed window, or hide it when it is already open.</summary>
        public void Toggle()
        {
            if (_disposed) return;

            if (!HasWindow)
            {
                _window = IntPtr.Zero;
                ConfigService.Log("Scratchpad: empty.");
                return;
            }

            if (NativeMethods.IsWindowVisible(_window))
            {
                Hide();
                return;
            }

            Show();
        }

        private void Hide()
        {
            NativeMethods.ShowWindow(_window, NativeMethods.SW_HIDE);
            ConfigService.Log($"Scratchpad: closed {NativeMethods.GetWindowTitle(_window)}");
        }

        private void Show()
        {
            if (!NativeMethods.IsWindow(_window)) return;

            // If native pinning was unavailable, move the window to the current
            // desktop so it still opens from anywhere. If it is pinned, leave its
            // desktop membership alone.
            if (!_wasAlreadyPinned && !_pinnedByUs)
                _workspaces.MoveWindowToDesktop(_window, _workspaces.ActiveWorkspace);

            if (NativeMethods.IsIconic(_window))
                NativeMethods.ShowWindow(_window, NativeMethods.SW_RESTORE);
            else if (NativeMethods.IsZoomed(_window))
                NativeMethods.ShowWindow(_window, NativeMethods.SW_MAXIMIZE);
            else
                NativeMethods.ShowWindow(_window, NativeMethods.SW_SHOW);

            FocusWindow(_window);
            ConfigService.Log($"Scratchpad: opened {NativeMethods.GetWindowTitle(_window)}");
        }

        /// <summary>Return the stashed window to the current workspace and unpin it if we pinned it.</summary>
        private void ReleaseStashed(bool focus)
        {
            var hwnd = _window;
            _window = IntPtr.Zero;
            bool pinnedByUs = _pinnedByUs;
            _pinnedByUs = false;
            _wasAlreadyPinned = false;

            if (!NativeMethods.IsWindow(hwnd)) return;

            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                if (NativeMethods.IsIconic(hwnd))
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                else if (NativeMethods.IsZoomed(hwnd))
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
                else
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            }

            if (pinnedByUs)
            {
                _workspaces.MoveWindowToDesktop(hwnd, _workspaces.ActiveWorkspace);
                _workspaces.UnpinAppWindow(hwnd);
            }

            if (focus) FocusWindow(hwnd);
            ConfigService.Log($"Scratchpad: released {NativeMethods.GetWindowTitle(hwnd)}");
        }

        /// <summary>
        /// Force foreground activation for an explicit Win+S press. This mirrors
        /// HoverFocusService's guarded activation without synthesizing input.
        /// </summary>
        private static bool FocusWindow(IntPtr hwnd)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == hwnd) return true;

            uint thisThread = NativeMethods.GetCurrentThreadId();
            uint foregroundThread = foreground == IntPtr.Zero
                ? 0
                : NativeMethods.GetWindowThreadProcessId(foreground, out _);
            uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);

            var attached = new List<uint>();
            try
            {
                foreach (uint thread in new[] { foregroundThread, targetThread })
                {
                    if (thread == 0 || thread == thisThread || attached.Contains(thread)) continue;
                    if (NativeMethods.AttachThreadInput(thisThread, thread, true))
                        attached.Add(thread);
                }

                NativeMethods.BringWindowToTop(hwnd);
                bool active = NativeMethods.SetForegroundWindow(hwnd);
                if (active) NativeMethods.SetFocus(hwnd);
                return active;
            }
            finally
            {
                foreach (uint thread in attached)
                    NativeMethods.AttachThreadInput(thisThread, thread, false);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (HasWindow) ReleaseStashed(focus: false);
            else _window = IntPtr.Zero;
        }
    }
}
