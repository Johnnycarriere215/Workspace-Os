using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.Hotkeys
{
    /// <summary>
    /// Global hotkey registration on a hidden message window.
    /// Binding strings look like "Win+Shift+M", "Alt+Space", "Win+F1".
    /// </summary>
    public class HotkeyManager : IDisposable
    {
        private readonly Dictionary<int, string> _idToAction = new();
        private readonly Dictionary<string, Action> _actions = new();
        private HwndSource _source;
        private IntPtr _hwnd;
        private int _nextId = 0xB000;
        private readonly KeyboardHook _hookFallback = new();

        public List<string> FailedBindings { get; } = new();

        public void Initialize()
        {
            var parameters = new HwndSourceParameters("WorkspaceOS.Hotkeys")
            {
                Width = 0, Height = 0, PositionX = 0, PositionY = 0,
                WindowStyle = 0, ExtendedWindowStyle = 0,
                ParentWindow = new IntPtr(-3) // HWND_MESSAGE
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            _hwnd = _source.Handle;
        }

        public void RegisterAction(string actionName, Action handler) => _actions[actionName] = handler;

        /// <summary>Re-registers everything from config. Returns list of bindings that failed.</summary>
        public void ApplyBindings(HotkeyConfig cfg)
        {
            UnregisterAll();
            _hookFallback.Clear();
            FailedBindings.Clear();
            foreach (var kv in cfg.Bindings)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                if (!TryParse(kv.Value, out uint mods, out uint vk))
                {
                    FailedBindings.Add($"{kv.Key}: cannot parse '{kv.Value}'");
                    continue;
                }
                int id = _nextId++;
                if (NativeMethods.RegisterHotKey(_hwnd, id, mods | NativeMethods.MOD_NOREPEAT, vk))
                {
                    _idToAction[id] = kv.Key;
                }
                else
                {
                    // Windows owns this combo (Win+Arrow, Win+M, Win+V, …) —
                    // take it over with the low-level keyboard hook instead.
                    var actionName = kv.Key;
                    _hookFallback.Add(vk, mods, () =>
                    {
                        if (_actions.TryGetValue(actionName, out var h)) h();
                    });
                }
            }
        }

        private void UnregisterAll()
        {
            foreach (var id in _idToAction.Keys)
                NativeMethods.UnregisterHotKey(_hwnd, id);
            _idToAction.Clear();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && _idToAction.TryGetValue(wParam.ToInt32(), out var action))
            {
                if (_actions.TryGetValue(action, out var handler))
                {
                    try { handler(); }
                    catch (Exception ex) { ConfigService.Log($"Hotkey action {action} threw: {ex}"); }
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        public static bool TryParse(string binding, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                switch (p.ToLowerInvariant())
                {
                    case "win": case "super": case "meta": mods |= NativeMethods.MOD_WIN; continue;
                    case "ctrl": case "control": mods |= NativeMethods.MOD_CONTROL; continue;
                    case "alt": mods |= NativeMethods.MOD_ALT; continue;
                    case "shift": mods |= NativeMethods.MOD_SHIFT; continue;
                }
                // last non-modifier token is the key
                vk = KeyNameToVk(p);
                if (vk == 0) return false;
            }
            return vk != 0;
        }

        private static uint KeyNameToVk(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "left": return 0x25;
                case "up": return 0x26;
                case "right": return 0x27;
                case "down": return 0x28;
                case "space": return 0x20;
                case "enter": case "return": return 0x0D;
                case "tab": return 0x09;
                case "esc": case "escape": return 0x1B;
                case "backspace": return 0x08;
                case "delete": case "del": return 0x2E;
                case "insert": return 0x2D;
                case "home": return 0x24;
                case "end": return 0x23;
                case "pageup": return 0x21;
                case "pagedown": return 0x22;
                case "printscreen": return 0x2C;
                case "`": case "grave": return 0xC0;
            }
            if (name.Length == 1)
            {
                char c = char.ToUpperInvariant(name[0]);
                if (c >= 'A' && c <= 'Z') return c;
                if (c >= '0' && c <= '9') return c;
            }
            if (name.Length >= 2 && (name[0] == 'F' || name[0] == 'f') && int.TryParse(name.Substring(1), out int f) && f >= 1 && f <= 24)
                return (uint)(0x70 + f - 1);
            return 0;
        }

        public void Dispose()
        {
            UnregisterAll();
            _hookFallback.Dispose();
            _source?.Dispose();
        }
    }
}
