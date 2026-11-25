using System;
using System.Runtime.InteropServices;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// One low-level keyboard hook for the surfaces that must see keys without
    /// holding focus. The bar never activates, and a panel can lose focus to the
    /// window under it, so Escape and the arrow keys have to be caught before
    /// Windows hands them to whatever is underneath.
    /// </summary>
    internal sealed class GlobalKeyHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        private HookProc _proc;          // held so the GC cannot collect the thunk
        private IntPtr _handle;

        /// <summary>
        /// Called on the hook thread for every key-down with the virtual-key code.
        /// Return true to swallow the key; marshal to the UI thread inside.
        /// </summary>
        public Func<int, bool> KeyDown { get; set; }

        public bool Installed => _handle != IntPtr.Zero;

        public void Install()
        {
            if (_handle != IntPtr.Zero) return;
            _proc = Dispatch;
            _handle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        }

        private IntPtr Dispatch(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0
                && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                && KeyDown != null)
            {
                try
                {
                    if (KeyDown(Marshal.ReadInt32(lParam))) return (IntPtr)1;
                }
                catch { /* a bad handler must never break the hook chain */ }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_handle);
                _handle = IntPtr.Zero;
            }
            _proc = null;
        }
    }
}
