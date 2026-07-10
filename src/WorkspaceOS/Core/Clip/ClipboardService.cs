using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.Clip
{
    public class ClipItem
    {
        public string Text { get; set; } = "";
        public DateTime At { get; set; }
        public bool Pinned { get; set; }
    }

    /// <summary>Clipboard history via AddClipboardFormatListener. Text-only, persisted.</summary>
    public class ClipboardService : IDisposable
    {
        private const int MaxItems = 200;
        private HwndSource _source;
        private bool _suppressNext;

        public List<ClipItem> Items { get; private set; } = new();
        public event Action Changed;

        private static string StorePath => Path.Combine(ConfigService.DataDir, "clipboard.json");

        public void Initialize()
        {
            var parameters = new HwndSourceParameters("WorkspaceOS.Clipboard")
            {
                Width = 0, Height = 0, WindowStyle = 0,
                ParentWindow = new IntPtr(-3) // HWND_MESSAGE
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            NativeMethods.AddClipboardFormatListener(_source.Handle);
            Load();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                handled = true;
                if (_suppressNext) { _suppressNext = false; return IntPtr.Zero; }
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        var text = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(text)) AddItem(text);
                    }
                }
                catch { /* clipboard can be locked by other apps */ }
            }
            return IntPtr.Zero;
        }

        private void AddItem(string text)
        {
            var existing = Items.FirstOrDefault(i => i.Text == text);
            if (existing != null) { Items.Remove(existing); Items.Insert(0, existing); existing.At = DateTime.Now; }
            else Items.Insert(0, new ClipItem { Text = text, At = DateTime.Now });

            while (Items.Count > MaxItems)
            {
                var victim = Items.LastOrDefault(i => !i.Pinned);
                if (victim == null) break;
                Items.Remove(victim);
            }
            Save();
            Changed?.Invoke();
        }

        public void CopyToClipboard(ClipItem item)
        {
            _suppressNext = true;
            try { Clipboard.SetText(item.Text); } catch { _suppressNext = false; }
            Items.Remove(item);
            Items.Insert(0, item);
            item.At = DateTime.Now;
            Save();
            Changed?.Invoke();
        }

        public void TogglePin(ClipItem item) { item.Pinned = !item.Pinned; Save(); Changed?.Invoke(); }
        public void Remove(ClipItem item) { Items.Remove(item); Save(); Changed?.Invoke(); }
        public void ClearUnpinned() { Items.RemoveAll(i => !i.Pinned); Save(); Changed?.Invoke(); }

        private void Save()
        {
            try { File.WriteAllText(StorePath, JsonSerializer.Serialize(Items.Take(MaxItems))); } catch { }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(StorePath))
                    Items = JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(StorePath)) ?? new();
            }
            catch { Items = new(); }
        }

        public void Dispose()
        {
            if (_source != null)
            {
                NativeMethods.RemoveClipboardFormatListener(_source.Handle);
                _source.Dispose();
            }
        }
    }
}
