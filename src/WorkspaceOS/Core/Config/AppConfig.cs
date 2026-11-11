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
        // Retained for the separate Linux daemon. Windows nulls this on load
        // and omits it from config.json now that tiling is removed there.
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
        // Defaults follow the Geometry Dash wallpaper: deep space black,
        // electric cyan energy, white ink and chunky neon edges.
        public string BarBackground { get; set; } = "#FF05080A";              // void black, faint teal cast
        public string BarForeground { get; set; } = "#FFE8F8F5";              // white ink
        public string AccentColor { get; set; } = "#FF7FA7A3";                // muted teal — inactive workspaces
        public string ActiveWorkspaceBackground { get; set; } = "#FF00E0CF";  // GD energy cyan
        public string ActiveWorkspaceForeground { get; set; } = "#FF04100F";  // void on cyan
        public string ModuleLabelColor { get; set; } = "#FF6FD8CF";           // neon cyan labels
        public string SeparatorColor { get; set; } = "#FF123A37";             // dark teal hairline
        public string FontFamily { get; set; } = "JetBrainsMono Nerd Font, Cascadia Mono, Consolas";
        public double FontSize { get; set; } = 12.5;
        public int BarHeight { get; set; } = 32;       // Quickshell V2 compact strip
        public string Theme { get; set; } = "Dark";
        public int BarThemeVersion { get; set; } = 4;   // 0/1 = pre-Quickshell, 2 = Pokémon, 3 = Rise, 4 = Geometry Dash
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
        public string IndicatorColor { get; set; } = "#FF00E0CF";  // GD cyan, matches the bar accent

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
        public Dictionary<string, string> Bindings { get; set; } = new()
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
            ["Launcher"] = "Alt+Space",
            ["StartMenu"] = "Win+Space",
            ["ClipboardManager"] = "Win+V",
            ["ScratchpadSend"] = "Ctrl+Win+S",
            ["ScratchpadToggle"] = "Win+S",
            ["Screenshot"] = "Win+Shift+S",
            ["SendToWorkspace1"] = "Win+Shift+1",
            ["SendToWorkspace2"] = "Win+Shift+2",
            ["SendToWorkspace3"] = "Win+Shift+3",
            ["SendToWorkspace4"] = "Win+Shift+4",

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
        };

        public HotkeyConfig()
        {
            // The Linux daemon still owns its separate Dwindle/X11 tiling port,
            // so keep its preselect chords and monitor keys disabled. The Windows
            // app no longer ships any tiling actions.
            if (!System.OperatingSystem.IsWindows())
            {
                Bindings["MoveWindowToMonitorLeft"] = "";
                Bindings["MoveWindowToMonitorRight"] = "";
                Bindings["MoveWindowToMonitorUp"] = "";
                Bindings["MoveWindowToMonitorDown"] = "";
                Bindings["TilePreselectLeft"] = "Win+Shift+Left";
                Bindings["TilePreselectRight"] = "Win+Shift+Right";
                Bindings["TilePreselectUp"] = "Win+Shift+Up";
                Bindings["TilePreselectDown"] = "Win+Shift+Down";
                return;
            }

            foreach (var key in Bindings.Keys.Where(IsTilingAction).ToList())
                Bindings.Remove(key);
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
