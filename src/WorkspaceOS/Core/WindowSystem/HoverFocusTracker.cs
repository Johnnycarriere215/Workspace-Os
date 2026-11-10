using System;
using WorkspaceOS.Core.Interop;

namespace WorkspaceOS.Core.WindowSystem
{
    /// <summary>
    /// Tracks a hovered window and arms focus only after the pointer has stayed
    /// on the same top-level window, at roughly the same spot, for the dwell
    /// time. Pure timing/geometry so it can be tested without a desktop.
    /// </summary>
    public sealed class HoverFocusTracker
    {
        private IntPtr _candidate;
        private POINT _anchor;
        private DateTime _since;
        private bool _focused;

        /// <summary>Returns true once per hover after the dwell time elapses.</summary>
        public bool ShouldFocus(DateTime now, IntPtr candidate, POINT point,
            TimeSpan dwell, int movementTolerance = 3)
        {
            if (candidate == IntPtr.Zero)
            {
                Reset();
                return false;
            }

            if (candidate != _candidate)
            {
                _candidate = candidate;
                _anchor = point;
                _since = now;
                _focused = false;
                return false;
            }

            int dx = point.X - _anchor.X;
            int dy = point.Y - _anchor.Y;
            int tolerance = Math.Max(0, movementTolerance);
            if (dx * dx + dy * dy > tolerance * tolerance)
            {
                _anchor = point;
                _since = now;
                _focused = false;
                return false;
            }

            if (_focused) return false;
            if (now - _since < dwell) return false;

            _focused = true;
            return true;
        }

        /// <summary>
        /// Record a window that is already focused (for example, the user just
        /// clicked it). Keeping the candidate marked prevents a stationary
        /// pointer from stealing focus back after an Alt+Tab or desktop switch.
        /// </summary>
        public void MarkSatisfied(IntPtr candidate, POINT point)
        {
            if (candidate == IntPtr.Zero)
            {
                Reset();
                return;
            }

            if (candidate == _candidate)
            {
                _focused = true;
                return;
            }

            _candidate = candidate;
            _anchor = point;
            _since = default;
            _focused = true;
        }

        public void Reset()
        {
            _candidate = IntPtr.Zero;
            _focused = false;
            _since = default;
        }
    }
}
