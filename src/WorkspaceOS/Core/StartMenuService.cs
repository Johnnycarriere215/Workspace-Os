using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core
{
    /// <summary>
    /// Opens the native Windows Start menu for Win+Space.
    /// The taskbar's Start button exposes a UI Automation Toggle pattern, which
    /// works without moving the pointer; clicking its bounds is the fallback.
    /// </summary>
    public static class StartMenuService
    {
        private static long _lastOpen;

        public static void Open()
        {
            // AHK and the built-in hook can both deliver the action; ignore a
            // duplicate within the same keypress.
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastOpen) < 400) return;
            Interlocked.Exchange(ref _lastOpen, now);

            // UI Automation must run on an STA thread; a task-pool (MTA) call
            // found the Start button but the toggle never opened the menu.
            var thread = new Thread(OpenCore) { IsBackground = true, Name = "WorkspaceOS Start menu" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private static void OpenCore()
        {
            try
            {
                var tray = Native.FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero) { ConfigService.Log("StartMenu: taskbar not found"); return; }

                var root = AutomationElement.FromHandle(tray);
                var start = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton"));
                if (start == null) { ConfigService.Log("StartMenu: Start button not found"); return; }

                if (start.TryGetCurrentPattern(TogglePattern.Pattern, out object toggleObject))
                {
                    var toggle = (TogglePattern)toggleObject;
                    var state = toggle.Current.ToggleState;
                    ConfigService.Log($"StartMenu: toggle state={state}");
                    // If the shell opened Start on the Win key-up first, leave it.
                    if (state != ToggleState.On)
                        toggle.Toggle();
                    return;
                }

                ConfigService.Log("StartMenu: no toggle pattern; clicking Start button");
                var bounds = start.Current.BoundingRectangle;
                if (bounds.IsEmpty) return;
                Native.SetCursorPos((int)(bounds.X + bounds.Width / 2),
                                    (int)(bounds.Y + bounds.Height / 2));
                Native.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // left down
                Native.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // left up
            }
            catch (Exception ex) { ConfigService.Log("Start menu open failed: " + ex); }
        }

        private static class Native
        {
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

            [DllImport("user32.dll")]
            public static extern bool SetCursorPos(int X, int Y);

            [DllImport("user32.dll")]
            public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
        }
    }
}
