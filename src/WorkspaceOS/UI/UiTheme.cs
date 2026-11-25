using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WorkspaceOS.Core;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Shared look for every WorkspaceOS window. The neutrals follow the active
    /// Omarchy theme through <see cref="Refresh"/>, so the bar, settings, launcher
    /// and calendar always share one palette. Green/Yellow/Blue stay semantic
    /// (battery good/low, bandwidth) rather than thematic.
    /// </summary>
    public static class UiTheme
    {
        public static Color Paper { get; private set; } = Color.FromRgb(0x12, 0x12, 0x12);
        public static Color Surface { get; private set; } = Color.FromRgb(0x1B, 0x1B, 0x1B);
        public static Color SurfaceRaised { get; private set; } = Color.FromRgb(0x24, 0x24, 0x24);
        public static Color Ink { get; private set; } = Color.FromRgb(0xD9, 0xD9, 0xD9);
        public static Color Sumi { get; private set; } = Color.FromRgb(0x86, 0x86, 0x86);
        public static Color Accent { get; private set; } = Color.FromRgb(0xC9, 0xA2, 0x27);
        public static Color Green { get; private set; } = Color.FromRgb(0x4F, 0xE3, 0xA5);
        public static Color Yellow { get; private set; } = Color.FromRgb(0xF2, 0xD3, 0x5C);
        public static Color Blue { get; private set; } = Color.FromRgb(0x35, 0xB6, 0xFF);
        public static Color Mauve { get; private set; } = Color.FromRgb(0xB0, 0x8C, 0xFF);
        public static Color Cyan { get; private set; } = Color.FromRgb(0x7F, 0xBB, 0xB3);
        public static Color Cream { get; private set; } = Color.FromRgb(0xC8, 0xC0, 0x93);
        public static Color Line { get; private set; } = Color.FromRgb(0x33, 0x33, 0x33);

        /// <summary>
        /// Re-derives every neutral from the active bar palette. Called on each
        /// ApplyConfig, so cycling a theme repaints all WorkspaceOS surfaces.
        /// </summary>
        public static void Refresh(AppearanceConfig a)
        {
            if (a == null) return;
            Paper = Parse(a.BarBackground);
            Ink = Parse(a.BarForeground);
            Accent = Parse(a.ActiveWorkspaceBackground);
            Surface = Blend(Paper, Ink, 0.06);
            SurfaceRaised = Blend(Paper, Ink, 0.13);
            Sumi = Blend(Paper, Ink, 0.58);
            Line = Parse(a.SeparatorColor);
            Cyan = Accent;
            Green = ParseOr(a.ActiveWorkspaceBackground, Color.FromRgb(0x4F, 0xE3, 0xA5));
        }

        private static Color Parse(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Paper; }
        }

        private static Color ParseOr(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return fallback; }
        }

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>The embedded Nerd Font; falls back to Cascadia/Consolas if the resource is missing.</summary>
        public static FontFamily Mono
        {
            get
            {
                var font = Application.Current?.TryFindResource("UiFont") as FontFamily;
                return font ?? new FontFamily("Cascadia Mono, Consolas");
            }
        }

        /// <summary>
        /// Resolves a configured font. The shipped default (and anything naming
        /// JetBrainsMono Nerd Font) uses the embedded family, because the font is not
        /// installed system-wide — a plain FontFamily("JetBrainsMono Nerd Font") would
        /// silently fall back and lose the glyphs.
        /// </summary>
        public static FontFamily ResolveFont(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) return Mono;
            if (configured.IndexOf("JetBrainsMono", StringComparison.OrdinalIgnoreCase) >= 0) return Mono;
            return new FontFamily(configured);
        }

        /// <summary>Dark title bar (Windows 10 1809+ and every Windows 11) so Settings/Monitor match the bar.</summary>
        public static void ApplyDarkTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                if (!WindowsVersion.SupportsDarkTitleBar) return;   // 1803 and older have no dark caption
                int enabled = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE on Windows 11 (19 on older 10 builds).
                if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(
            IntPtr hwnd, ref WindowCompositionAttributeData data);

        /// <summary>
        /// Windows' acrylic blur behind a custom window. The bar uses it so the
        /// wallpaper is felt through the surface instead of being covered by an
        /// opaque black strip. Returns false on systems without the compositor,
        /// where the caller falls back to a translucent gradient.
        /// </summary>
        public static bool TryEnableAcrylic(IntPtr hwnd, Color tint, byte alpha)
        {
            try
            {
                bool classicBlur = WindowsVersion.PrefersClassicBlur;
                int gradient = (alpha << 24) | (tint.B << 16) | (tint.G << 8) | tint.R;
                var policy = new AccentPolicy
                {
                    // 3 = ACCENT_ENABLE_BLURBEHIND on Windows 10 before 2004, where
                    // the acrylic compositor drag-lags; 4 = acrylic everywhere else.
                    AccentState = classicBlur ? 3 : 4,
                    AccentFlags = classicBlur ? 0 : 2,
                    GradientColor = gradient
                };
                int size = Marshal.SizeOf<AccentPolicy>();
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(policy, ptr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = 19,   // WCA_ACCENT_POLICY
                        Data = ptr,
                        SizeOfData = size
                    };
                    if (SetWindowCompositionAttribute(hwnd, ref data) == 0) return false;
                    // Classic blur carries no tint, so the caller keeps painting its
                    // translucent brush over it; acrylic supplies the tint itself.
                    return !classicBlur;
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch { return false; }
        }
    }
}
