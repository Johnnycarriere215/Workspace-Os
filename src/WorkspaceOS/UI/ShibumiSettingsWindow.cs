using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Shibumi;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// The Shibumi configuration page, modelled on the suite's own Configure
    /// surface: every plugin in the catalogue with its role and Windows backend,
    /// a section picker (left / centre / right / hidden), and the bar's geometry
    /// and surface toggles. Changes apply immediately to every monitor.
    /// </summary>
    public sealed class ShibumiSettingsWindow : Window
    {
        private static ShibumiSettingsWindow _instance;
        private ShibumiTokens _tokens;
        private StackPanel _pluginList;
        private DockPanel _root;

        public static void ShowWindow()
        {
            if (_instance == null || !_instance.IsLoaded) _instance = new ShibumiSettingsWindow();
            ((Window)_instance).Show();
            _instance.Activate();
        }

        /// <summary>Dev-only: renders the page to a PNG without showing a window.</summary>
        public static bool Snapshot(string path)
        {
            try
            {
                var w = new ShibumiSettingsWindow();
                double width = Math.Max(560, w.Width - 36);
                double height = Math.Max(480, w.Height - 28);
                w._root.Measure(new Size(width, height));
                w._root.Arrange(new Rect(0, 0, width, height));
                w._root.UpdateLayout();

                int cw = (int)Math.Ceiling(w._root.ActualWidth);
                int ch = (int)Math.Ceiling(w._root.ActualHeight);
                if (cw <= 0 || ch <= 0) return false;

                var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    cw, ch, 96, 96, PixelFormats.Pbgra32);
                target.Render(w._root);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
                using (var stream = System.IO.File.Create(path)) encoder.Save(stream);
                w.Close();
                return true;
            }
            catch (Exception ex)
            {
                ConfigService.Log("Snapshot: Shibumi page render failed (" + ex.Message + ")");
                return false;
            }
        }

        public ShibumiSettingsWindow()
        {
            var a = App.Configs.Config.Appearance;
            _tokens = ShibumiTokens.From(a, App.Configs.Config.Bar.Shibumi);

            Title = "WorkspaceOS — Shibumi shell";
            Width = 760;
            Height = 720;
            MinWidth = 640;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Solid(_tokens.Paper);
            Foreground = Solid(_tokens.Ink);
            FontFamily = UiTheme.ResolveFont(a.FontFamily);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SourceInitialized += (_, _) => UiTheme.ApplyDarkTitleBar(this);

            var root = new DockPanel
            {
                Margin = new Thickness(18, 14, 18, 14),
                Background = Solid(_tokens.Paper)
            };
            _root = root;

            // ---- header -----------------------------------------------------
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var title = new TextBlock
            {
                Text = "SHIBUMI",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Solid(_tokens.Sumi)
            };
            var heading = new TextBlock
            {
                Text = "The shell, on Windows",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Solid(_tokens.Ink),
                Margin = new Thickness(0, 3, 0, 3)
            };
            var sub = new TextBlock
            {
                Text = $"{ShibumiSuite.Count} plugins from the Shibumi-Shell manifests · Windows backends",
                FontSize = 10.5,
                Foreground = Solid(_tokens.Sumi)
            };
            header.Children.Add(title);
            header.Children.Add(heading);
            header.Children.Add(sub);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ---- footer -----------------------------------------------------
            var footer = new DockPanel
            {
                Margin = new Thickness(0, 10, 0, 0),
                // The scroll view runs to the bottom edge; an opaque footer keeps
                // plugin rows from showing through behind its text.
                Background = Solid(_tokens.Paper)
            };
            var status = new TextBlock
            {
                Text = "Changes apply to every monitor immediately.",
                FontSize = 10,
                Foreground = Solid(_tokens.Sumi),
                VerticalAlignment = VerticalAlignment.Center
            };
            footer.Children.Add(status);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(Button("Re-apply wallpaper", () =>
                Core.Themes.ThemeApplication.Apply(App.Configs.Config, forceWallpaper: true)));
            buttons.Children.Add(Button("Settings", SettingsWindow.ShowSettings));
            buttons.Children.Add(Button("Close", Close, primary: true));
            DockPanel.SetDock(buttons, Dock.Right);
            footer.Children.Add(buttons);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            // The scroll view is added after the footer, so it would otherwise
            // paint plugin rows over the footer's buttons.
            Panel.SetZIndex(footer, 1);

            // ---- body -------------------------------------------------------
            var body = new StackPanel();
            body.Children.Add(Section("BAR GEOMETRY"));

            var sh = App.Configs.Config.Bar.Shibumi;
            body.Children.Add(SliderRow("Bar height", 26, 56, sh.BarHeight, value =>
            {
                sh.BarHeight = (int)value;
                Applied();
            }, "px"));
            body.Children.Add(SliderRow("Pill height", 16, 40, sh.PillHeight, value =>
            {
                sh.PillHeight = (int)value;
                Applied();
            }, "px"));
            body.Children.Add(SliderRow("Island radius", 4, 24, sh.IslandRadius, value =>
            {
                sh.IslandRadius = (int)value;
                Applied();
            }, "px"));
            body.Children.Add(SliderRow("Pill radius", 4, 20, sh.PillRadius, value =>
            {
                sh.PillRadius = (int)value;
                Applied();
            }, "px"));
            body.Children.Add(SliderRow("Pill padding", 2, 20, sh.PillPaddingX, value =>
            {
                sh.PillPaddingX = (int)value;
                Applied();
            }, "px"));

            body.Children.Add(Section("SURFACE"));
            var frost = Toggle("Frost — translucent islands", sh.Frost, value => { sh.Frost = value; Applied(); });
            var borders = Toggle("Borders — 1 px pill and island edges", sh.Borders, value => { sh.Borders = value; Applied(); });
            var shadow = Toggle("Panel shadow", sh.Shadow, value => { sh.Shadow = value; Applied(); });
            var dots = Toggle("Workspace markers as dots", string.Equals(sh.WorkspaceMarker, "Dots",
                StringComparison.OrdinalIgnoreCase), value =>
            {
                sh.WorkspaceMarker = value ? "Dots" : "Numbers";
                Applied();
            });
            body.Children.Add(frost);
            body.Children.Add(borders);
            body.Children.Add(shadow);
            body.Children.Add(dots);

            body.Children.Add(Section("PLUGINS — click a section to move or hide it"));
            _pluginList = new StackPanel();
            body.Children.Add(_pluginList);
            BuildPlugins();

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(0, 0, 6, 0),
                Content = body
            };
            root.Children.Add(scroll);
            Content = root;
        }

        private void BuildPlugins()
        {
            _pluginList.Children.Clear();
            var sh = App.Configs.Config.Bar.Shibumi;

            foreach (var plugin in ShibumiSuite.All)
            {
                if (string.IsNullOrWhiteSpace(plugin.Key)) continue;   // services have no widget

                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var role = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(plugin.Role) ? "—" : plugin.Role,
                    FontSize = 9.5,
                    Foreground = Solid(_tokens.Sumi),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(role, 0);
                row.Children.Add(role);

                var text = new StackPanel();
                text.Children.Add(new TextBlock
                {
                    Text = plugin.DisplayName,
                    FontSize = 11.5,
                    Foreground = Solid(plugin.Available ? _tokens.Ink : _tokens.Sumi),
                    FontWeight = plugin.Available ? FontWeights.Normal : FontWeights.Light
                });
                text.Children.Add(new TextBlock
                {
                    Text = plugin.Available
                        ? plugin.Backend
                        : plugin.Backend + " — hidden on this machine",
                    FontSize = 9.5,
                    Foreground = Solid(_tokens.Sumi)
                });
                Grid.SetColumn(text, 1);
                row.Children.Add(text);

                var section = ShibumiSuite.SectionFor(plugin, sh);
                bool hidden = ShibumiSuite.IsHidden(plugin, sh);
                var pill = Button(hidden ? "HIDDEN" : section.ToUpperInvariant(), () =>
                {
                    CycleSection(plugin);
                    BuildPlugins();
                });
                pill.MinWidth = 74;
                Grid.SetColumn(pill, 2);
                row.Children.Add(pill);

                _pluginList.Children.Add(row);
            }
        }

        private void CycleSection(ShibumiPlugin plugin)
        {
            var sh = App.Configs.Config.Bar.Shibumi;
            string[] order = { "left", "center", "right", "hidden" };
            string current = ShibumiSuite.IsHidden(plugin, sh)
                ? "hidden"
                : ShibumiSuite.SectionFor(plugin, sh);
            int index = Array.FindIndex(order, o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase));
            string next = order[(index + 1 + order.Length) % order.Length];

            if (string.Equals(next, "hidden", StringComparison.OrdinalIgnoreCase))
            {
                if (!sh.HiddenWidgets.Contains(plugin.Id, StringComparer.OrdinalIgnoreCase))
                    sh.HiddenWidgets.Add(plugin.Id);
                sh.Sections.Remove(plugin.Id);
            }
            else
            {
                sh.HiddenWidgets.RemoveAll(id => string.Equals(id, plugin.Id, StringComparison.OrdinalIgnoreCase));
                sh.Sections[plugin.Id] = next;
            }

            Applied();
        }

        private void Applied()
        {
            App.Configs.Save();
            App.Bars?.ApplyConfig();
        }

        // ---- small styled controls ------------------------------------------

        private FrameworkElement Section(string text)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 6) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border
            {
                Width = 3,
                Height = 11,
                CornerRadius = new CornerRadius(2),
                Background = Solid(_tokens.Seal),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Solid(_tokens.Sumi),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(row);
            panel.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 7, 0, 2),
                Background = Solid(_tokens.Separator)
            });
            return panel;
        }

        private FrameworkElement SliderRow(string label, double min, double max, double value,
            Action<double> onChange, string suffix)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

            var caption = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Solid(_tokens.Ink),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(caption, 0);
            row.Children.Add(caption);

            var readout = new TextBlock
            {
                Text = $"{value:0}{suffix}",
                FontSize = 10.5,
                Foreground = Solid(_tokens.Seal),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(readout, 2);
            row.Children.Add(readout);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            slider.ValueChanged += (_, e) =>
            {
                readout.Text = $"{e.NewValue:0}{suffix}";
                onChange(e.NewValue);
            };
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            return row;
        }

        private CheckBox Toggle(string label, bool value, Action<bool> onChange)
        {
            var check = new CheckBox
            {
                Content = label,
                IsChecked = value,
                Margin = new Thickness(0, 5, 0, 5),
                Foreground = Solid(_tokens.Ink),
                FontSize = 11
            };
            check.Checked += (_, _) => onChange(true);
            check.Unchecked += (_, _) => onChange(false);
            return check;
        }

        private Border Button(string text, Action onClick, bool primary = false)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                Foreground = primary ? Solid(_tokens.Paper) : Solid(_tokens.Ink),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var background = primary ? _tokens.Seal : ShibumiTokens.WithAlpha(_tokens.Ink, 0.08);
            var button = new Border
            {
                Child = label,
                Background = Solid(background),
                BorderBrush = Solid(primary ? _tokens.Seal : _tokens.Separator),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(_tokens.TileRadius),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand
            };
            button.MouseEnter += (_, _) => button.Background = Solid(primary
                ? _tokens.FillPrimaryHover
                : _tokens.FillHover);
            button.MouseLeave += (_, _) => button.Background = Solid(background);
            button.MouseLeftButtonUp += (_, _) => onClick();
            return button;
        }

        private static SolidColorBrush Solid(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
