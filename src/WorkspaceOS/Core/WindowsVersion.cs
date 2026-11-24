using System;

namespace WorkspaceOS.Core
{
    /// <summary>
    /// The one place that answers "what can this Windows do?".
    ///
    /// The registry's <c>ProductName</c> cannot be used for this: a Windows 11
    /// install still reports "Windows 10 Home" / "Windows 10 Pro" there, so every
    /// decision is made from the build number instead:
    ///
    ///   10240 … 19045   Windows 10 (22H2 ends at 19045)
    ///   22000 … 26099   Windows 11 21H2 … 23H2
    ///   26100 …         Windows 11 24H2 / 25H2 and later
    /// </summary>
    public static class WindowsVersion
    {
        /// <summary>First build with <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> (attribute 19).</summary>
        public const int Win10_1809 = 17763;

        /// <summary>Windows 10 2004 — acrylic's compositor stops drag-lagging here.</summary>
        public const int Win10_2004 = 19041;

        public const int Win11_21H2 = 22000;
        public const int Win11_24H2 = 26100;

        public static int Build => Environment.OSVersion.Version.Build;

        public static bool IsWindows10 => Build < Win11_21H2;
        public static bool IsWindows11 => Build >= Win11_21H2;
        public static bool IsWindows11_24H2OrLater => Build >= Win11_24H2;

        /// <summary>Windows 10 1809+ and every Windows 11 can draw a dark title bar.</summary>
        public static bool SupportsDarkTitleBar => SupportsDarkTitleBarOn(Build);
        public static bool SupportsDarkTitleBarOn(int build) => build >= Win10_1809;

        /// <summary>
        /// Windows 10 before 2004 keeps the classic blur. Its acrylic compositor
        /// has the long-standing drag-lag bug, so the bar asks for
        /// <c>ACCENT_ENABLE_BLURBEHIND</c> there instead; the tint still comes from
        /// the bar's own translucent brush.
        /// </summary>
        public static bool PrefersClassicBlur => PrefersClassicBlurOn(Build);
        public static bool PrefersClassicBlurOn(int build) => build < Win10_2004;

        /// <summary>The virtual-desktop interface set that matches this build.</summary>
        public static string DesktopInterfaceSet(int build) =>
            build >= Win11_24H2 ? "win11-24h2" :
            build >= Win11_21H2 ? "win11" :
            "win10";

        public static string FamilyName(int build) =>
            build >= Win11_24H2 ? "11 24H2+" :
            build >= Win11_21H2 ? "11" :
            "10";

        /// <summary>"Windows 10 (build 19045)" — used in diagnostics.</summary>
        public static string Describe() => Describe(Build);
        public static string Describe(int build) => $"Windows {FamilyName(build)} (build {build})";
    }
}
