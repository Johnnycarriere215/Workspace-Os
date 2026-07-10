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
    }

    public class GeneralConfig
    {
        public bool StartWithWindows { get; set; } = true;
        public int ActiveWorkspace { get; set; } = 1;
        public bool RestoreWorkspacesOnStart { get; set; } = true;
    }

    public class AppearanceConfig
    {
        public string BarBackground { get; set; } = "#FF000000";
        public string BarForeground { get; set; } = "#FFE0E0E0";
        public string AccentColor { get; set; } = "#FFFFD75F";       // inactive workspace yellow
        public string ActiveWorkspaceBackground { get; set; } = "#FFFFFFFF";
        public string ActiveWorkspaceForeground { get; set; } = "#FF000000";
        public string FontFamily { get; set; } = "Consolas";
        public double FontSize { get; set; } = 13;
        public int BarHeight { get; set; } = 28;
        public string Theme { get; set; } = "Dark";
    }

    public class WorkspaceConfig
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
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
