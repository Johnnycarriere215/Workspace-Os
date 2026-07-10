using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WorkspaceOS.Core.Config;

namespace WorkspaceOS.UI
{
    public partial class SettingsWindow : Window
    {
        private static SettingsWindow _instance;
        private readonly Dictionary<string, TextBox> _appearanceBoxes = new();
        private readonly Dictionary<string, TextBox> _hotkeyBoxes = new();
        private readonly List<(TextBox name, WorkspaceConfig ws)> _workspaceRows = new();
        private readonly List<(WindowRule rule, TextBox match, ComboBox type, TextBox ws, CheckBox enabled)> _ruleRows = new();
        private StackPanel _rulesStack;
        private TextBox _modulesBox, _barRefreshBox, _barHeightBox, _monitorRefreshBox, _presetsBox, _blockedBox, _workspaceCountBox;
        private CheckBox _startupCheck, _secondsCheck, _restoreCheck;

        public static void ShowSettings()
        {
            if (_instance == null || !_instance.IsLoaded) _instance = new SettingsWindow();
            _instance.Show();
            _instance.Activate();
        }

        public SettingsWindow()
        {
            InitializeComponent();
            BuildAppearance();
            BuildWorkspaces();
            BuildRules();
            BuildHotkeys();
            BuildBar();
            BuildFocus();
            BuildAdvanced();
        }

        // ---------- helpers ----------

        private static TextBlock Label(string text, double size = 12) => new()
        {
            Text = text, FontFamily = new FontFamily("Consolas"), FontSize = size,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
        };

