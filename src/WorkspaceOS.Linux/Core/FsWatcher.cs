using System;
using System.IO;
using System.Threading;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Linux
{
    /// <summary>
    /// Watches config.json and fires onChange (debounced) whenever it is
    /// rewritten — the Linux counterpart of the Windows app's ConfigChanged
    /// event, so keybinding edits apply without a restart.
    /// </summary>
    internal sealed class FsWatcher : IDisposable
    {
        private readonly FileSystemWatcher _fsw;
        private readonly Action _onChange;
        private Timer _debounce;
        private readonly string _path;

        public FsWatcher(string path, Action onChange)
        {
            _path = path;
            _onChange = onChange;
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            _fsw = new FileSystemWatcher(dir, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _fsw.Changed += (_, _) => Debounce();
            _fsw.Created += (_, _) => Debounce();
        }

        private void Debounce()
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ =>
            {
                try { _onChange(); }
                catch (Exception ex) { ConfigService.Log("watch: " + ex.Message); }
            }, null, 400, Timeout.Infinite);
        }

        public void Dispose()
        {
            _debounce?.Dispose();
            _fsw?.Dispose();
        }
    }
}
