using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// Shared look for every WorkspaceOS window, themed after the Geometry Dash
    /// wallpaper: void-black space, electric cyan energy, white ink, chunky 3px
    /// edges — and the embedded JetBrainsMono Nerd Font so glyph modules never
    /// fall back to tofu boxes.
    /// </summary>
    public static class UiTheme
    {
        // Geometry Dash palette (wallpaper: black space, cyan energy, white icon).
        public static readonly Color Paper = Color.FromRgb(0x05, 0x08, 0x0A);
        public static readonly Color Surface = Color.FromRgb(0x08, 0x10, 0x12);
        public static readonly Color SurfaceRaised = Color.FromRgb(0x0D, 0x1A, 0x1C);
        public static readonly Color Ink = Color.FromRgb(0xE8, 0xF8, 0xF5);
        public static readonly Color Sumi = Color.FromRgb(0x8F, 0xB3, 0xAF);
        public static readonly Color Accent = Color.FromRgb(0x00, 0xE0, 0xCF);
        public static readonly Color Green = Color.FromRgb(0x4F, 0xE3, 0xA5);
        public static readonly Color Yellow = Color.FromRgb(0xF2, 0xD3, 0x5C);
        public static readonly Color Blue = Color.FromRgb(0x35, 0xB6, 0xFF);
        public static readonly Color Mauve = Color.FromRgb(0xB0, 0x8C, 0xFF);
        public static readonly Color Cyan = Color.FromRgb(0x00, 0xE0, 0xCF);
        public static readonly Color Cream = Color.FromRgb(0xC8, 0xC0, 0x93);
        public static readonly Color Line = Color.FromRgb(0x13, 0x32, 0x30);

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

        /// <summary>Windows 11 dark title bar so Settings/Monitor match the bar.</summary>
        public static void ApplyDarkTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
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
                int gradient = (alpha << 24) | (tint.B << 16) | (tint.G << 8) | tint.R;
                var policy = new AccentPolicy
                {
                    AccentState = 4,   // ACCENT_ENABLE_ACRYLICBLURBEHIND
                    AccentFlags = 2,
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
                    return SetWindowCompositionAttribute(hwnd, ref data) != 0;
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch { return false; }
        }
    }
}
