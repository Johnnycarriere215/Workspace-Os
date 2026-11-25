using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace WorkspaceOS.Core.Config
{
    public class AppConfig
    {
        public GeneralConfig General { get; set; } = new();
        public AppearanceConfig Appearance { get; set; } = new();
        public List<WorkspaceConfig> Workspaces { get; set; } = new()
        {
            new WorkspaceConfig { Index = 1, Name = "1" },
            new WorkspaceConfig { Index = 2, Name = "2" },
            new WorkspaceConfig { Index = 3, Name = "3" },
            new WorkspaceConfig { Index = 4, Name = "4" },
        };
        public List<WindowRule> Rules { get; set; } = new();
        public HotkeyConfig Hotkeys { get; set; } = new();
        public BarConfig Bar { get; set; } = new();
        public MonitorConfig Monitor { get; set; } = new();
        public FocusConfig Focus { get; set; } = new();
        public AutomationConfig Automation { get; set; } = new();
        public TaskbarConfig Taskbar { get; set; } = new();
        // Retained for the separate Linux daemon. Windows nulls this on load
        // and omits it from config.json; the Windows app has no tiling engine.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TilingConfig Tiling { get; set; } = new();
    }

    public class GeneralConfig
    {
        public bool StartWithWindows { get; set; } = true;
        public bool MaximizeNewWindows { get; set; } = true;
        // Release the bar's appbar strip and hide it while a fullscreen game or
        // fullscreen/borderless app owns the screen.
        public bool HideBarInFullscreen { get; set; } = true;
        // Focus the top-level window under a stationary mouse cursor after a
        // short hover. This is focus-follows-mouse, not click-through.
        public bool FocusWindowOnHover { get; set; } = true;
        public int ActiveWorkspace { get; set; } = 1;
        public bool RestoreWorkspacesOnStart { get; set; } = true;
    }

    public class AppearanceConfig
    {
        // Defaults are the Omarchy "Gold Rush" palette. Every Omarchy theme
        // maps onto these same slots, so cycling a theme only rewrites them.
        public string BarBackground { get; set; } = "#FF121212";

        /// <summary>Set the desktop wallpaper to the theme's own when it changes.</summary>
        public bool ApplyThemeWallpaper { get; set; } = true;
        public string BarForeground { get; set; } = "#FFD9D9D9";
        public string AccentColor { get; set; } = "#FF805B10";                // muted — inactive workspaces
        public string ActiveWorkspaceBackground { get; set; } = "#FFC9A227";
        public string ActiveWorkspaceForeground { get; set; } = "#FF121212";
        public string ModuleLabelColor { get; set; } = "#FFC9A227";
        public string SeparatorColor { get; set; } = "#FF333333";
        public string ModuleBackground { get; set; } = "#FF1F1F1F";
        public string ModuleBorder { get; set; } = "#FF333333";
        // Shibumi's own default launcher identity is the SHIBUMI wordmark; the
        // menu also offers the actual Omarchy, Hyprland, Arch and OMACOM assets.
        public string BarIcon { get; set; } = "Shibumi";
        public string BarIconPath { get; set; } = "";                         // optional custom PNG/JPG
        public int BarIconWidth { get; set; } = 84;                            // px
        public bool ShowBarIcon { get; set; } = true;
        public string FontFamily { get; set; } = "JetBrainsMono Nerd Font, Cascadia Mono, Consolas";
        public double FontSize { get; set; } = 12.5;
        public int BarHeight { get; set; } = 32;
        public string Theme { get; set; } = "Dark";
        /// <summary>Active Omarchy theme name; drives bar, tiler and taskbar preset.</summary>
        public string BarTheme { get; set; } = "Gold Rush";
        public int BarThemeVersion { get; set; } = 5;   // 5 = Omarchy palette system
    }

    public class WorkspaceConfig
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// Tiling window manager configuration — Hyprland-inspired.
    /// Defaults mirror Hyprland's Dwindle layout with sensible Windows behavior.
    /// </summary>
    public class TilingConfig
    {
        // --- General ---
        public bool EnableTiling { get; set; } = true;            // v4: Hyprland-style tiling is the default
        public bool ManageNewWindows { get; set; } = true;         // tile windows that open later
        public int SchemaVersion { get; set; } = 4;                // 0–3 = pre-v4 config (opt-in tiling)

        // --- Dwindle layout ---
        public bool PreserveSplit { get; set; } = false;           // Hyprland dwindle:preserve_split
        public bool SmartSplit { get; set; } = false;              // split where the cursor enters a window
        public string DefaultSplit { get; set; } = "Auto";         // Auto | Horizontal | Vertical
        public double DefaultSplitRatio { get; set; } = 0.5;       // Hyprland default 0.5
        public string SplitBias { get; set; } = "New";             // "New" (active keeps size) | "Active" (new window takes 50%)
        public double MinSplitRatio { get; set; } = 0.1;
        public double MaxSplitRatio { get; set; } = 0.9;
        public string PersistentPreselect { get; set; } = "None";   // None | Left | Right | Up | Down

        // --- Floating / pseudotiling ---
        public bool Pseudotile { get; set; } = false;              // center preferred size inside tiled slot

        // --- Appearance ---
        public int InnerGap { get; set; } = 6;
        public int OuterGap { get; set; } = 6;
        public bool SmartGaps { get; set; } = true;                // no gaps with a single window
        public bool ActiveIndicator { get; set; } = true;          // accent border on the focused window
        public string IndicatorColor { get; set; } = "#FFC9A227";  // Gold Rush accent, matches the bar

        // --- Behavior ---
        public bool FollowMovedWindow { get; set; } = true;        // switch to the workspace a window was sent to
        public bool FocusWrap { get; set; } = false;               // directional focus wraps around
        public int ResizeStep { get; set; } = 5;                   // percent of the split changed per press
        public int MinWindowSize { get; set; } = 120;              // px; never tile below this (avoids dead slots)
        public bool CenterSingleWindow { get; set; } = false;
        public int SingleWindowMaxWidthPct { get; set; } = 0;      // 0 = disabled; else 10-100

        // --- Scratchpad ---
        public bool EnableScratchpad { get; set; } = false;

        // --- Tiling window rules (matched like workspace rules; Action decides what happens) ---
        public List<TilingRule> TilingRules { get; set; } = new();
    }

    /// <summary>
    /// The Windows taskbar surface. WorkspaceOS sets the shell taskbar's accent
    /// policy (the TranslucentTB mechanism), so the taskbar can be clear, blurred
    /// or acrylic without patching Explorer; the Windhawk preset still owns the
    /// floating-pill geometry.
    /// </summary>
    public class TaskbarConfig
    {
        public bool EnableAppearance { get; set; } = true;

        /// <summary>Dynamic | Clear | Blur | Acrylic | Disabled.</summary>
        public string Mode { get; set; } = "Dynamic";

        /// <summary>Acrylic tint strength, 0–1. Clear/Blur ignore it.</summary>
        public double Opacity { get; set; } = 0.35;
    }

    /// <summary>Global-hotkey automation settings (independent of the Linux tiling port).</summary>
    public class AutomationConfig
    {
        public bool EnableAutoHotkey { get; set; } = true;         // runs bundled AHK v2 for reliable hotkeys
        public string AutoHotkeyPath { get; set; } = "";           // custom AutoHotkey64.exe (empty = bundled)
    }

    /// <summary>Tiling rule. Action: Float | Tile | Ignore | Workspace</summary>
    public class TilingRule
    {
        public string Match { get; set; } = "";
        public string MatchType { get; set; } = "Executable";      // Executable | Process | Title | Class | Regex
        public string Action { get; set; } = "Float";              // Float | Tile | Ignore | Workspace
        public int Workspace { get; set; } = 1;                    // used when Action = Workspace
        public bool Enabled { get; set; } = true;
    }

    /// <summary>Automatic workspace assignment rule. Workspace 0 = all workspaces (never moved).</summary>
    public class WindowRule
    {
        public string Match { get; set; } = "";        // pattern text
        public string MatchType { get; set; } = "Executable"; // Executable | Process | Title | Class | Regex
        public int Workspace { get; set; } = 1;        // 0 = pin to all workspaces
        public bool Enabled { get; set; } = true;
    }

    public class HotkeyConfig
    {
        // Format: "Win+1", "Win+Shift+M", "Alt+Space", "Ctrl+Alt+P"
        public Dictionary<string, string> Bindings { get; set; } = CreateDefaults();

        /// <summary>
        /// The shipped binding table before the per-platform pass. A method rather
        /// than an initializer so the tests can inspect the Windows set on any
        /// host OS.
        /// </summary>
        public static Dictionary<string, string> CreateDefaults() => new()
        {
            ["Workspace1"] = "Win+1",
            ["Workspace2"] = "Win+2",
            ["Workspace3"] = "Win+3",
            ["Workspace4"] = "Win+4",
            ["Workspace5"] = "Win+5",
            ["Workspace6"] = "Win+6",
            ["Workspace7"] = "Win+7",
            ["Workspace8"] = "Win+8",
            ["Workspace9"] = "Win+9",
            ["MoveWindowLeft"] = "Win+Left",
            ["MoveWindowRight"] = "Win+Right",
            ["MoveWindowUp"] = "Win+Up",
            ["MoveWindowDown"] = "Win+Down",
            ["MoveWindowToMonitorLeft"] = "Win+Shift+Left",
            ["MoveWindowToMonitorRight"] = "Win+Shift+Right",
            ["MoveWindowToMonitorUp"] = "Win+Shift+Up",
            ["MoveWindowToMonitorDown"] = "Win+Shift+Down",
            ["CloseWindow"] = "Win+W",
            ["MaximizeWindow"] = "Win+M",
            ["RestoreWindow"] = "Win+Shift+M",
            ["CenterWindow"] = "Win+C",
            ["FullscreenWindow"] = "Win+F",
            ["FocusMode"] = "Win+F1",
            // Win+Space keeps the desktop's own behaviour: it opens the Start
            // menu through the generated AutoHotkey script. The command palette
            // lives on Alt+Space, which the shell does not own.
            ["Launcher"] = "Alt+Space",
            ["LauncherAlt"] = "",
            ["StartMenu"] = "Win+Space",
            ["ClipboardManager"] = "Win+V",
            // Opens the Audio sheet under the bar; Up/Down then step the level
            // (including the output's boost headroom) without touching the mouse.
            ["VolumePanel"] = "Ctrl+Win+A",
            ["ScratchpadSend"] = "Ctrl+Win+S",
            ["ScratchpadToggle"] = "Win+S",
            ["SendToWorkspace1"] = "Win+Shift+1",
            ["SendToWorkspace2"] = "Win+Shift+2",
            ["SendToWorkspace3"] = "Win+Shift+3",
            ["SendToWorkspace4"] = "Win+Shift+4",
            ["SendToWorkspace5"] = "Win+Shift+5",
            ["SendToWorkspace6"] = "Win+Shift+6",
            ["SendToWorkspace7"] = "Win+Shift+7",
            ["SendToWorkspace8"] = "Win+Shift+8",
            ["SendToWorkspace9"] = "Win+Shift+9",

            // Tiling (Hyprland-inspired). Empty string = disabled.
            ["TileFocusLeft"] = "Win+H",
            ["TileFocusDown"] = "Win+J",
            ["TileFocusUp"] = "Win+K",
            ["TileFocusRight"] = "Win+L",
            ["TileMoveLeft"] = "Win+Shift+H",
            ["TileMoveDown"] = "Win+Shift+J",
            ["TileMoveUp"] = "Win+Shift+K",
            ["TileMoveRight"] = "Win+Shift+L",
            ["TileResizeLeft"] = "Win+Ctrl+H",
            ["TileResizeDown"] = "Win+Ctrl+J",
            ["TileResizeUp"] = "Win+Ctrl+K",
            ["TileResizeRight"] = "Win+Ctrl+L",
            ["TileToggleFloating"] = "Win+Shift+Space",
            ["TileToggleSplit"] = "Win+P",
            ["TileTogglePseudotile"] = "Win+Shift+P",
            // Monitor moves own Win+Shift+Arrow now; bind preselect in Settings if needed.
            ["TilePreselectLeft"] = "",
            ["TilePreselectRight"] = "",
            ["TilePreselectUp"] = "",
            ["TilePreselectDown"] = "",
            ["TileScratchpadToggle"] = "Win+S",
            ["TileScratchpadSend"] = "",
            ["ToggleTiling"] = "Win+Shift+T",
            // The chord opens Omarchy's Hearthstone picker: arrow through the
            // theme cards, Enter applies. Cycling itself stays on the bar menu.
            ["ThemePicker"] = "Ctrl+Win+Shift+Space",
            ["CycleTheme"] = "",
        };

        public HotkeyConfig() => ApplyPlatformDefaults(Bindings, System.OperatingSystem.IsWindows());

        /// <summary>
        /// Removes the bindings a platform cannot use. The Linux daemon still owns
        /// its separate Dwindle/X11 tiling port, so it keeps the preselect chords;
        /// the Windows app ships no tiling actions at all.
        /// </summary>
        public static void ApplyPlatformDefaults(Dictionary<string, string> bindings, bool windows)
        {
            if (bindings == null) return;

            if (!windows)
            {
                // Win+Space belongs to the desktop's input switcher on Linux, so
                // the launcher stays on Alt+Space there.
                bindings["Launcher"] = "Alt+Space";
                bindings.Remove("LauncherAlt");
                bindings["MoveWindowToMonitorLeft"] = "";
                bindings["MoveWindowToMonitorRight"] = "";
                bindings["MoveWindowToMonitorUp"] = "";
                bindings["MoveWindowToMonitorDown"] = "";
                bindings["TilePreselectLeft"] = "Win+Shift+Left";
                bindings["TilePreselectRight"] = "Win+Shift+Right";
                bindings["TilePreselectUp"] = "Win+Shift+Up";
                bindings["TilePreselectDown"] = "Win+Shift+Down";
                // The daemon's tiling scratchpad and the shell's scratchpad were
                // both on Win+S; one chord cannot do two things, so the shell's
                // keeps it and the tiling one stays unbound (rebindable).
                bindings["TileScratchpadToggle"] = "";
                return;
            }

            foreach (var key in bindings.Keys.Where(IsTilingAction).ToList())
                bindings.Remove(key);

            // Win+Arrow is Aero Snap on Windows: halving, maximizing and docking a
            // window with the keyboard. The shell's nudge actions used to sit on
            // those chords, which meant snapping stopped working for anyone who
            // ran it. Nudging stays available in Settings for whoever wants it;
            // moving to another monitor keeps Win+Shift+Arrow.
            foreach (var key in new[] { "MoveWindowLeft", "MoveWindowRight", "MoveWindowUp", "MoveWindowDown" })
                bindings[key] = "";
        }

        /// <summary>True for bindings that belonged to the removed Windows tiling engine.</summary>
        public static bool IsTilingAction(string name) =>
            string.Equals(name, "ToggleTiling", System.StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Tile", System.StringComparison.OrdinalIgnoreCase);
    }

    public class BarConfig
    {
        // Workspace chips drawn on the bar. Slots beyond the number of native
        // desktops are shown dimmed and created the first time they are used.
        public int WorkspaceSlots { get; set; } = 5;

        // Right-side modules in order
        public List<string> Modules { get; set; } = new() { "CPU", "RAM", "GPU", "Disk", "NetUp", "NetDown", "Battery", "Volume" };
        public int RefreshMs { get; set; } = 2000;
        public bool ShowSeconds { get; set; } = false;

        /// <summary>Clock format. Off (the default) is standard 12-hour with AM/PM.</summary>
        public bool Use24Hour { get; set; } = false;

        /// <summary>Bar form, switched from the logo menu: Shibumi | Full | Fit | Dock | Notch.</summary>
        public string Profile { get; set; } = "Shibumi";

        /// <summary>Legacy duplicate of Appearance.BarIcon; kept for old configs.</summary>
        public string Logo { get; set; } = "Shibumi";

        // cava audio bars in the centre cluster.
        public bool ShowCava { get; set; } = true;
        public int CavaBars { get; set; } = 18;
        public string CavaPath { get; set; } = "";

        /// <summary>Geometry for the Shibumi bar form (its V1 presentation tokens).</summary>
        public ShibumiConfig Shibumi { get; set; } = new();
    }

    /// <summary>
    /// The Shibumi bar form's token set, taken from the QS Rise V1 presentation
    /// contract: a 35px strip, a 32px island (16px radius, 5px inset) and 24px
    /// pills (12px radius, 9px padding per side, 5px gaps).
    /// </summary>
    public class ShibumiConfig
    {
        public int BarHeight { get; set; } = 35;      // bar strip, px
        public int IslandRadius { get; set; } = 16;   // island corner radius
        public int PillHeight { get; set; } = 24;
        public int PillRadius { get; set; } = 12;
        public int PillPaddingX { get; set; } = 9;    // per side
        public bool Frost { get; set; } = false;      // Shibumi V1 is opaque paper (0.94)
        public bool Borders { get; set; } = true;     // 1 px pill/island borders
        public bool Shadow { get; set; } = false;     // panel shadow (off in V1)
        public string WorkspaceMarker { get; set; } = "Numbers"; // Numbers | Dots

        /// <summary>Plugin ids the user hid from the bar.</summary>
        public List<string> HiddenWidgets { get; set; } = new();

        /// <summary>Plugin id (or bar key) → left | center | right override.</summary>
        public Dictionary<string, string> Sections { get; set; } = new();

        /// <summary>1 once the Shibumi layout migration has run.</summary>
        public int LayoutVersion { get; set; } = 0;
    }

    public class MonitorConfig
    {
        public int RefreshMs { get; set; } = 1500;
        public int ProcessCount { get; set; } = 12;
    }

    public class FocusConfig
    {
        public List<int> PresetMinutes { get; set; } = new() { 15, 25, 45, 60 };
        public List<string> BlockedApps { get; set; } = new() { "discord.exe" };
        public int LastDurationMinutes { get; set; } = 25;
    }
}
