using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        private CheckBox _ahkCheck;
        private TextBox _ahkPathBox;
        private TextBlock _ahkStatusText;
        private TextBox _modulesBox, _barRefreshBox, _monitorRefreshBox, _presetsBox, _blockedBox,
                        _workspaceCountBox, _workspaceSlotsBox;
        private Slider _barHeightSlider, _iconWidthSlider;
        private TextBlock _rulesEmpty;
        private CheckBox _startupCheck, _maximizeNewCheck, _hideBarInFullscreenCheck, _hoverFocusCheck, _secondsCheck;
        private ComboBox _iconCombo;
        private TextBox _iconPathBox;
        private CheckBox _showIconCheck;
        private Viewbox _iconPreviewView;
        private Canvas _iconPreviewCanvas;
        private ThemePreset _selectedTheme;
        private TextBlock _themeStatus;
        private TextBlock _photoStatus;
        private CheckBox _matchWallpaperCheck;
        private string _matchedImagePath;
        private readonly List<(Border Card, ThemePreset Theme)> _themeCards = new();
        private static readonly HttpClient Http = new HttpClient();

        public static void ShowSettings()
        {
            if (_instance == null || !_instance.IsLoaded) _instance = new SettingsWindow();
            _instance.Show();
            _instance.Activate();
        }

        public SettingsWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => UiTheme.ApplyDarkTitleBar(this);
            BuildAppearance();
            BuildWorkspaces();
            BuildRules();
            BuildHotkeys();
            BuildBar();
            BuildFocus();
            BuildAdvanced();
        }

        // ---------- helpers ----------

        private static readonly Color Cream = UiTheme.Ink;

        private static TextBlock Label(string text, double size = 12) => new()
        {
            Text = text, FontFamily = UiTheme.Mono, FontSize = size,
            Foreground = new SolidColorBrush(Cream),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
        };

        /// <summary>A group header: cyan tick, quiet uppercase label, hairline rule.</summary>
        private static FrameworkElement Section(string text)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 8) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border
            {
                Width = 3, Height = 11, CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(UiTheme.Accent),
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = text, FontFamily = UiTheme.Mono, FontSize = 10.5, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xCF, 0xC9)),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(row);
            panel.Children.Add(new Border
            {
                Height = 1, Margin = new Thickness(0, 7, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x12, 0x2B, 0x29))
            });
            return panel;
        }

        private static TextBlock FieldLabel(string text) => new()
        {
            Text = text, FontFamily = UiTheme.Mono, FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0xA8, 0xA4)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Width = 220,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        /// <summary>Small grey explanatory line under a control.</summary>
        private void Hint(Panel parent, string text)
        {
            parent.Children.Add(new TextBlock
            {
                Text = text, FontFamily = UiTheme.Mono, FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x7E, 0x7B)),
                TextWrapping = TextWrapping.Wrap, MaxWidth = 660,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 4)
            });
        }

        private Slider SliderField(Panel parent, string label, double min, double max, double value,
            out TextBlock readout)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var readoutBlock = new TextBlock
            {
                Text = $"{value:0}", FontFamily = UiTheme.Mono, FontSize = 11.5,
                Foreground = new SolidColorBrush(UiTheme.Accent),
                Width = 42, TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            readout = readoutBlock;
            var slider = new Slider
            {
                Minimum = min, Maximum = max, Value = value,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Width = 260, Style = (Style)FindResource("BarSlider"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            slider.ValueChanged += (_, _) => readoutBlock.Text = $"{slider.Value:0}";
            row.Children.Add(FieldLabel(label));
            row.Children.Add(readoutBlock);
            row.Children.Add(slider);
            parent.Children.Add(row);
            return slider;
        }

        private TextBox Field(Panel parent, string label, string value, double width = 220)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var tb = new TextBox { Text = value, Width = width, Style = (Style)FindResource("DarkTextBox"), HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(FieldLabel(label));
            row.Children.Add(tb);
            parent.Children.Add(row);
            return tb;
        }

        private CheckBox Check(Panel parent, string label, bool value)
        {
            var cb = new CheckBox
            {
                Content = label, IsChecked = value, Margin = new Thickness(0, 5, 0, 5),
                Foreground = new SolidColorBrush(Cream), FontFamily = UiTheme.Mono
            };
            parent.Children.Add(cb);
            return cb;
        }

        // ---------- Appearance ----------

        private static readonly (string Key, string Text)[] AppearanceColors =
        {
            ("BarBackground", "Bar background"),
            ("BarForeground", "Bar text"),
            ("AccentColor", "Inactive workspace text"),
            ("ActiveWorkspaceBackground", "Active workspace background"),
            ("ActiveWorkspaceForeground", "Active workspace text"),
            ("ModuleLabelColor", "Module label color"),
            ("ModuleBackground", "Module background"),
            ("ModuleBorder", "Module border"),
            ("SeparatorColor", "Separator color")
        };

        private void BuildAppearance()
        {
            var a = App.Configs.Config.Appearance;

            _selectedTheme = ThemeCatalog.Find(a.Theme) ??
                ThemeCatalog.Match(a.BarBackground, a.ActiveWorkspaceBackground);

            AppearancePanel.Children.Add(Section("THEMES — one click fills every colour below, wallpaper included"));
            var gallery = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            foreach (var theme in ThemeCatalog.All) gallery.Children.Add(ThemeCard(theme));
            AppearancePanel.Children.Add(gallery);

            var themeRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            var download = FlatButton("Download 4K wallpaper");
            download.Click += (_, _) => DownloadWallpaper();
            themeRow.Children.Add(download);

            var setDesktop = FlatButton("Set as desktop wallpaper");
            setDesktop.Margin = new Thickness(8, 0, 0, 0);
            setDesktop.Click += (_, _) => SetThemeWallpaper();
            themeRow.Children.Add(setDesktop);

            _matchWallpaperCheck = new CheckBox
            {
                Content = "Match bar colours to the wallpaper",
                IsChecked = true,
                Foreground = new SolidColorBrush(Cream),
                FontFamily = UiTheme.Mono,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };
            themeRow.Children.Add(_matchWallpaperCheck);
            AppearancePanel.Children.Add(themeRow);

            _themeStatus = new TextBlock
            {
                FontFamily = UiTheme.Mono, FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x7E, 0x7B)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 2),
                Text = _selectedTheme == null
                    ? "Pick a theme, then download or set its 3840 × 2160 wallpaper."
                    : $"{_selectedTheme.Name} — press Apply & Save to use it."
            };
            AppearancePanel.Children.Add(_themeStatus);
            UpdateThemeSelection();

            AppearancePanel.Children.Add(Section("PHOTO WALLPAPERS — real 4K photos, free from Unsplash"));
            Hint(AppearancePanel, "These are downloaded from Unsplash at 3840 px wide and can be set as your desktop background. Photos are free to use under the Unsplash License. Save keeps a copy in Pictures\\WorkspaceOS.");
            var photoRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            foreach (var photo in PhotoWallpapers.All) photoRow.Children.Add(PhotoCard(photo));
            AppearancePanel.Children.Add(photoRow);
            _photoStatus = new TextBlock
            {
                FontFamily = UiTheme.Mono, FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x7E, 0x7B)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2),
                Text = "Photos load from images.unsplash.com — an internet connection is needed the first time."
            };
            AppearancePanel.Children.Add(_photoStatus);

            AppearancePanel.Children.Add(Section("THEME FROM YOUR WALLPAPER — recolor the bar to match any image"));
            var matchRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            var useCurrent = FlatButton("Theme from current wallpaper");
            useCurrent.Click += (_, _) => ThemeFromPath(WallpaperTheme.CurrentWallpaperPath());
            matchRow.Children.Add(useCurrent);
            var pick = FlatButton("Choose an image…");
            pick.Margin = new Thickness(8, 0, 0, 0);
            pick.Click += (_, _) =>
            {
                var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
                if (dlg.ShowDialog() == true) ThemeFromPath(dlg.FileName);
            };
            matchRow.Children.Add(pick);
            AppearancePanel.Children.Add(matchRow);
            Hint(AppearancePanel, "Samples the picture, uses its darkest hue for the bar surface and its most vivid hue for the active slot.");

            AppearancePanel.Children.Add(Section("COLORS — click a swatch to pick, or type a hex value"));
            foreach (var (key, text) in AppearanceColors)
                _appearanceBoxes[key] = ColorField(AppearancePanel, text, key, ColorText(key, a));

            AppearancePanel.Children.Add(Section("FONT"));
            _appearanceBoxes["FontFamily"] = Field(AppearancePanel, "Font family", a.FontFamily, 440);
            _appearanceBoxes["FontSize"] = Field(AppearancePanel, "Font size", a.FontSize.ToString());
            AppearancePanel.Children.Add(Section("BAR SIZE"));
            _barHeightSlider = SliderField(AppearancePanel, "Bar height (px)", 20, 80, a.BarHeight, out _);
            Hint(AppearancePanel, "The bar reserves exactly this many pixels at the top of every monitor; maximized windows stop below it.");

            AppearancePanel.Children.Add(Section("BAR ICON — GD-STYLE SHIP AT THE LEFT OF THE BAR"));
            _showIconCheck = Check(AppearancePanel, "Show the icon on the bar", a.ShowBarIcon);
            _showIconCheck.Checked += (_, _) => UpdateIconPreview();
            _showIconCheck.Unchecked += (_, _) => UpdateIconPreview();

            var iconRow = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var iconLabel = Label("Ship");
            iconLabel.Width = 220;
            _iconCombo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var name in BarIconCatalog.Names) _iconCombo.Items.Add(name);
            _iconCombo.SelectedItem = BarIconCatalog.IsKnown(a.BarIcon) ? a.BarIcon : BarIconCatalog.Names[0];
            _iconCombo.SelectionChanged += (_, _) => UpdateIconPreview();
            iconRow.Children.Add(iconLabel);
            iconRow.Children.Add(_iconCombo);
            AppearancePanel.Children.Add(iconRow);

            _iconWidthSlider = SliderField(AppearancePanel, "Icon width (px)", 12, 64, a.BarIconWidth, out _);
            _iconWidthSlider.ValueChanged += (_, _) => UpdateIconPreview();
            _iconPathBox = Field(AppearancePanel, "Custom image", a.BarIconPath, 440);
            _iconPathBox.TextChanged += (_, _) => UpdateIconPreview();

            var pathRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
            var browse = new Button
            {
                Content = "Browse image…", Style = (Style)FindResource("FlatButton"),
                Margin = new Thickness(0, 0, 8, 0)
            };
            browse.Click += (_, _) =>
            {
                var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*" };
                if (dlg.ShowDialog() == true) _iconPathBox.Text = dlg.FileName;
            };
            var clearImage = new Button { Content = "Use built-in ship", Style = (Style)FindResource("FlatButton") };
            clearImage.Click += (_, _) => _iconPathBox.Text = "";
            pathRow.Children.Add(browse);
            pathRow.Children.Add(clearImage);
            AppearancePanel.Children.Add(pathRow);

            var previewRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var previewLabel = Label("Preview");
            previewLabel.Width = 220;
            _iconPreviewView = new Viewbox
            {
                Width = 138, Height = 72,
                Child = _iconPreviewCanvas = new Canvas { Width = 46, Height = 24 }
            };
            RenderOptions.SetBitmapScalingMode(_iconPreviewView, BitmapScalingMode.HighQuality);
            previewRow.Children.Add(previewLabel);
            previewRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x0A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x3A, 0x37)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 7, 12, 7),
                Child = _iconPreviewView
            });
            AppearancePanel.Children.Add(previewRow);

            var hint = Label("Built-in ships are original vector marks. To use a community-shop icon you own, browse to its PNG/JPG — it replaces the ship.");
            hint.TextWrapping = TextWrapping.Wrap;
            hint.MaxWidth = 620;
            hint.HorizontalAlignment = HorizontalAlignment.Left;
            hint.Margin = new Thickness(0, 8, 0, 0);
            hint.FontSize = 11;
            hint.Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x8E, 0x8B));
            AppearancePanel.Children.Add(hint);

            UpdateIconPreview();
        }

        private Button FlatButton(string text) => new Button
        {
            Content = text,
            Style = (Style)FindResource("FlatButton"),
            Padding = new Thickness(12, 5, 12, 5)
        };

        /// <summary>Renders the selected theme and applies it as the desktop wallpaper.</summary>
        private void SetThemeWallpaper()
        {
            // No preset selected means the bar was matched to an image: put that
            // same image back on the desktop instead of switching to a preset.
            if (_selectedTheme == null)
            {
                if (!string.IsNullOrEmpty(_matchedImagePath) && File.Exists(_matchedImagePath))
                {
                    bool restored = WallpaperTheme.Apply(_matchedImagePath);
                    _themeStatus.Text = restored
                        ? $"Desktop wallpaper set → {Path.GetFileName(_matchedImagePath)}"
                        : "Could not set the desktop wallpaper.";
                    return;
                }
                _themeStatus.Text = "Pick a theme first, or match an image below.";
                return;
            }

            var theme = _selectedTheme;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                _themeStatus.Text = $"Rendering {theme.Name} at 3840 × 2160…";
                Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                Directory.CreateDirectory(WallpaperTheme.CacheFolder);
                string path = Path.Combine(WallpaperTheme.CacheFolder, theme.Name + " 4K.png");
                var bitmap = ThemeWallpaper.Render(theme);
                ThemeWallpaper.SavePng(bitmap, path);

                bool applied = WallpaperTheme.Apply(path);
                _themeStatus.Text = applied
                    ? $"Desktop wallpaper set → {theme.Name} 4K"
                    : "Could not set the desktop wallpaper.";

                if (applied && _matchWallpaperCheck?.IsChecked == true)
                    ApplyPalette(WallpaperTheme.Derive(theme), "bar matched to " + theme.Name,
                        clearThemeSelection: false);
            }
            catch (Exception ex)
            {
                _themeStatus.Text = "Could not set wallpaper: " + ex.Message;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>A real Unsplash photo with save / set-as-desktop actions.</summary>
        private Border PhotoCard(PhotoWallpaper photo)
        {
            var thumb = new Image { Height = 84, Stretch = Stretch.UniformToFill };
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(photo.ImageUrl(640), UriKind.Absolute);
                bitmap.DecodePixelWidth = 320;
                bitmap.EndInit();
                thumb.Source = bitmap;
            }
            catch { }

            var stack = new StackPanel();
            stack.Children.Add(thumb);
            stack.Children.Add(new TextBlock
            {
                Text = photo.Title, FontFamily = UiTheme.Mono, FontSize = 11.5,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0),
                Foreground = new SolidColorBrush(UiTheme.Ink),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var actions = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            var save = FlatButton("Save");
            save.Padding = new Thickness(8, 3, 8, 3);
            save.FontSize = 10.5;
            save.Click += async (_, _) => await SavePhotoAsync(photo);
            actions.Children.Add(save);

            var set = FlatButton("Set");
            set.Padding = new Thickness(8, 3, 8, 3);
            set.FontSize = 10.5;
            set.Margin = new Thickness(6, 0, 0, 0);
            set.Click += async (_, _) => await SetPhotoAsync(photo);
            actions.Children.Add(set);
            stack.Children.Add(actions);

            return new Border
            {
                Width = 172, Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(8), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(0x07, 0x0D, 0x0F)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x3A, 0x37)),
                Child = stack,
                ToolTip = $"{photo.Title} — free 4K photo from Unsplash"
            };
        }

        private async Task SavePhotoAsync(PhotoWallpaper photo)
        {
            try
            {
                if (_photoStatus != null) _photoStatus.Text = $"Downloading {photo.Title}…";
                byte[] bytes = await Http.GetByteArrayAsync(photo.ImageUrl());

                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WorkspaceOS");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "Unsplash - " + photo.Title + ".jpg");
                await File.WriteAllBytesAsync(path, bytes);

                if (_photoStatus != null)
                    _photoStatus.Text = $"Saved {photo.Title} ({bytes.Length / 1048576.0:0.0} MB) → {path}";
            }
            catch (Exception ex)
            {
                if (_photoStatus != null) _photoStatus.Text = "Download failed: " + ex.Message;
            }
        }

        private async Task SetPhotoAsync(PhotoWallpaper photo)
        {
            try
            {
                if (_photoStatus != null) _photoStatus.Text = $"Fetching {photo.Title}…";
                byte[] bytes = await Http.GetByteArrayAsync(photo.ImageUrl());

                Directory.CreateDirectory(WallpaperTheme.CacheFolder);
                string path = Path.Combine(WallpaperTheme.CacheFolder, "Unsplash - " + photo.Title + ".jpg");
                await File.WriteAllBytesAsync(path, bytes);

                bool applied = WallpaperTheme.Apply(path);
                if (_photoStatus != null)
                    _photoStatus.Text = applied
                        ? $"Desktop wallpaper set → {photo.Title} (Unsplash)"
                        : "Could not set the desktop wallpaper.";

                if (applied && _matchWallpaperCheck?.IsChecked == true)
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    ApplyPalette(WallpaperTheme.Derive(image), "bar matched to " + photo.Title, path);
                }
            }
            catch (Exception ex)
            {
                if (_photoStatus != null) _photoStatus.Text = "Could not set wallpaper: " + ex.Message;
            }
        }

        /// <summary>Derives a palette from an image file and fills the colour fields.</summary>
        private void ThemeFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                if (_themeStatus != null)
                    _themeStatus.Text = "No wallpaper image found — choose one instead.";
                return;
            }

            try
            {
                string readable = path;
                if (string.IsNullOrEmpty(Path.GetExtension(readable)))
                {
                    // Windows' transcoded wallpaper has no extension; the decoder
                    // sniffs bytes, but a real extension avoids surprises.
                    string temp = Path.Combine(Path.GetTempPath(), "workspaceos-wallpaper.jpg");
                    File.Copy(path, temp, true);
                    readable = temp;
                }

                var image = WallpaperTheme.Load(readable);
                ApplyPalette(WallpaperTheme.Derive(image),
                    "bar matched to " + Path.GetFileName(path), path);
            }
            catch (Exception ex)
            {
                if (_themeStatus != null)
                    _themeStatus.Text = "Could not read that image: " + ex.Message;
            }
        }

        private void ApplyPalette(BarPalette palette, string note, string imagePath = null,
            bool clearThemeSelection = true)
        {
            // A palette taken from a picture is not a preset; only then should the
            // theme highlight clear (set-as-desktop keeps the chosen theme active).
            if (clearThemeSelection) _selectedTheme = null;
            if (imagePath != null) _matchedImagePath = imagePath;
            SetColor("BarBackground", palette.BarBackground);
            SetColor("BarForeground", palette.BarForeground);
            SetColor("AccentColor", palette.InactiveText);
            SetColor("ActiveWorkspaceBackground", palette.ActiveBackground);
            SetColor("ActiveWorkspaceForeground", palette.ActiveForeground);
            SetColor("ModuleLabelColor", palette.ModuleLabel);
            SetColor("ModuleBackground", palette.ModuleBackground);
            SetColor("ModuleBorder", palette.ModuleBorder);
            SetColor("SeparatorColor", palette.Separator);
            UpdateThemeSelection();

            if (_themeStatus != null) _themeStatus.Text = note + " — press Apply & Save to use it.";
        }

        /// <summary>A clickable theme tile: wallpaper thumbnail, name and tagline.</summary>
        private Border ThemeCard(ThemePreset theme)
        {
            var preview = new Image
            {
                Source = ThemeWallpaper.Render(theme, 336, 189),
                Height = 84,
                Stretch = Stretch.UniformToFill
            };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);

            var stack = new StackPanel();
            stack.Children.Add(preview);
            stack.Children.Add(new TextBlock
            {
                Text = theme.Name, FontFamily = UiTheme.Mono, FontSize = 11.5,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 7, 0, 0),
                Foreground = new SolidColorBrush(UiTheme.Ink),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = theme.Tagline, FontFamily = UiTheme.Mono, FontSize = 9.5,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x7E, 0x7B)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var card = new Border
            {
                Width = 172, Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(8), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0x07, 0x0D, 0x0F)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x3A, 0x37)),
                Child = stack,
                ToolTip = $"{theme.Name} — {theme.Tagline}. Click to load these colours."
            };
            card.MouseLeftButtonUp += (_, _) => ApplyTheme(theme);
            _themeCards.Add((card, theme));
            return card;
        }

        private void ApplyTheme(ThemePreset theme)
        {
            if (theme == null) return;
            _selectedTheme = theme;
            _matchedImagePath = null;

            SetColor("BarBackground", theme.BarBackground);
            SetColor("BarForeground", theme.BarForeground);
            SetColor("AccentColor", theme.AccentColor);
            SetColor("ActiveWorkspaceBackground", theme.ActiveWorkspaceBackground);
            SetColor("ActiveWorkspaceForeground", theme.ActiveWorkspaceForeground);
            SetColor("ModuleLabelColor", theme.ModuleLabelColor);
            SetColor("ModuleBackground", theme.ModuleBackground);
            SetColor("ModuleBorder", theme.ModuleBorder);
            SetColor("SeparatorColor", theme.SeparatorColor);

            UpdateThemeSelection();
            if (_themeStatus != null)
                _themeStatus.Text = $"{theme.Name} — press Apply & Save to use it.";
        }

        private void SetColor(string key, string hex)
        {
            if (_appearanceBoxes.TryGetValue(key, out var box)) box.Text = hex;
        }

        private void UpdateThemeSelection()
        {
            var selectedBrush = UiTheme.Brush(UiTheme.Accent);
            var idleBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x3A, 0x37));
            foreach (var (card, theme) in _themeCards)
            {
                bool selected = theme == _selectedTheme;
                card.BorderBrush = selected ? selectedBrush : idleBrush;
                card.BorderThickness = new Thickness(selected ? 2 : 1);
                card.Background = new SolidColorBrush(selected
                    ? Color.FromRgb(0x0B, 0x22, 0x20)
                    : Color.FromRgb(0x07, 0x0D, 0x0F));
            }
        }

        /// <summary>
        /// Renders the selected theme's 3840x2160 wallpaper and writes it where
        /// the user chooses, defaulting to Pictures\WorkspaceOS.
        /// </summary>
        private void DownloadWallpaper()
        {
            var theme = _selectedTheme ?? ThemeCatalog.All[0];
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WorkspaceOS");
            try { Directory.CreateDirectory(folder); } catch { }

            var dialog = new SaveFileDialog
            {
                Title = "Save 4K wallpaper",
                Filter = "PNG image|*.png",
                FileName = theme.Name + " 4K.png",
                InitialDirectory = folder,
                DefaultExt = ".png",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (_themeStatus != null) _themeStatus.Text = "Rendering 3840 × 2160…";
                Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                var bitmap = ThemeWallpaper.Render(theme);
                ThemeWallpaper.SavePng(bitmap, dialog.FileName);

                if (_themeStatus != null)
                    _themeStatus.Text = $"Saved 4K wallpaper → {dialog.FileName}";
            }
            catch (Exception ex)
            {
                if (_themeStatus != null)
                    _themeStatus.Text = "Could not save wallpaper: " + ex.Message;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private static string ColorText(string key, AppearanceConfig a) => key switch
        {
            "BarBackground" => a.BarBackground,
            "BarForeground" => a.BarForeground,
            "AccentColor" => a.AccentColor,
            "ActiveWorkspaceBackground" => a.ActiveWorkspaceBackground,
            "ActiveWorkspaceForeground" => a.ActiveWorkspaceForeground,
            "ModuleLabelColor" => a.ModuleLabelColor,
            "ModuleBackground" => a.ModuleBackground,
            "ModuleBorder" => a.ModuleBorder,
            "SeparatorColor" => a.SeparatorColor,
            _ => "#FF000000"
        };

        /// <summary>A hex box with a live swatch; clicking the swatch opens the picker.</summary>
        private TextBox ColorField(Panel parent, string label, string key, string value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var swatch = new Border
            {
                Width = 30, Height = 22, CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x52, 0x4E)),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Pick a colour"
            };

            var tb = new TextBox
            {
                Text = value, Width = 110, Style = (Style)FindResource("DarkTextBox"),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            void RefreshSwatch()
            {
                swatch.Background = new SolidColorBrush(
                    TryParseHex(tb.Text, out var c) ? c : UiTheme.Surface);
                UpdateIconPreview();
            }

            tb.TextChanged += (_, _) => RefreshSwatch();

            swatch.MouseLeftButtonUp += (_, _) =>
            {
                Color current = TryParseHex(tb.Text, out var c) ? c : UiTheme.Accent;
                using var dlg = new System.Windows.Forms.ColorDialog
                {
                    FullOpen = true,
                    AnyColor = true,
                    Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
                };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var picked = dlg.Color;
                // Keep the existing alpha: translucent surfaces stay translucent.
                tb.Text = $"#{current.A:X2}{picked.R:X2}{picked.G:X2}{picked.B:X2}";
            };

            row.Children.Add(FieldLabel(label));
            row.Children.Add(swatch);
            row.Children.Add(tb);
            parent.Children.Add(row);
            RefreshSwatch();
            return tb;
        }

        private static bool TryParseHex(string text, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                if (ColorConverter.ConvertFromString(text.Trim()) is Color c)
                {
                    color = c;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private Color ParseOr(string text, Color fallback) =>
            TryParseHex(text, out var c) ? c : fallback;

        /// <summary>Re-renders the ship preview when the selection, size or image changes.</summary>
        private void UpdateIconPreview()
        {
            if (_iconPreviewCanvas == null || _iconPreviewView == null) return;

            // Preview a little larger than the bar so shapes read clearly.
            // Fixed 3x preview: at bar size a 46x24 canvas is too small to judge,
            // and the size slider is visible on the bar itself.
            // Faded preview when the icon is switched off: still shows the ship.
            _iconPreviewView.Opacity = _showIconCheck == null || _showIconCheck.IsChecked == true ? 1 : 0.35;

            var name = _iconCombo?.SelectedItem as string ?? BarIconCatalog.Names[0];
            var custom = _iconPathBox?.Text.Trim() ?? "";
            var accent = ParseOr(_appearanceBoxes.TryGetValue("ActiveWorkspaceBackground", out var ab) ? ab.Text : "", UiTheme.Accent);
            var ink = ParseOr(_appearanceBoxes.TryGetValue("BarForeground", out var fb) ? fb.Text : "", UiTheme.Ink);
            var outline = ParseOr(_appearanceBoxes.TryGetValue("ModuleBorder", out var mb) ? mb.Text : "", UiTheme.Line);

            BarIconCatalog.Render(_iconPreviewCanvas, name, custom, accent, ink, outline);
        }

        // ---------- Workspaces ----------

        private void BuildWorkspaces()
        {
            var cfg = App.Configs.Config;
            WorkspacesPanel.Children.Add(Section("WORKSPACES"));
            _workspaceCountBox = Field(WorkspacesPanel, "Number of workspaces (1-9)", cfg.Workspaces.Count.ToString(), 60);
            _workspaceSlotsBox = Field(WorkspacesPanel, "Workspace slots on the bar (1-9)", cfg.Bar.WorkspaceSlots.ToString(), 60);
            WorkspacesPanel.Children.Add(Section("NAMES"));
            _workspaceRows.Clear();
            foreach (var ws in cfg.Workspaces.OrderBy(w => w.Index))
            {
                var tb = Field(WorkspacesPanel, $"Workspace {ws.Index}", ws.Name);
                _workspaceRows.Add((tb, ws));
            }
            Hint(WorkspacesPanel, "Workspaces are Windows' native virtual desktops — they also respond to Ctrl+Win+Arrow and Task View.");
        }

        // ---------- Rules ----------

        private void BuildRules()
        {
            var grid = RulesPanel;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = Section("WINDOW RULES — auto-assign applications to workspaces (workspace 0 = all)");
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _rulesStack = new StackPanel();
            scroll.Content = _rulesStack;
            Grid.SetRow(scroll, 2);
            grid.Children.Add(scroll);

            var addBtn = new Button { Content = "+ Add rule", Style = (Style)FindResource("FlatButton"), Width = 110, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 8) };
            addBtn.Click += (_, _) =>
            {
                var rule = new WindowRule { Match = "chrome.exe", MatchType = "Executable", Workspace = 2 };
                App.Configs.Config.Rules.Add(rule);
                AddRuleRow(rule);
            };
            Grid.SetRow(addBtn, 1);
            grid.Children.Add(addBtn);

            // Empty state, so the tab does not read as a blank card.
            _rulesEmpty = new TextBlock
            {
                Text = "No window rules yet — every app opens on the workspace you are on.",
                FontFamily = UiTheme.Mono, FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x7E, 0x7B)),
                TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0)
            };

            foreach (var rule in App.Configs.Config.Rules) AddRuleRow(rule);
            if (_rulesStack.Children.Count == 0) _rulesStack.Children.Add(_rulesEmpty);
        }

        private void AddRuleRow(WindowRule rule)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var enabled = new CheckBox { IsChecked = rule.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var match = new TextBox { Text = rule.Match, Width = 240, Style = (Style)FindResource("DarkTextBox"), Margin = new Thickness(0, 0, 8, 0) };
            var type = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 8, 0), FontFamily = UiTheme.Mono };
            foreach (var t in new[] { "Executable", "Process", "Title", "Class", "Regex" }) type.Items.Add(t);
            type.SelectedItem = rule.MatchType;
            var ws = new TextBox { Text = rule.Workspace.ToString(), Width = 40, Style = (Style)FindResource("DarkTextBox"), Margin = new Thickness(0, 0, 8, 0) };
            var del = new Button { Content = "✕", Style = (Style)FindResource("FlatButton"), Width = 30 };
            del.Click += (_, _) =>
            {
                App.Configs.Config.Rules.Remove(rule);
                _ruleRows.RemoveAll(r => r.rule == rule);
                _rulesStack.Children.Remove(row);
                if (_rulesEmpty != null && _rulesStack.Children.Count == 0)
                    _rulesStack.Children.Add(_rulesEmpty);
            };
            row.Children.Add(enabled);
            row.Children.Add(match);
            row.Children.Add(type);
            row.Children.Add(Label("→ ws"));
            row.Children.Add(ws);
            row.Children.Add(del);
            if (_rulesEmpty != null) _rulesStack.Children.Remove(_rulesEmpty);
            _rulesStack.Children.Add(row);
            _ruleRows.Add((rule, match, type, ws, enabled));
        }

        private void RefreshAhkStatus()
        {
            if (_ahkStatusText == null) return;
            var ahk = App.Ahk;
            if (ahk == null) { _ahkStatusText.Text = "AutoHotkey: not started (settings not applied yet)"; return; }
            var runtime = ahk.ResolveRuntime();
            _ahkStatusText.Text = ahk.Running
                ? $"AutoHotkey: running ✓  ({runtime})"
                : $"AutoHotkey: not running — fallback C# hook active. Runtime: {(string.IsNullOrEmpty(runtime) ? "not found" : runtime)}";
        }

        // ---------- Hotkeys ----------

        private void BuildHotkeys()
        {
            HotkeysPanel.Children.Add(Section("HOTKEYS — format: Win+Shift+M, Alt+Space, Win+F1. Clear to disable."));
            foreach (var kv in App.Configs.Config.Hotkeys.Bindings.OrderBy(k => k.Key))
            {
                if (HotkeyConfig.IsTilingAction(kv.Key)) continue;
                _hotkeyBoxes[kv.Key] = Field(HotkeysPanel, kv.Key, kv.Value);
            }
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
            _modulesBox = Field(BarPanel, "Modules", string.Join(", ", bar.Modules), 470);
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
            Hint(FocusPanel, "Opening any of these during a focus session fails it immediately.");
        }

        // ---------- Advanced ----------

        private void BuildAdvanced()
        {
            AdvancedPanel.Children.Add(Section("GENERAL"));
            _startupCheck = Check(AdvancedPanel, "Start WorkspaceOS with Windows", App.Configs.Config.General.StartWithWindows);
            _maximizeNewCheck = Check(AdvancedPanel, "Maximize new windows when they open (they stay movable and resizable)", App.Configs.Config.General.MaximizeNewWindows);
            _hideBarInFullscreenCheck = Check(AdvancedPanel, "Hide the bar and release its screen space while a fullscreen game or app is open", App.Configs.Config.General.HideBarInFullscreen);
            _hoverFocusCheck = Check(AdvancedPanel, "Focus the window under the mouse on hover (120 ms; immediate after a workspace switch)", App.Configs.Config.General.FocusWindowOnHover);

            AdvancedPanel.Children.Add(Section("AUTOHOTKEY BRIDGE (Win+1..9 etc. — owns taskbar-conflicting keys)"));
            _ahkCheck = Check(AdvancedPanel, "Run bundled AutoHotkey for global hotkeys", App.Configs.Config.Automation.EnableAutoHotkey);
            _ahkPathBox = Field(AdvancedPanel, "AutoHotkey64 path", App.Configs.Config.Automation.AutoHotkeyPath, 380);
            _ahkStatusText = new TextBlock
            {
                FontFamily = UiTheme.Mono,
                FontSize = 11,
                Foreground = UiTheme.Brush(UiTheme.Green),
                Margin = new Thickness(0, 4, 0, 0)
            };
            AdvancedPanel.Children.Add(_ahkStatusText);
            RefreshAhkStatus();

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
            var version = typeof(SettingsWindow).Assembly.GetName().Version;
            AdvancedPanel.Children.Add(Label($"WorkspaceOS {version?.ToString(3) ?? "4.2.0"} — config: {ConfigService.ConfigPath}"));
        }

        // ---------- Save ----------

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var cfg = App.Configs.Config;
            var a = cfg.Appearance;

            // Invalid hex keeps the previous value instead of breaking the bar.
            void TakeColor(string key, Action<string> set)
            {
                var text = _appearanceBoxes[key].Text.Trim();
                if (TryParseHex(text, out _)) set(text);
            }

            TakeColor("BarBackground", v => a.BarBackground = v);
            TakeColor("BarForeground", v => a.BarForeground = v);
            TakeColor("AccentColor", v => a.AccentColor = v);
            TakeColor("ActiveWorkspaceBackground", v => a.ActiveWorkspaceBackground = v);
            TakeColor("ActiveWorkspaceForeground", v => a.ActiveWorkspaceForeground = v);
            TakeColor("ModuleLabelColor", v => a.ModuleLabelColor = v);
            TakeColor("ModuleBackground", v => a.ModuleBackground = v);
            TakeColor("ModuleBorder", v => a.ModuleBorder = v);
            TakeColor("SeparatorColor", v => a.SeparatorColor = v);
            if (_selectedTheme != null) a.Theme = _selectedTheme.Name;

            a.FontFamily = _appearanceBoxes["FontFamily"].Text.Trim();
            if (double.TryParse(_appearanceBoxes["FontSize"].Text.Trim(), out double fs) && fs is >= 8 and <= 28) a.FontSize = fs;
            a.BarHeight = (int)Math.Round(_barHeightSlider.Value);

            // bar icon
            a.ShowBarIcon = _showIconCheck.IsChecked == true;
            a.BarIcon = _iconCombo.SelectedItem as string ?? BarIconCatalog.Names[0];
            a.BarIconPath = _iconPathBox.Text.Trim();
            a.BarIconWidth = (int)Math.Round(_iconWidthSlider.Value);

            // workspaces
            if (int.TryParse(_workspaceCountBox.Text.Trim(), out int count) && count is >= 1 and <= 9 && count != cfg.Workspaces.Count)
            {
                while (cfg.Workspaces.Count < count)
                    cfg.Workspaces.Add(new WorkspaceConfig { Index = cfg.Workspaces.Count + 1, Name = (cfg.Workspaces.Count + 1).ToString() });
                while (cfg.Workspaces.Count > count)
                    cfg.Workspaces.RemoveAt(cfg.Workspaces.Count - 1);
            }
            if (int.TryParse(_workspaceSlotsBox.Text.Trim(), out int slots) && slots is >= 1 and <= 9)
                cfg.Bar.WorkspaceSlots = slots;
            foreach (var (tb, ws) in _workspaceRows)
                ws.Name = tb.Text.Trim();

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

            // global hotkey automation
            cfg.Automation.EnableAutoHotkey = _ahkCheck.IsChecked == true;
            cfg.Automation.AutoHotkeyPath = _ahkPathBox.Text.Trim();
            foreach (var key in cfg.Hotkeys.Bindings.Keys.Where(HotkeyConfig.IsTilingAction).ToList())
                cfg.Hotkeys.Bindings.Remove(key);

            // general
            cfg.General.StartWithWindows = _startupCheck.IsChecked == true;
            cfg.General.MaximizeNewWindows = _maximizeNewCheck.IsChecked == true;
            cfg.General.HideBarInFullscreen = _hideBarInFullscreenCheck.IsChecked == true;
            cfg.General.FocusWindowOnHover = _hoverFocusCheck.IsChecked == true;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (cfg.General.StartWithWindows) key?.SetValue("WorkspaceOS", '"' + (Environment.ProcessPath ?? "") + '"');
                else key?.DeleteValue("WorkspaceOS", false);
            }
            catch { }

            App.Configs.NotifyChanged();
            RefreshAhkStatus();
            StatusText.Text = "Saved ✓";
        }
    }
}
