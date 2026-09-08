using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.Ipc
{
    /// <summary>
    /// Tiny named-pipe command server (\\.\pipe\WorkspaceOS.Ctrl).
    ///
    /// The AutoHotkey bridge sends one command per connection, e.g. "ws:2",
    /// "focus:left", "move:right", "resize:left", "float", "pre:l". The server
    /// parses nothing here beyond delegation — command handling lives in the
    /// registrar callback so it can be wired to any layer (tiling, workspaces…).
    ///
    /// Line protocol, UTF-8, one request per connection, one-line response.
    /// All commands are marshalled onto the WPF dispatcher by the registrar.
    /// </summary>
    public sealed class CommandServer : IDisposable
    {
        public const string PipeName = "WorkspaceOS.Ctrl";

        private readonly Func<string, string> _handler;
        private NamedPipeServerStream _pipe;
        private Thread _thread;
        private volatile bool _running;

        public bool ClientsConnected { get; private set; }

        public CommandServer(Func<string, string> handler) => _handler = handler;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "WorkspaceOS.Ipc"
            };
            _thread.Start();
            ConfigService.Log("IPC: command server listening on " + PipeName);
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    _pipe = pipe;
                    var connectTask = pipe.WaitForConnectionAsync();
                    // No async/await on a plain thread: poll with timeout so _running stays effective.
                    while (!connectTask.IsCompleted)
                    {
                        if (!_running) return;
                        Thread.Sleep(50);
                    }
                    if (!_running) return;
                    if (connectTask.IsFaulted) { Thread.Sleep(500); continue; }   // recreate the pipe and retry

                    ClientsConnected = true;
                    var line = ReadLine(pipe);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        string response;
                        try { response = _handler(line.Trim()) ?? "ok"; }
                        catch (Exception ex)
                        {
                            ConfigService.Log("IPC: command '" + line + "' failed: " + ex.Message);
                            response = "err " + ex.Message;
                        }
                        var bytes = Encoding.UTF8.GetBytes(response + "\n");
                        pipe.Write(bytes, 0, bytes.Length);
                        pipe.Flush();
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    // client dropped — normal
                }
                catch (Exception ex)
                {
                    ConfigService.Log("IPC: listener error: " + ex.Message);
                    Thread.Sleep(500);
                }
                finally { ClientsConnected = false; _pipe = null; }
            }
        }

        private static string ReadLine(PipeStream pipe)
        {
            var sb = new StringBuilder();
            var buf = new byte[1];
            while (sb.Length < 256)
            {
                int n = pipe.Read(buf, 0, 1);
                if (n <= 0) break;
                if (buf[0] == (byte)'\n') break;
                sb.Append((char)buf[0]);
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            _running = false;
            try { _pipe?.Dispose(); } catch { }
            try { _thread?.Join(500); } catch { }
        }
    }
}
