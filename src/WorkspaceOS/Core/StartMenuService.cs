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
                // If Win is still physically down, the key-up would toggle the
                // freshly opened Start menu straight back closed. Wait for it.
                for (int i = 0; i < 40; i++)
                {
                    bool winDown = (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
                                   (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0;
                    if (!winDown) break;
                    Thread.Sleep(15);
                }

                var tray = Native.FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero) { ConfigService.Log("StartMenu: taskbar not found"); return; }

                var root = AutomationElement.FromHandle(tray);
                var start = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton"));
                if (start == null) { ConfigService.Log("StartMenu: Start button not found"); return; }

                var bounds = start.Current.BoundingRectangle;
                if (bounds.IsEmpty) { ConfigService.Log("StartMenu: Start button bounds empty"); return; }

                // A real click is the reliable path here; UI Automation's Toggle
                // reports success but the menu closes immediately from this process.
                Native.GetCursorPos(out var previous);
                int x = (int)(bounds.X + bounds.Width / 2);
                int y = (int)(bounds.Y + bounds.Height / 2);
                Native.SetCursorPos(x, y);
                Native.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // left down
                Native.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // left up
                Thread.Sleep(120);
                Native.SetCursorPos(previous.X, previous.Y);
                ConfigService.Log($"StartMenu: clicked Start at {x},{y}");
            }
            catch (Exception ex) { ConfigService.Log("Start menu open failed: " + ex); }
        }

        private static class Native
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct POINT { public int X, Y; }

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

            [DllImport("user32.dll")]
            public static extern short GetAsyncKeyState(int vKey);

            [DllImport("user32.dll")]
            public static extern bool GetCursorPos(out POINT point);

            [DllImport("user32.dll")]
            public static extern bool SetCursorPos(int X, int Y);

            [DllImport("user32.dll")]
            public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
        }
    }
}
