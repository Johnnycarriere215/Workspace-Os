using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.Hotkeys
{
    /// <summary>
    /// Low-level keyboard hook fallback for hotkeys the shell already owns
    /// (Win+Arrow, Win+M, Win+V, Win+Shift+S, …). RegisterHotKey cannot take
    /// those over; a WH_KEYBOARD_LL hook can swallow them before the shell
    /// sees them. A dummy key event is injected so releasing Win afterwards
    /// does not open the Start menu.
    /// </summary>
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_DUMMY = 0xFF;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private IntPtr _hook = IntPtr.Zero;
        private LowLevelKeyboardProc _proc; // keep alive
        private readonly Dictionary<(uint vk, uint mods), Action> _bindings = new();
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

        public void Clear() => _bindings.Clear();

        public void Add(uint vk, uint mods, Action action)
        {
            _bindings[(vk, mods)] = action;
            EnsureHooked();
        }

        private void EnsureHooked()
        {
            if (_hook != IntPtr.Zero) return;
            _proc = Callback;
            _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
            if (_hook == IntPtr.Zero)
                ConfigService.Log("Keyboard hook installation failed: " + Marshal.GetLastWin32Error());
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _bindings.Count > 0)
            {
                int msg = wParam.ToInt32();
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    uint mods = CurrentMods();
                    if (_bindings.TryGetValue((data.vkCode, mods), out var action))
                    {
                        // Poison the Win chord so releasing Win won't open Start.
                        if ((mods & NativeMethods.MOD_WIN) != 0)
                        {
                            keybd_event(VK_DUMMY, 0, 0, UIntPtr.Zero);
                            keybd_event(VK_DUMMY, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        }
                        _dispatcher.BeginInvoke(() =>
                        {
                            try { action(); }
                            catch (Exception ex) { ConfigService.Log("Hook action threw: " + ex); }
                        });
                        return new IntPtr(1); // swallow
                    }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static uint CurrentMods()
        {
            uint mods = 0;
            if (Down(0x5B) || Down(0x5C)) mods |= NativeMethods.MOD_WIN;     // LWin/RWin
            if (Down(0x11)) mods |= NativeMethods.MOD_CONTROL;               // Ctrl
            if (Down(0x12)) mods |= NativeMethods.MOD_ALT;                   // Alt
            if (Down(0x10)) mods |= NativeMethods.MOD_SHIFT;                 // Shift
            return mods;
        }

        private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }
    }
}
