using System.Collections.Generic;

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
        public TilingConfig Tiling { get; set; } = new();
    }

    public class GeneralConfig
    {
        public bool StartWithWindows { get; set; } = true;
        public int ActiveWorkspace { get; set; } = 1;
        public bool RestoreWorkspacesOnStart { get; set; } = true;
    }

    public class AppearanceConfig
    {
        // Defaults mirror the user's Quickshell/Omarchy bar running the Pokémon
        // (Charizard) theme: navy strip, cream text, steel-blue secondary,
        // red accent pill on the focused workspace.
        public string BarBackground { get; set; } = "#FF0F2138";              // navy
        public string BarForeground { get; set; } = "#FFFDF4C4";              // cream
        public string AccentColor { get; set; } = "#FF7893B4";                // muted steel — inactive workspaces
        public string ActiveWorkspaceBackground { get; set; } = "#FFC56363";  // Charizard red pill
        public string ActiveWorkspaceForeground { get; set; } = "#FF0F2138";  // navy on red
        public string ModuleLabelColor { get; set; } = "#FF7893B4";           // module labels (CPU, RAM…)
        public string SeparatorColor { get; set; } = "#FF434F57";             // bar bottom border / separators (paper↔ink @0.22)
        public string FontFamily { get; set; } = "JetBrainsMono Nerd Font, Cascadia Mono, Consolas";
        public double FontSize { get; set; } = 13;
        public int BarHeight { get; set; } = 33;       // matches the Quickshell V2 bar height
        public string Theme { get; set; } = "Dark";
        public int BarThemeVersion { get; set; } = 2;   // 0/1 = pre-Quickshell look; migration bumps to 2
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
        public bool EnableTiling { get; set; } = false;           // opt-in; normal Windows until enabled
        public bool ManageNewWindows { get; set; } = true;         // tile windows that open later

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
        public string IndicatorColor { get; set; } = "#FFFFD75F";  // matches the bar accent

        // --- Behavior ---
        public bool FollowMovedWindow { get; set; } = true;        // switch to the workspace a window was sent to
        public bool FocusWrap { get; set; } = false;               // directional focus wraps around
        public int ResizeStep { get; set; } = 5;                   // percent of the split changed per press
        public int MinWindowSize { get; set; } = 120;              // px; never tile below this (avoids dead slots)
        public bool CenterSingleWindow { get; set; } = false;
        public int SingleWindowMaxWidthPct { get; set; } = 0;      // 0 = disabled; else 10-100

        // --- Scratchpad ---
        public bool EnableScratchpad { get; set; } = false;

        // --- Debug ---
        public bool TilingDebug { get; set; } = false;

        // --- AutoHotkey bridge ---
        public bool EnableAutoHotkey { get; set; } = true;         // runs bundled AHK v2 for reliable hotkeys
        public string AutoHotkeyPath { get; set; } = "";           // custom AutoHotkey64.exe (empty = bundled)

        // --- Tiling window rules (matched like workspace rules; Action decides what happens) ---
        public List<TilingRule> TilingRules { get; set; } = new();
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
            ["MaximizeWindow"] = "Win+M",
            ["RestoreWindow"] = "Win+Shift+M",
            ["CenterWindow"] = "Win+C",
            ["FullscreenWindow"] = "Win+F",
            ["FocusMode"] = "Win+F1",
            ["Launcher"] = "Alt+Space",
            ["ClipboardManager"] = "Win+V",
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
            ["TilePreselectLeft"] = "Win+Shift+Left",
            ["TilePreselectRight"] = "Win+Shift+Right",
            ["TilePreselectUp"] = "Win+Shift+Up",
            ["TilePreselectDown"] = "Win+Shift+Down",
            ["TileScratchpadToggle"] = "Win+S",
            ["TileScratchpadSend"] = "",
            ["ToggleTiling"] = "Win+Shift+T",
        };
    }

    public class BarConfig
    {
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
