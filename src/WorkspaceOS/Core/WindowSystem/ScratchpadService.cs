using System;
using System.Collections.Generic;
using System.IO;
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
            try
            {
                ConfigService.Log(File.Exists(StatePath)
                    ? "Scratchpad: stashed window recorded, restoring it."
                    : "Scratchpad: nothing recorded from the last session.");
            }
            catch { }
            TryRecover();
        }

        /// <summary>
        /// Where the stashed window is remembered between runs. The handle lives in
        /// this process otherwise, so an update or crash that kills WorkspaceOS used
        /// to strand a hidden window (audio still playing) and reopen an empty pad.
        /// </summary>
        private static string StatePath => Path.Combine(ConfigService.DataDir, "scratchpad.txt");

        private void Persist()
        {
            try
            {
                if (!HasWindow)
                {
                    if (File.Exists(StatePath)) File.Delete(StatePath);
                    return;
                }

                // The return value is the owning thread; the process id comes out
                // of the out parameter. Getting this backwards made every recovery
                // look like a foreign window.
                NativeMethods.GetWindowThreadProcessId(_window, out uint pid);
                File.WriteAllLines(StatePath, new[]
                {
                    _window.ToInt64().ToString(),
                    pid.ToString(),
                    _pinnedByUs ? "1" : "0",
                    NativeMethods.GetWindowTitle(_window) ?? string.Empty
                });
            }
            catch (Exception ex) { ConfigService.Log("Scratchpad: could not save state: " + ex.Message); }
        }

        /// <summary>
        /// Re-adopts the window that was stashed before the last restart, so an
        /// update never loses it. Only trusts the record when the saved handle is
        /// still the same window (same owning process), never a recycled HWND.
        /// </summary>
        private bool TryRecover()
        {
            if (_disposed) return false;
            if (HasWindow) return true;

            try
            {
                if (!File.Exists(StatePath)) return false;

                var lines = File.ReadAllLines(StatePath);
                if (lines.Length < 2 ||
                    !long.TryParse(lines[0], out long raw) ||
                    !uint.TryParse(lines[1], out uint savedPid))
                {
                    ConfigService.Log("Scratchpad: state file unreadable, discarded.");
                    File.Delete(StatePath);
                    return false;
                }

                var hwnd = new IntPtr(raw);
                // The window is hidden by definition, so visibility cannot be
                // required here.
                if (!NativeMethods.IsWindow(hwnd))
                {
                    ConfigService.Log("Scratchpad: saved window is gone; forgotten.");
                    File.Delete(StatePath);
                    return false;
                }
                if (!WindowTracker.IsManageable(hwnd, requireVisible: false))
                {
                    ConfigService.Log("Scratchpad: saved window is no longer manageable; forgotten.");
                    File.Delete(StatePath);
                    return false;
                }

                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != savedPid)
                {
                    // A different process owns that handle now: not our window.
                    ConfigService.Log("Scratchpad: saved handle belongs to another process now; forgotten.");
                    File.Delete(StatePath);
                    return false;
                }

                _window = hwnd;
                bool wasPinnedByUs = lines.Length > 2 && lines[2] == "1";
                _wasAlreadyPinned = _workspaces.IsWindowPinned(hwnd);
                _pinnedByUs = wasPinnedByUs && _wasAlreadyPinned;

                if (!_wasAlreadyPinned)
                {
                    _workspaces.PinAppWindow(hwnd);
                    _pinnedByUs = _workspaces.IsWindowPinned(hwnd);
                }

                ConfigService.Log(
                    $"Scratchpad: recovered {NativeMethods.GetWindowTitle(hwnd)} after a restart.");
                return true;
            }
            catch (Exception ex)
            {
                ConfigService.Log("Scratchpad: recovery failed: " + ex.Message);
                return false;
            }
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
            Persist();
        }

        /// <summary>Show the stashed window, or hide it when it is already open.</summary>
        public void Toggle()
        {
            if (_disposed) return;

            if (!HasWindow)
            {
                _window = IntPtr.Zero;
                // A restart may have left the record behind (update or crash);
                // pick the window back up before declaring the pad empty.
                if (!TryRecover())
                {
                    ConfigService.Log("Scratchpad: empty.");
                    return;
                }
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
            Persist();
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
            else
            {
                _window = IntPtr.Zero;
                Persist();
            }
        }
    }
}