        private static TextBlock Section(string text) => new()
        {
            Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x5F)), Margin = new Thickness(0, 14, 0, 6)
        };

        private TextBox Field(Panel parent, string label, string value, double width = 220)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var lb = Label(label);
            lb.Width = 220;
            var tb = new TextBox { Text = value, Width = width, Style = (Style)FindResource("DarkTextBox"), HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(lb);
            row.Children.Add(tb);
            parent.Children.Add(row);
            return tb;
        }

        private CheckBox Check(Panel parent, string label, bool value)
        {
            var cb = new CheckBox
            {
                Content = label, IsChecked = value, Margin = new Thickness(0, 5, 0, 5),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)), FontFamily = new FontFamily("Consolas")
            };
            parent.Children.Add(cb);
            return cb;
        }

        // ---------- Appearance ----------

        private void BuildAppearance()
        {
            var a = App.Configs.Config.Appearance;
            AppearancePanel.Children.Add(Section("COLORS (hex, e.g. #FF000000)"));
            _appearanceBoxes["BarBackground"] = Field(AppearancePanel, "Bar background", a.BarBackground);
            _appearanceBoxes["BarForeground"] = Field(AppearancePanel, "Bar text", a.BarForeground);
            _appearanceBoxes["AccentColor"] = Field(AppearancePanel, "Inactive workspace text", a.AccentColor);
            _appearanceBoxes["ActiveWorkspaceBackground"] = Field(AppearancePanel, "Active workspace background", a.ActiveWorkspaceBackground);
            _appearanceBoxes["ActiveWorkspaceForeground"] = Field(AppearancePanel, "Active workspace text", a.ActiveWorkspaceForeground);
            AppearancePanel.Children.Add(Section("FONT"));
            _appearanceBoxes["FontFamily"] = Field(AppearancePanel, "Font family", a.FontFamily);
            _appearanceBoxes["FontSize"] = Field(AppearancePanel, "Font size", a.FontSize.ToString());
            AppearancePanel.Children.Add(Section("BAR"));
            _barHeightBox = Field(AppearancePanel, "Bar height (px)", a.BarHeight.ToString());
        }

        // ---------- Workspaces ----------

        private void BuildWorkspaces()
        {
            var cfg = App.Configs.Config;
            WorkspacesPanel.Children.Add(Section("WORKSPACES"));
            _workspaceCountBox = Field(WorkspacesPanel, "Number of workspaces (1-9)", cfg.Workspaces.Count.ToString(), 60);
            WorkspacesPanel.Children.Add(Section("NAMES"));
            _workspaceRows.Clear();
            foreach (var ws in cfg.Workspaces.OrderBy(w => w.Index))
            {
                var tb = Field(WorkspacesPanel, $"Workspace {ws.Index}", ws.Name);
                _workspaceRows.Add((tb, ws));
            }
            _restoreCheck = Check(WorkspacesPanel, "Restore window assignments after restart", cfg.General.RestoreWorkspacesOnStart);
        }

        // ---------- Rules ----------

        private void BuildRules()
        {
            var grid = RulesPanel;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = Section("WINDOW RULES — auto-assign applications to workspaces (workspace 0 = all)");
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _rulesStack = new StackPanel();
            scroll.Content = _rulesStack;
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            var addBtn = new Button { Content = "+ Add rule", Style = (Style)FindResource("FlatButton"), Width = 110, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            addBtn.Click += (_, _) =>
            {
                var rule = new WindowRule { Match = "chrome.exe", MatchType = "Executable", Workspace = 2 };
                App.Configs.Config.Rules.Add(rule);
                AddRuleRow(rule);
            };
            Grid.SetRow(addBtn, 2);
            grid.Children.Add(addBtn);

            foreach (var rule in App.Configs.Config.Rules) AddRuleRow(rule);
        }

        private void AddRuleRow(WindowRule rule)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var enabled = new CheckBox { IsChecked = rule.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var match = new TextBox { Text = rule.Match, Width = 240, Style = (Style)FindResource("DarkTextBox"), Margin = new Thickness(0, 0, 8, 0) };
            var type = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 8, 0), FontFamily = new FontFamily("Consolas") };
            foreach (var t in new[] { "Executable", "Process", "Title", "Class", "Regex" }) type.Items.Add(t);
            type.SelectedItem = rule.MatchType;
            var ws = new TextBox { Text = rule.Workspace.ToString(), Width = 40, Style = (Style)FindResource("DarkTextBox"), Margin = new Thickness(0, 0, 8, 0) };
            var del = new Button { Content = "✕", Style = (Style)FindResource("FlatButton"), Width = 30 };
            del.Click += (_, _) =>
            {
                App.Configs.Config.Rules.Remove(rule);
                _ruleRows.RemoveAll(r => r.rule == rule);
                _rulesStack.Children.Remove(row);
            };
            row.Children.Add(enabled);
            row.Children.Add(match);
            row.Children.Add(type);
            row.Children.Add(Label("→ ws"));
            row.Children.Add(ws);
            row.Children.Add(del);
            _rulesStack.Children.Add(row);
            _ruleRows.Add((rule, match, type, ws, enabled));
        }

        // ---------- Hotkeys ----------

        private void BuildHotkeys()
        {
            HotkeysPanel.Children.Add(Section("HOTKEYS — format: Win+Shift+M, Alt+Space, Win+F1. Clear to disable."));
            foreach (var kv in App.Configs.Config.Hotkeys.Bindings.OrderBy(k => k.Key))
                _hotkeyBoxes[kv.Key] = Field(HotkeysPanel, kv.Key, kv.Value);
            if (App.Hotkeys.FailedBindings.Count > 0)
            {
                HotkeysPanel.Children.Add(Section("WARNINGS"));
                foreach (var f in App.Hotkeys.FailedBindings)
                {
                    var t = Label("• " + f);
                    t.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                    t.Margin = new Thickness(0, 2, 0, 2);
                    HotkeysPanel.Children.Add(t);
                }
            }
        }

        // ---------- Bar ----------

        private void BuildBar()
        {
            var bar = App.Configs.Config.Bar;
            BarPanel.Children.Add(Section("MODULES (comma separated, in order)"));
            BarPanel.Children.Add(Label("Available: CPU, RAM, GPU, Disk, NetUp, NetDown, Battery, Volume"));
            _modulesBox = Field(BarPanel, "Modules", string.Join(", ", bar.Modules), 380);
            BarPanel.Children.Add(Section("REFRESH"));
            _barRefreshBox = Field(BarPanel, "Bar refresh (ms)", bar.RefreshMs.ToString(), 80);
            _monitorRefreshBox = Field(BarPanel, "System monitor refresh (ms)", App.Configs.Config.Monitor.RefreshMs.ToString(), 80);
            _secondsCheck = Check(BarPanel, "Show seconds in clock", bar.ShowSeconds);
        }

        // ---------- Focus ----------

        private void BuildFocus()
        {
            var f = App.Configs.Config.Focus;
            FocusPanel.Children.Add(Section("TIMERS"));
            _presetsBox = Field(FocusPanel, "Preset minutes (comma sep.)", string.Join(", ", f.PresetMinutes));
            FocusPanel.Children.Add(Section("BLOCKED APPLICATIONS (process names, comma sep.)"));
            _blockedBox = Field(FocusPanel, "Blocked apps", string.Join(", ", f.BlockedApps), 380);
            FocusPanel.Children.Add(Label("Opening any of these during a focus session fails it immediately."));
        }

        // ---------- Advanced ----------

        private void BuildAdvanced()
        {
            AdvancedPanel.Children.Add(Section("GENERAL"));
            _startupCheck = Check(AdvancedPanel, "Start WorkspaceOS with Windows", App.Configs.Config.General.StartWithWindows);

            AdvancedPanel.Children.Add(Section("CONFIG"));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var exportBtn = new Button { Content = "Export settings…", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 0) };
            exportBtn.Click += (_, _) =>
            {
                var dlg = new SaveFileDialog { FileName = "workspaceos-config.json", Filter = "JSON|*.json" };
                if (dlg.ShowDialog() == true) System.IO.File.WriteAllText(dlg.FileName, App.Configs.Export());
            };
            var importBtn = new Button { Content = "Import settings…", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 0) };
            importBtn.Click += (_, _) =>
            {
                var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
                if (dlg.ShowDialog() == true)
                {
                    if (App.Configs.Import(System.IO.File.ReadAllText(dlg.FileName)))
                    { Close(); ShowSettings(); }
                    else MessageBox.Show("Import failed — invalid file.", "WorkspaceOS");
                }
            };
            var folderBtn = new Button { Content = "Open data folder", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 0) };
            folderBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", ConfigService.DataDir) { UseShellExecute = true });
            var logBtn = new Button { Content = "Open log", Style = (Style)FindResource("FlatButton") };
            logBtn.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(ConfigService.LogPath) { UseShellExecute = true }); } catch { } };
            row.Children.Add(exportBtn); row.Children.Add(importBtn); row.Children.Add(folderBtn); row.Children.Add(logBtn);
            AdvancedPanel.Children.Add(row);

            AdvancedPanel.Children.Add(Section("ABOUT"));
            AdvancedPanel.Children.Add(Label($"WorkspaceOS 1.0.0 — config: {ConfigService.ConfigPath}"));
        }

        // ---------- Save ----------

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var cfg = App.Configs.Config;
            var a = cfg.Appearance;
            a.BarBackground = _appearanceBoxes["BarBackground"].Text.Trim();
            a.BarForeground = _appearanceBoxes["BarForeground"].Text.Trim();
            a.AccentColor = _appearanceBoxes["AccentColor"].Text.Trim();
            a.ActiveWorkspaceBackground = _appearanceBoxes["ActiveWorkspaceBackground"].Text.Trim();
            a.ActiveWorkspaceForeground = _appearanceBoxes["ActiveWorkspaceForeground"].Text.Trim();
            a.FontFamily = _appearanceBoxes["FontFamily"].Text.Trim();
            if (double.TryParse(_appearanceBoxes["FontSize"].Text.Trim(), out double fs) && fs is >= 8 and <= 28) a.FontSize = fs;
            if (int.TryParse(_barHeightBox.Text.Trim(), out int bh) && bh is >= 20 and <= 80) a.BarHeight = bh;

            // workspaces
            if (int.TryParse(_workspaceCountBox.Text.Trim(), out int count) && count is >= 1 and <= 9 && count != cfg.Workspaces.Count)
            {
                while (cfg.Workspaces.Count < count)
                    cfg.Workspaces.Add(new WorkspaceConfig { Index = cfg.Workspaces.Count + 1, Name = (cfg.Workspaces.Count + 1).ToString() });
                while (cfg.Workspaces.Count > count)
                    cfg.Workspaces.RemoveAt(cfg.Workspaces.Count - 1);
            }
            foreach (var (tb, ws) in _workspaceRows)
                ws.Name = tb.Text.Trim();
            cfg.General.RestoreWorkspacesOnStart = _restoreCheck.IsChecked == true;

            // rules
            foreach (var (rule, match, type, ws, enabled) in _ruleRows)
            {
                rule.Match = match.Text.Trim();
                rule.MatchType = type.SelectedItem as string ?? "Executable";
                rule.Enabled = enabled.IsChecked == true;
                if (int.TryParse(ws.Text.Trim(), out int w) && w is >= 0 and <= 9) rule.Workspace = w;
            }

            // hotkeys
            foreach (var kv in _hotkeyBoxes)
                cfg.Hotkeys.Bindings[kv.Key] = kv.Value.Text.Trim();

            // bar
            cfg.Bar.Modules = _modulesBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (int.TryParse(_barRefreshBox.Text.Trim(), out int br) && br >= 250) cfg.Bar.RefreshMs = br;
            if (int.TryParse(_monitorRefreshBox.Text.Trim(), out int mr) && mr >= 250) cfg.Monitor.RefreshMs = mr;
            cfg.Bar.ShowSeconds = _secondsCheck.IsChecked == true;

            // focus
            cfg.Focus.PresetMinutes = _presetsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out int v) ? v : 0).Where(v => v > 0).ToList();
            if (cfg.Focus.PresetMinutes.Count == 0) cfg.Focus.PresetMinutes = new List<int> { 15, 25, 45, 60 };
            cfg.Focus.BlockedApps = _blockedBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            // general
            cfg.General.StartWithWindows = _startupCheck.IsChecked == true;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (cfg.General.StartWithWindows) key?.SetValue("WorkspaceOS", '"' + (Environment.ProcessPath ?? "") + '"');
                else key?.DeleteValue("WorkspaceOS", false);
            }
            catch { }

            App.Configs.NotifyChanged();
            StatusText.Text = "Saved ✓";
        }
    }
}
