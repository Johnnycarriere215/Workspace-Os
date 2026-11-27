using System;
using System.Runtime.InteropServices;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// One low-level mouse hook for surfaces that must notice a click they never
    /// receive. The bar is WS_EX_NOACTIVATE, so clicking it does not deactivate a
    /// sheet opened from it and that sheet would stay open; this hook reports the
    /// click wherever it lands. Clicks are never swallowed - the hook only watches.
    /// </summary>
    internal sealed class GlobalMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        /// <summary>MSLLHOOKSTRUCT with POINT flattened, in screen pixels.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInfo
        {
            public int X, Y;
            public uint MouseData, Flags, Time;
            public IntPtr ExtraInfo;
        }

        private HookProc _proc;          // held so the GC cannot collect the thunk
        private IntPtr _handle;

        /// <summary>Called on the hook thread for every button-down, in screen pixels.</summary>
        public Action<int, int> Click { get; set; }

        public bool Installed => _handle != IntPtr.Zero;

        public void Install()
        {
            if (_handle != IntPtr.Zero) return;
            _proc = Dispatch;
            _handle = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
        }

        private IntPtr Dispatch(int code, IntPtr wParam, IntPtr lParam)
        {
            int msg = wParam.ToInt32();
            if (code >= 0
                && (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                && Click != null)
            {
                try
                {
                    var info = Marshal.PtrToStructure<MouseInfo>(lParam);
                    Click(info.X, info.Y);
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
            Click = null;
        }
    }
}
