using System;
using System.Collections.Generic;
using WorkspaceOS.Core.Interop;
using WorkspaceOS.Core.Tiling;

namespace WorkspaceOS.Core.WindowSystem
{
    /// <summary>Picks the nearest monitor in a direction from the current monitor.</summary>
    public static class MonitorNavigator
    {
        /// <summary>
        /// Returns the index of the nearest monitor whose center lies to the left,
        /// right, above or below the current monitor's center, or -1 when there is
        /// no monitor in that direction. A strong cross-axis penalty keeps moves
        /// on the same row/column when several monitors are arranged diagonally.
        /// </summary>
        public static int Pick(RECT current, IReadOnlyList<RECT> monitors, Direction direction)
        {
            if (monitors == null || monitors.Count == 0) return -1;

            double currentX = CenterX(current), currentY = CenterY(current);
            int best = -1;
            double bestScore = double.MaxValue;

            for (int i = 0; i < monitors.Count; i++)
            {
                RECT candidate = monitors[i];
                double dx = CenterX(candidate) - currentX;
                double dy = CenterY(candidate) - currentY;

                bool ahead = direction switch
                {
                    Direction.Left  => dx < -1,
                    Direction.Right => dx > 1,
                    Direction.Up    => dy < -1,
                    Direction.Down  => dy > 1,
                    _ => false
                };
                if (!ahead) continue;

                double primary = direction is Direction.Left or Direction.Right
                    ? Math.Abs(dx) : Math.Abs(dy);
                double cross = direction is Direction.Left or Direction.Right
                    ? Math.Abs(dy) : Math.Abs(dx);

                double score = primary + cross * 2.0;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private static double CenterX(RECT r) => (r.Left + r.Right) / 2.0;
        private static double CenterY(RECT r) => (r.Top + r.Bottom) / 2.0;
    }
}
