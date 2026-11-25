using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace WorkspaceOS.Core.Themes
{
    /// <summary>
    /// One Omarchy theme: the colours an Omarchy <c>colors.toml</c> declares, plus
    /// the few derived values the bar, the tiler and the taskbar preset need.
    /// Themes are data, never code, so a user can drop a new folder into
    /// <c>%APPDATA%\WorkspaceOS\themes</c> and cycle to it without a rebuild.
    /// </summary>
    public sealed class OmarchyTheme
    {
        public string Name { get; init; } = "";
        public string Tagline { get; init; } = "";
        public bool IsLight { get; init; }

        public string Background { get; init; } = "#121212";
        public string Foreground { get; init; } = "#e6e6e6";
        public string Accent { get; init; } = "#e68e0d";
        public string Muted { get; init; } = "";
        public string Selection { get; init; } = "";

        /// <summary>ANSI 0–15, the roles Omarchy themes are built from.</summary>
        public IReadOnlyList<string> Colors { get; init; } = Array.Empty<string>();

        /// <summary>The file this theme was read from; empty for built-ins.</summary>
        public string SourcePath { get; init; } = "";

        public string Color(int index)
        {
            if (index < 0 || index >= Colors.Count) return Accent;
            string value = Colors[index];
            return string.IsNullOrWhiteSpace(value) ? Accent : value;
        }

        /// <summary>Focused-window border / bar accent.</summary>
        public string ActiveBorder => Accent;

        /// <summary>Inactive borders stay visible without competing with the accent.</summary>
        public string InactiveBorder =>
            !string.IsNullOrWhiteSpace(Muted) ? Muted : ThemeColor.Blend(Background, Foreground, 0.35);

        /// <summary>
        /// Reads an Omarchy <c>colors.toml</c>. The format is a flat list of
        /// <c>key = "value"</c> lines, so no TOML dependency is needed; unknown
        /// keys (waybar css, hyprland borders, …) are simply ignored.
        /// </summary>
        public static OmarchyTheme Load(string path, string fallbackName = null)
        {
            var values = ParseFile(path);
            string name = fallbackName ?? Path.GetFileName(Path.GetDirectoryName(path) ?? "");
            if (string.IsNullOrWhiteSpace(name)) name = "Omarchy";

            string bg = Get(values, "background", "#121212");

            // Keep the ANSI slots positional: a file that only defines color0 and
            // color15 must still answer color15 with color15, not index 1.
            var colors = new List<string>(16);
            for (int i = 0; i < 16; i++)
            {
                string slot = Get(values, $"color{i}", null);
                colors.Add(string.IsNullOrWhiteSpace(slot) ? "" : ThemeColor.Normalize(slot));
            }

            string muted = Get(values, "muted", "");
            if (string.IsNullOrWhiteSpace(muted) && colors[8].Length > 0) muted = colors[8];

            return new OmarchyTheme
            {
                Name = name,
                Tagline = Get(values, "description", "") ?? "",
                IsLight = string.Equals(Get(values, "mode", ""), "light", StringComparison.OrdinalIgnoreCase)
                          || ThemeColor.Luminance(bg) > 0.62,
                Background = ThemeColor.Normalize(bg),
                Foreground = ThemeColor.Normalize(Get(values, "foreground", "#e6e6e6")),
                Accent = ThemeColor.Normalize(Get(values, "accent", "#e68e0d")),
                Muted = ThemeColor.Normalize(muted),
                Selection = ThemeColor.Normalize(Get(values, "selection", Get(values, "selection_background", ""))),
                Colors = colors,
                SourcePath = path
            };
        }

        private static Dictionary<string, string> ParseFile(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return map;

            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal)) continue;   // section header

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                // A hex value starts with '#', so a quoted value must be read to
                // its closing quote before any comment stripping happens.
                if (value.Length > 1 && (value[0] == '"' || value[0] == '\''))
                {
                    char quote = value[0];
                    int end = value.IndexOf(quote, 1);
                    value = end > 1 ? value.Substring(1, end - 1) : value.Trim(quote);
                }
                else
                {
                    int comment = value.IndexOf('#');
                    if (comment >= 0) value = value.Substring(0, comment).Trim();
                }

                if (key.Length > 0 && value.Length > 0) map[key] = value;
            }
            return map;
        }

        private static string Get(Dictionary<string, string> map, string key, string fallback)
        {
            if (map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
            return fallback;
        }
    }

    /// <summary>Small RGB helpers so themes can derive values without WPF types.</summary>
    public static class ThemeColor
    {
        public static string Normalize(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return "";
            string value = hex.Trim().TrimStart('#');
            if (value.Length == 8) value = value.Substring(2);       // drop alpha
            if (value.Length == 3)
                value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
            if (value.Length != 6) return hex;
            return "#" + value.ToUpperInvariant();
        }

        /// <summary>Appearance colours are stored as #AARRGGBB.</summary>
        public static string Alpha(string hex, byte alpha)
        {
            string rgb = Normalize(hex).TrimStart('#');
            if (rgb.Length != 6) rgb = "000000";
            return $"#{alpha:X2}{rgb.ToUpperInvariant()}";
        }

        public static (int R, int G, int B) Rgb(string hex)
        {
            string value = Normalize(hex).TrimStart('#');
            if (value.Length != 6) return (0, 0, 0);
            return (int.Parse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    int.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    int.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        public static string FromRgb(int r, int g, int b) =>
            "#" + Math.Clamp(r, 0, 255).ToString("X2")
                + Math.Clamp(g, 0, 255).ToString("X2")
                + Math.Clamp(b, 0, 255).ToString("X2");

        /// <summary>Linear blend of two colours; <paramref name="amount"/> 0 = a, 1 = b.</summary>
        public static string Blend(string a, string b, double amount)
        {
            var (ar, ag, ab) = Rgb(a);
            var (br, bg, bb) = Rgb(b);
            double t = Math.Clamp(amount, 0, 1);
            return FromRgb(
                (int)Math.Round(ar + (br - ar) * t),
                (int)Math.Round(ag + (bg - ag) * t),
                (int)Math.Round(ab + (bb - ab) * t));
        }

        /// <summary>Relative luminance, 0 (black) to 1 (white).</summary>
        public static double Luminance(string hex)
        {
            var (r, g, b) = Rgb(hex);
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
        }

        /// <summary>Black or white ink for text drawn on <paramref name="background"/>.</summary>
        public static string ContrastText(string background) =>
            Luminance(background) > 0.55 ? "#000000" : "#FFFFFF";
    }
}
