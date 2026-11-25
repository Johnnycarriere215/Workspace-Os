using System;
using System.Windows.Media;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Themes;

namespace WorkspaceOS.Core.Shibumi
{
    /// <summary>
    /// Direct port of Shibumi's <c>VisualTokens.qml</c> for Windows.
    ///
    /// The geometry is the approved V1 presentation contract: a 35 px strip with
    /// a 38 px exclusive band, a 32 px island inset 5 px with a 16 px radius, and
    /// 24 px pills (12 px radius, 9 px per-side padding, 5 px content gap). The
    /// colours are the contract's semantic roles — paper, ink, sumi, seal — mixed
    /// exactly as the QML does, so a theme reaches the bar through the same
    /// formulas instead of a second hand-tuned palette.
    /// </summary>
    public sealed class ShibumiTokens
    {
        // ---- geometry (V1 presentation contract) --------------------------
        public int BarHeight { get; init; } = 35;
        public int ExclusiveHeight { get; init; } = 40;
        public int IslandHeight { get; init; } = 32;
        public int IslandInsetX { get; init; } = 5;
        public int IslandContentInsetX { get; init; } = 4;
        public int IslandOffsetY { get; init; } = 3;
        public int SlotHeight { get; init; } = 28;
        public int PillHeight { get; init; } = 24;
        public int PillRadius { get; init; } = 12;
        public int IslandRadius { get; init; } = 16;
        public int TileRadius { get; init; } = 10;
        public int PillPaddingX { get; init; } = 9;
        public int IconSize { get; init; } = 15;
        public int ContentGap { get; init; } = 5;
        public int CompactGap { get; init; } = 4;
        public int GroupGap { get; init; } = 6;
        public int SplitGap { get; init; } = 16;
        public int TooltipRadius { get; init; } = 6;
        public int TooltipPaddingX { get; init; } = 10;
        public int TooltipPaddingY { get; init; } = 4;

        // ---- motion (contract durations, ms) ------------------------------
        public int WidgetFadeMs { get; init; } = 140;
        public int GeometryMs { get; init; } = 160;
        public int ColorMs { get; init; } = 160;

        // ---- surfaces -----------------------------------------------------
        public bool BorderEnabled { get; init; } = true;
        public bool PanelBorderEnabled { get; init; } = true;
        public bool ShadowEnabled { get; init; }
        public bool FrostEnabled { get; init; } = true;
        public int PillBorderWidth => BorderEnabled ? 1 : 0;
        public int PanelBorderWidth => PanelBorderEnabled ? 1 : 0;

        public Color Paper { get; init; }
        public Color Ink { get; init; }
        public Color Sumi { get; init; }
        public Color SumiHi { get; init; }
        public Color Seal { get; init; }
        public Color MutedInk => Sumi;
        public Color PillBorder { get; init; }
        public Color IslandBorder { get; init; }
        public Color ShellBorder { get; init; }
        public Color Separator { get; init; }

        public Color BarBackground { get; init; }
        public Color PanelBackground { get; init; }
        public Color PillShadow { get; init; }
        public Color ShellShadow { get; init; }

        public Color FillActive { get; init; }
        public Color FillHover { get; init; }
        public Color FillIdle { get; init; }
        public Color FillPrimaryHover { get; init; }

        /// <summary>Builds the token set from the active Omarchy theme.</summary>
        public static ShibumiTokens From(AppearanceConfig a, ShibumiConfig cfg)
        {
            a ??= new AppearanceConfig();
            cfg ??= new ShibumiConfig();

            var theme = ThemeLibrary.Find(a.BarTheme);

            Color paper = Hex(theme?.Background ?? a.BarBackground, Color.FromRgb(0x12, 0x12, 0x12));
            Color ink = Hex(theme?.Foreground ?? a.BarForeground, Color.FromRgb(0xD9, 0xD9, 0xD9));

            // Shibumi's sumi is exactly Commons.Color.muted, not a mix. The
            // theme's muted role is the secondary ink by definition; replacing
            // it with a hand-tuned grey is what made this port drift.
            Color sumi = Mix(paper, ink, 0.58);
            if (theme != null && !string.IsNullOrWhiteSpace(theme.Muted))
                sumi = Hex(theme.Muted, sumi);
            Color seal = Hex(theme?.Accent ?? a.ActiveWorkspaceBackground, Color.FromRgb(0xC9, 0xA2, 0x27));

            bool frost = cfg.Frost;
            return new ShibumiTokens
            {
                // Geometry can be overridden, but defaults are the contract values.
                BarHeight = Math.Clamp(cfg.BarHeight <= 0 ? 35 : cfg.BarHeight, 26, 56),
                ExclusiveHeight = Math.Clamp((cfg.BarHeight <= 0 ? 35 : cfg.BarHeight) + 5, 28, 60),
                IslandHeight = Math.Clamp(cfg.PillHeight + 8, 24, 48),
                IslandRadius = Math.Clamp(cfg.IslandRadius, 4, 24),
                PillHeight = Math.Clamp(cfg.PillHeight, 16, 40),
                PillRadius = Math.Clamp(cfg.PillRadius, 4, 20),
                PillPaddingX = Math.Clamp(cfg.PillPaddingX, 2, 20),
                TileRadius = Math.Max(1, Math.Clamp(cfg.PillRadius, 4, 20) - 2),

                BorderEnabled = cfg.Borders,
                PanelBorderEnabled = cfg.Borders,
                ShadowEnabled = cfg.Shadow,
                FrostEnabled = frost,

                Paper = paper,
                Ink = ink,
                Sumi = sumi,
                SumiHi = Mix(sumi, ink, 0.55),
                Seal = seal,

                // Qt.rgba(paper, ink, 0.13 / 0.16 / 0.22) — same three mixes.
                PillBorder = Mix(paper, ink, 0.13),
                IslandBorder = Mix(paper, ink, 0.16),
                ShellBorder = Mix(paper, ink, 0.22),
                Separator = WithAlpha(ink, 0.18),

                BarBackground = WithAlpha(paper, frost ? 0.68 : 0.94),
                PanelBackground = WithAlpha(paper, 0.94),
                PillShadow = WithAlpha(Colors.Black, 0.55),
                ShellShadow = WithAlpha(Colors.Black, 0.46),

                FillActive = WithAlpha(seal, 0.18),
                FillHover = WithAlpha(seal, 0.10),
                FillIdle = WithAlpha(Colors.Black, 0.12),
                FillPrimaryHover = Lighten(seal, 1.15)
            };
        }

        /// <summary>Qt.rgba-mix of two colours; matches Shibumi's <c>mix()</c>.</summary>
        public static Color Mix(Color from, Color to, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromRgb(
                (byte)Math.Round(from.R * (1 - amount) + to.R * amount),
                (byte)Math.Round(from.G * (1 - amount) + to.G * amount),
                (byte)Math.Round(from.B * (1 - amount) + to.B * amount));
        }

        public static Color WithAlpha(Color color, double alpha) =>
            Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

        private static double Luminance(Color color) =>
            (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

        private static Color Lighten(Color color, double factor) => Color.FromRgb(
            (byte)Math.Min(255, Math.Round(color.R * factor)),
            (byte)Math.Min(255, Math.Round(color.G * factor)),
            (byte)Math.Min(255, Math.Round(color.B * factor)));

        private static Color Hex(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(ThemeColor.Normalize(hex)); }
            catch { return fallback; }
        }
    }
}
