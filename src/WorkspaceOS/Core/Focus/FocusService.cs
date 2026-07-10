using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.WindowSystem;

namespace WorkspaceOS.Core.Focus
{
    public enum FocusState { Idle, Running, Succeeded, Failed }

    public class FocusSessionRecord
    {
        public DateTime StartedAt { get; set; }
        public int PlannedMinutes { get; set; }
        public double CompletedMinutes { get; set; }
        public bool Success { get; set; }
        public string FailReason { get; set; } = "";
    }

    /// <summary>
    /// Gamified focus timer. While running, watches for blocked applications;
    /// launching one fails the session immediately.
    /// </summary>
    public class FocusService
    {
        private readonly ConfigService _config;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer _blockScan = new() { Interval = TimeSpan.FromSeconds(2) };
        private DateTime _startedAt;
        private HashSet<int> _preexistingBlockedPids = new();

        public FocusState State { get; private set; } = FocusState.Idle;
        public TimeSpan Planned { get; private set; }
        public TimeSpan Elapsed => State == FocusState.Running ? DateTime.UtcNow - _startedAt : _finalElapsed;
        public TimeSpan Remaining => Planned - Elapsed > TimeSpan.Zero ? Planned - Elapsed : TimeSpan.Zero;
        public string FailReason { get; private set; } = "";
        private TimeSpan _finalElapsed;

        public event Action StateChanged;
        public event Action Tick;

        private static string HistoryPath => Path.Combine(ConfigService.DataDir, "focus-history.json");
        public List<FocusSessionRecord> History { get; private set; } = new();

        public FocusService(ConfigService config)
        {
            _config = config;
            _timer.Tick += (_, _) => OnTick();
            _blockScan.Tick += (_, _) => ScanBlockedProcesses();
            LoadHistory();
        }

        public void Start(int minutes)
        {
            if (State == FocusState.Running) return;
            Planned = TimeSpan.FromMinutes(Math.Max(1, minutes));
            _startedAt = DateTime.UtcNow;
            _config.Config.Focus.LastDurationMinutes = minutes;
            _config.Save();

            // Blocked apps already running when the session starts don't fail it
            // instantly — only NEW launches or focusing them do.
            _preexistingBlockedPids = GetBlockedProcesses().Select(p => p.Id).ToHashSet();

            State = FocusState.Running;
            _timer.Start();
            _blockScan.Start();
            StateChanged?.Invoke();
        }

        public void Abort() => Fail("Session aborted by user.");

        public void ResetToIdle()
        {
            if (State == FocusState.Running) return;
            State = FocusState.Idle;
            StateChanged?.Invoke();
        }

        private void OnTick()
        {
            if (State != FocusState.Running) return;
            if (Remaining <= TimeSpan.Zero)
            {
                Complete();
                return;
            }
            Tick?.Invoke();
        }

        private void Complete()
        {
            _finalElapsed = Planned;
            StopTimers();
            State = FocusState.Succeeded;
            Record(true, "");
            StateChanged?.Invoke();
        }

        private void Fail(string reason)
        {
            if (State != FocusState.Running) return;
            _finalElapsed = DateTime.UtcNow - _startedAt;
            StopTimers();
            State = FocusState.Failed;
            FailReason = reason;
            Record(false, reason);
            StateChanged?.Invoke();
        }

        private void StopTimers() { _timer.Stop(); _blockScan.Stop(); }

        private List<Process> GetBlockedProcesses()
        {
            var blocked = _config.Config.Focus.BlockedApps
                .Select(b => Path.GetFileNameWithoutExtension(b).ToLowerInvariant())
                .Where(b => b.Length > 0)
                .ToHashSet();
            if (blocked.Count == 0) return new List<Process>();
            return Process.GetProcesses().Where(p =>
            {
                try { return blocked.Contains(p.ProcessName.ToLowerInvariant()); }
                catch { return false; }
            }).ToList();
        }

        private void ScanBlockedProcesses()
        {
            if (State != FocusState.Running) return;
            foreach (var p in GetBlockedProcesses())
            {
                if (!_preexistingBlockedPids.Contains(p.Id))
                {
                    Fail($"Blocked application opened: {p.ProcessName}");
                    return;
                }
            }
        }

        /// <summary>Called by WorkspaceManager when any window appears — catches focusing a pre-existing blocked app.</summary>
        public void OnWindowShown(TrackedWindow w)
        {
            if (State != FocusState.Running || string.IsNullOrEmpty(w.ExeName)) return;
            var name = Path.GetFileNameWithoutExtension(w.ExeName).ToLowerInvariant();
            if (_config.Config.Focus.BlockedApps.Any(b =>
                    Path.GetFileNameWithoutExtension(b).Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                Fail($"Blocked application opened: {w.ExeName}");
            }
        }

        private void Record(bool success, string reason)
        {
            History.Add(new FocusSessionRecord
            {
                StartedAt = _startedAt.ToLocalTime(),
                PlannedMinutes = (int)Planned.TotalMinutes,
                CompletedMinutes = Math.Round(_finalElapsed.TotalMinutes, 1),
                Success = success,
                FailReason = reason
            });
            try { File.WriteAllText(HistoryPath, JsonSerializer.Serialize(History)); } catch { }
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(HistoryPath))
                    History = JsonSerializer.Deserialize<List<FocusSessionRecord>>(File.ReadAllText(HistoryPath)) ?? new();
            }
            catch { History = new(); }
        }

        public (int total, int wins, double minutes, int streak) Stats()
        {
            int total = History.Count;
            int wins = History.Count(h => h.Success);
            double minutes = History.Where(h => h.Success).Sum(h => h.CompletedMinutes);
            int streak = 0;
            for (int i = History.Count - 1; i >= 0 && History[i].Success; i--) streak++;
            return (total, wins, minutes, streak);
        }
    }
}
