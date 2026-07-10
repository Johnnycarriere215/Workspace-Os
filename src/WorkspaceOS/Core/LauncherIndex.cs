using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace WorkspaceOS.Core
{
    public enum LaunchKind { App, File, Command, Calculator, Setting, WorkspaceAction }

    public class LaunchEntry
    {
        public string Title = "";
        public string Subtitle = "";
        public LaunchKind Kind;
        public string Payload = "";   // path / command / action id
        public Action Action;         // for internal actions
    }

    /// <summary>
    /// Search index for the launcher/command palette: Start Menu apps,
    /// internal commands, workspace actions, calculator, file paths, shell commands.
    /// </summary>
    public class LauncherIndex
    {
        private List<LaunchEntry> _apps = new();
        public List<LaunchEntry> InternalCommands { get; } = new();

        public void BuildAppIndex()
        {
            var apps = new List<LaunchEntry>();
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            };
            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
            {
                try
                {
                    foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(lnk);
                        if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                        apps.Add(new LaunchEntry { Title = name, Subtitle = "Application", Kind = LaunchKind.App, Payload = lnk });
                    }
                }
                catch { }
            }
            _apps = apps.GroupBy(a => a.Title, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }

        public List<LaunchEntry> Search(string query, int max = 12)
        {
            var results = new List<LaunchEntry>();
            if (string.IsNullOrWhiteSpace(query)) return InternalCommands.Take(max).ToList();
            query = query.Trim();

            // Calculator: expressions starting with = or that look like math
            var expr = query.StartsWith('=') ? query[1..] : query;
            if (query.StartsWith('=') || LooksLikeMath(query))
            {
                var calc = TryCalculate(expr);
                if (calc != null)
                    results.Add(new LaunchEntry { Title = calc, Subtitle = $"= {expr}   (Enter copies result)", Kind = LaunchKind.Calculator, Payload = calc });
            }

            // Command palette prefix
            var text = query.StartsWith('>') ? query[1..].Trim() : query;

            // Internal commands & settings
            results.AddRange(InternalCommands
                .Where(c => c.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Take(6));

            // Apps
            if (!query.StartsWith('>'))
            {
                var starts = _apps.Where(a => a.Title.StartsWith(text, StringComparison.OrdinalIgnoreCase));
                var contains = _apps.Where(a => !a.Title.StartsWith(text, StringComparison.OrdinalIgnoreCase)
                                                && a.Title.Contains(text, StringComparison.OrdinalIgnoreCase));
                results.AddRange(starts.Concat(contains).Take(max));
            }

            // File path
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(query);
                if ((expanded.Contains('\\') || expanded.Contains('/')) && (File.Exists(expanded) || Directory.Exists(expanded)))
                    results.Add(new LaunchEntry { Title = expanded, Subtitle = "Open path", Kind = LaunchKind.File, Payload = expanded });
            }
            catch { }

            // Raw command fallback
            if (results.Count < max && query.Length > 1 && !query.StartsWith('='))
                results.Add(new LaunchEntry { Title = $"Run: {query}", Subtitle = "Execute command", Kind = LaunchKind.Command, Payload = query });

            return results.Take(max).ToList();
        }

        private static bool LooksLikeMath(string q)
        {
            bool hasDigit = q.Any(char.IsDigit);
            bool hasOp = q.Any(c => c is '+' or '-' or '*' or '/' or '%' or '(' or ')');
            return hasDigit && hasOp && q.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || c is '+' or '-' or '*' or '/' or '%' or '(' or ')' or '.' or ',');
        }

        private static string TryCalculate(string expr)
        {
            try
            {
                var result = new DataTable().Compute(expr, null);
                if (result == DBNull.Value) return null;
                return Convert.ToDouble(result).ToString("0.########");
            }
            catch { return null; }
        }
    }
}
