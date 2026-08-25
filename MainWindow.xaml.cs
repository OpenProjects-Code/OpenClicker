// MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OpenClicker
{
    public partial class MainWindow : Window
    {
        private bool isRunning = false;
        private CancellationTokenSource? clickCancellation;

        private bool isMouseHeld = false;
        private bool isKeyHeld = false;

        private string heldInputType = "Left";
        private Key heldInputKey = Key.A;

        private bool waitingForHotkey = false;
        private bool waitingForClickKey = false;

        private Key toggleKey = Key.F6;
        private Key clickKey = Key.A;

        private const int HOTKEY_ID = 9000;
        private const uint MOD_NONE = 0x0000;
        private const int WM_HOTKEY = 0x0312;

        private bool hotkeyRegistered = false;
        private HwndSource? hwndSource;

        private long clicksPerformed = 0;
        private long lifetimeClicks = 0;

        private readonly List<string> availableThemes = new();
        private int currentThemeIndex = 0;
        private bool themeMenuOpen = false;

        private bool presetMenuOpen = false;

        private OverlayWindow? overlayWindow;

        private readonly DispatcherTimer statsTimer;

        private bool suppressSave = true;

        private class OpenClickerPreset
        {
            public string Name { get; set; } = "Preset";

            public int Interval { get; set; } = 100;

            public string ClickType { get; set; } = "Left";

            public string ActionType { get; set; } = "Click";

            public string ClickKey { get; set; } = "A";

            public bool ClickLimitEnabled { get; set; } = false;

            public int ClickLimit { get; set; } = 100;
        }

        private class OpenClickerSettings
        {
            public int Interval { get; set; } = 100;

            public bool LeftClick { get; set; } = true;

            public string ToggleKey { get; set; } = "F6";

            public string ClickKey { get; set; } = "A";

            public string ClickType { get; set; } = "Left";

            public string ActionType { get; set; } = "Click";

            public string Theme { get; set; } = "NeonTheme.xaml";

            public bool OverlayEnabled { get; set; } = true;

            public bool ClickLimitEnabled { get; set; } = false;

            public int ClickLimit { get; set; } = 100;

            public long LifetimeClicks { get; set; } = 0;

            public bool AutoUpdateEnabled { get; set; } = true;

            public List<OpenClickerPreset> Presets { get; set; } = new();
        }

        private OpenClickerSettings settings = new();

        private readonly string settingsFolder =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "OpenClicker");

        private string SettingsFile =>
            Path.Combine(
                settingsFolder,
                "settings.json");

        public MainWindow()
        {
            InitializeComponent();

            LoadSettings();

            lifetimeClicks = Math.Max(0, settings.LifetimeClicks);

            HotkeyButton.Content =
                GetKeyDisplayName(toggleKey);

            ClickKeyButton.Content =
                GetKeyDisplayName(clickKey);

            IntervalBox.Text =
                settings.Interval.ToString();

            OverlayCheckBox.IsChecked =
                settings.OverlayEnabled;

            ClickLimitCheckBox.IsChecked =
                settings.ClickLimitEnabled;

            ClickLimitBox.Text =
                settings.ClickLimit.ToString();

            LeftClickRadio.IsChecked =
                settings.ClickType == "Left";

            RightClickRadio.IsChecked =
                settings.ClickType == "Right";

            KeyClickRadio.IsChecked =
                settings.ClickType == "Key";

            ClickActionRadio.IsChecked =
                settings.ActionType == "Click";

            HoldActionRadio.IsChecked =
                settings.ActionType == "Hold";

            LoadThemes();
            BuildPresetMenu();

            ApplyClickTypeUI();
            ApplyActionTypeUI();
            ApplyClickLimitUI();

            SetStoppedStatus();

            LifetimeClicksText.Text =
                FormatCount(lifetimeClicks);

            ProjectedCpsText.Text =
                FormatCps(GetProjectedCps());

            statsTimer =
                new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(250)
                };

            statsTimer.Tick +=
                StatsTimer_Tick;

            statsTimer.Start();

            SourceInitialized +=
                MainWindow_SourceInitialized;

            Loaded +=
                MainWindow_Loaded;

            Closed +=
                MainWindow_Closed;

            suppressSave = false;

            UpdateProjectedCpsUI();
            UpdateLifetimeClicksUI();
            UpdateWindowSize();
        }

        private void MainWindow_Loaded(
            object? sender,
            RoutedEventArgs e)
        {
            TryLaunchUpdater();
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile))
                {
                    settings =
                        new OpenClickerSettings();

                    return;
                }

                string json =
                    File.ReadAllText(SettingsFile);

                OpenClickerSettings? loaded =
                    JsonSerializer.Deserialize<OpenClickerSettings>(
                        json);

                if (loaded == null)
                {
                    settings =
                        new OpenClickerSettings();

                    return;
                }

                settings = loaded;

                settings.Presets ??=
                    new List<OpenClickerPreset>();

                if (settings.Interval < 1)
                    settings.Interval = 100;

                if (settings.ClickLimit < 1)
                    settings.ClickLimit = 100;

                if (settings.LifetimeClicks < 0)
                    settings.LifetimeClicks = 0;

                if (!Enum.TryParse(
                        settings.ToggleKey,
                        true,
                        out Key loadedToggleKey))
                {
                    toggleKey = Key.F6;
                    settings.ToggleKey = "F6";
                }
                else
                {
                    toggleKey = loadedToggleKey;
                }

                if (!Enum.TryParse(
                        settings.ClickKey,
                        true,
                        out Key loadedClickKey))
                {
                    clickKey = Key.A;
                    settings.ClickKey = "A";
                }
                else
                {
                    clickKey = loadedClickKey;
                }

                if (string.IsNullOrWhiteSpace(
                        settings.ClickType))
                {
                    settings.ClickType = "Left";
                }

                if (settings.ClickType != "Left" &&
                    settings.ClickType != "Right" &&
                    settings.ClickType != "Key")
                {
                    settings.ClickType = "Left";
                }

                if (string.IsNullOrWhiteSpace(
                        settings.ActionType))
                {
                    settings.ActionType = "Click";
                }

                if (settings.ActionType != "Click" &&
                    settings.ActionType != "Hold")
                {
                    settings.ActionType = "Click";
                }

                foreach (OpenClickerPreset preset in settings.Presets)
                {
                    if (preset.Interval < 1)
                        preset.Interval = 100;

                    if (preset.ClickLimit < 1)
                        preset.ClickLimit = 100;

                    if (preset.ClickType != "Left" &&
                        preset.ClickType != "Right" &&
                        preset.ClickType != "Key")
                    {
                        preset.ClickType = "Left";
                    }

                    if (preset.ActionType != "Click" &&
                        preset.ActionType != "Hold")
                    {
                        preset.ActionType = "Click";
                    }
                }
            }
            catch
            {
                settings =
                    new OpenClickerSettings();

                toggleKey = Key.F6;
                clickKey = Key.A;
                lifetimeClicks = 0;
            }
        }

        private void SaveSettings()
        {
            if (suppressSave)
                return;

            try
            {
                Directory.CreateDirectory(
                    settingsFolder);

                if (int.TryParse(
                        IntervalBox.Text,
                        out int parsedInterval) &&
                    parsedInterval >= 1)
                {
                    settings.Interval =
                        parsedInterval;
                }

                settings.LeftClick =
                    LeftClickRadio.IsChecked == true;

                settings.ToggleKey =
                    toggleKey.ToString();

                settings.ClickKey =
                    clickKey.ToString();

                settings.ClickType =
                    GetSelectedClickType();

                settings.ActionType =
                    GetSelectedActionType();

                settings.OverlayEnabled =
                    OverlayCheckBox.IsChecked == true;

                settings.ClickLimitEnabled =
                    ClickLimitCheckBox.IsChecked == true;

                if (int.TryParse(
                        ClickLimitBox.Text,
                        out int parsedLimit) &&
                    parsedLimit >= 1)
                {
                    settings.ClickLimit =
                        parsedLimit;
                }

                settings.LifetimeClicks =
                    lifetimeClicks;

                if (availableThemes.Count > 0 &&
                    currentThemeIndex >= 0 &&
                    currentThemeIndex < availableThemes.Count)
                {
                    settings.Theme =
                        GetThemeFileName(
                            availableThemes[currentThemeIndex]);
                }

                string json =
                    JsonSerializer.Serialize(
                        settings,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                File.WriteAllText(
                    SettingsFile,
                    json);
            }
            catch
            {
            }
        }

        private void StatsTimer_Tick(
            object? sender,
            EventArgs e)
        {
            UpdateProjectedCpsUI();
            UpdateLifetimeClicksUI();
        }

        private string GetSelectedClickType()
        {
            if (RightClickRadio.IsChecked == true)
                return "Right";

            if (KeyClickRadio.IsChecked == true)
                return "Key";

            return "Left";
        }

        private string GetSelectedActionType()
        {
            return HoldActionRadio.IsChecked == true
                ? "Hold"
                : "Click";
        }

        private double GetProjectedCps()
        {
            if (ClickActionRadio.IsChecked != true)
                return 0;

            if (!int.TryParse(
                    IntervalBox.Text,
                    out int interval))
            {
                return 0;
            }

            if (interval < 1)
                return 0;

            return 1000.0 / interval;
        }

        private string FormatCps(double cps)
        {
            if (cps <= 0)
                return "0.0";

            if (cps >= 1000)
                return cps.ToString("0");

            return cps.ToString("0.0");
        }

        private string FormatCount(long count)
        {
            if (count < 1_000)
                return count.ToString();

            if (count < 1_000_000)
                return $"{count / 1_000.0:0.#}K";

            if (count < 1_000_000_000)
                return $"{count / 1_000_000.0:0.#}M";

            return $"{count / 1_000_000_000.0:0.##}B";
        }

        private void UpdateProjectedCpsUI()
        {
            if (!IsInitialized)
                return;

            double cps =
                GetProjectedCps();

            ProjectedCpsText.Text =
                FormatCps(cps);

            if (overlayWindow != null)
            {
                overlayWindow.SetStats(
                    cps,
                    lifetimeClicks);
            }
        }

        private void UpdateLifetimeClicksUI()
        {
            if (!IsInitialized)
                return;

            LifetimeClicksText.Text =
                FormatCount(lifetimeClicks);

            if (overlayWindow != null)
            {
                overlayWindow.SetStats(
                    GetProjectedCps(),
                    lifetimeClicks);
            }
        }

        private void LoadThemes()
        {
            availableThemes.Clear();

            Assembly assembly =
                Assembly.GetExecutingAssembly();

            string[] resources =
                assembly.GetManifestResourceNames();

            foreach (string resource in resources)
            {
                if (!resource.EndsWith(
                        ".xaml",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!resource.Contains(
                        ".Themes.",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                availableThemes.Add(resource);
            }

            availableThemes.Sort(
                StringComparer.OrdinalIgnoreCase);

            if (availableThemes.Count == 0)
                return;

            int savedThemeIndex =
                availableThemes.FindIndex(
                    theme =>
                        GetThemeFileName(theme).Equals(
                            settings.Theme,
                            StringComparison.OrdinalIgnoreCase));

            if (savedThemeIndex >= 0)
            {
                currentThemeIndex =
                    savedThemeIndex;
            }
            else
            {
                int neonIndex =
                    availableThemes.FindIndex(
                        theme =>
                            GetThemeFileName(theme).Equals(
                                "NeonTheme.xaml",
                                StringComparison.OrdinalIgnoreCase));

                if (neonIndex >= 0)
                    currentThemeIndex = neonIndex;
            }

            ApplyCurrentTheme();
            BuildThemeMenu();
        }

        private string GetThemeFileName(
            string resourceName)
        {
            int lastDot =
                resourceName.LastIndexOf('.');

            if (lastDot < 0)
                return resourceName;

            int previousDot =
                resourceName.LastIndexOf(
                    '.',
                    lastDot - 1);

            if (previousDot < 0)
                return resourceName;

            return resourceName.Substring(
                previousDot + 1);
        }

        private string GetThemeDisplayName(
            string resourceName)
        {
            string name =
                GetThemeFileName(resourceName);

            if (name.EndsWith(
                    ".xaml",
                    StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^5];
            }

            if (name.Equals(
                    "SecretTheme",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "BLACK & WHITE";
            }

            if (name.EndsWith(
                    "Theme",
                    StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^5];
            }

            return name.ToUpperInvariant();
        }

        private void BuildThemeMenu()
        {
            ThemeListPanel.Children.Clear();

            for (
                int i = 0;
                i < availableThemes.Count;
                i++)
            {
                int themeIndex = i;

                Button themeButton =
                    new Button
                    {
                        Content =
                            GetThemeDisplayName(
                                availableThemes[i]),

                        Style =
                            (Style)FindResource(
                                "ThemeMenuButton")
                    };

                themeButton.Click +=
                    (sender, e) =>
                    {
                        currentThemeIndex =
                            themeIndex;

                        ApplyCurrentTheme();
                        SaveSettings();
                        CloseThemeMenu();
                    };

                ThemeListPanel.Children.Add(
                    themeButton);
            }
        }

        private void ApplyCurrentTheme()
        {
            if (availableThemes.Count == 0)
                return;

            string themeResource =
                availableThemes[currentThemeIndex];

            try
            {
                Application.Current.Resources
                    .MergedDictionaries
                    .Clear();

                string themeFileName =
                    GetThemeFileName(
                        themeResource);

                Uri themeUri =
                    new Uri(
                        $"/OpenClicker;component/Themes/{themeFileName}",
                        UriKind.Relative);

                Application.Current.Resources
                    .MergedDictionaries
                    .Add(
                        new ResourceDictionary
                        {
                            Source = themeUri
                        });

                overlayWindow?.RefreshTheme();
            }
            catch
            {
            }
        }

        private void ThemeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenThemeMenu();
        }

        private void OpenThemeMenu()
        {
            if (themeMenuOpen)
                return;

            if (presetMenuOpen)
                ClosePresetMenuInstant();

            themeMenuOpen = true;

            ThemeBackdrop.Visibility =
                Visibility.Visible;

            ThemePanelTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);

            double panelWidth =
                ThemePanel.ActualWidth;

            if (panelWidth <= 0)
                panelWidth = ThemePanel.Width;

            ThemePanelTransform.X =
                panelWidth + 2;

            DoubleAnimation animation =
                new DoubleAnimation
                {
                    From = panelWidth + 2,
                    To = 0,
                    Duration =
                        TimeSpan.FromMilliseconds(220),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseOut
                        },

                    FillBehavior =
                        FillBehavior.Stop
                };

            animation.Completed +=
                (sender, e) =>
                {
                    ThemePanelTransform.X = 0;
                };

            ThemePanelTransform.BeginAnimation(
                TranslateTransform.XProperty,
                animation);
        }

        private void ThemeBackdrop_Click(
            object sender,
            RoutedEventArgs e)
        {
            CloseThemeMenu();
        }

        private void CloseThemeMenu()
        {
            if (!themeMenuOpen)
                return;

            themeMenuOpen = false;

            ThemePanelTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);

            double panelWidth =
                ThemePanel.ActualWidth;

            if (panelWidth <= 0)
                panelWidth = ThemePanel.Width;

            double currentX =
                ThemePanelTransform.X;

            DoubleAnimation animation =
                new DoubleAnimation
                {
                    From = currentX,
                    To = panelWidth + 2,
                    Duration =
                        TimeSpan.FromMilliseconds(180),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseIn
                        },

                    FillBehavior =
                        FillBehavior.Stop
                };

            animation.Completed +=
                (sender, e) =>
                {
                    ThemePanelTransform.X =
                        panelWidth + 2;

                    ThemeBackdrop.Visibility =
                        Visibility.Collapsed;
                };

            ThemePanelTransform.BeginAnimation(
                TranslateTransform.XProperty,
                animation);
        }

        private void CloseThemeMenuInstant()
        {
            themeMenuOpen = false;

            ThemePanelTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);

            double panelWidth =
                ThemePanel.ActualWidth;

            if (panelWidth <= 0)
                panelWidth = ThemePanel.Width;

            ThemePanelTransform.X =
                panelWidth + 2;

            ThemeBackdrop.Visibility =
                Visibility.Collapsed;
        }

        private void PresetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenPresetMenu();
        }

        private void OpenPresetMenu()
        {
            if (presetMenuOpen)
                return;

            if (themeMenuOpen)
                CloseThemeMenuInstant();

            presetMenuOpen = true;

            BuildPresetMenu();

            PresetBackdrop.Visibility =
                Visibility.Visible;

            PresetPanelTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);

            double panelWidth =
                PresetPanel.ActualWidth;

            if (panelWidth <= 0)
                panelWidth = PresetPanel.Width;

            PresetPanelTransform.X =
                panelWidth + 2;

            DoubleAnimation animation =
                new DoubleAnimation
                {
                    From = panelWidth + 2,
                    To = 0,
                    Duration =
                        TimeSpan.FromMilliseconds(220),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseOut
                        },

                    FillBehavior =
                        FillBehavior.Stop
                };

            animation.Completed +=
                (sender, e) =>
                {
                    PresetPanelTransform.X = 0;
                };

            PresetPanelTransform.BeginAnimation(
                TranslateTransform.XProperty,
                animation);
        }

        private void PresetBackdrop_Click(
            object sender,
            RoutedEventArgs e)
        {
            ClosePresetMenu();
        }

        private void ClosePresetMenu()
        {
            if (!presetMenuOpen)
                return;

            presetMenuOpen = false;

            PresetPanelTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);

            double panelWidth =
                PresetPanel.ActualWidth;

            if (panelWidth <= 0)
                panelWidth = PresetPanel.Width;

            double currentX =
                PresetPanelTransform.X;

            DoubleAnimation animation =
                new DoubleAnimation
                {
                    From = currentX,
                    To = panelWidth + 2,
                    Duration =
                        TimeSpan.FromMilliseconds(180),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseIn
                        },

                    FillBehavior =
                        FillBehavior.Stop
                };

            animation.Completed +=
                (sender, e) =>
                {
                    PresetPanelTransform.X =
                        panelWidth + 2;

                    PresetBackdrop.Visibility =
                        Visibility.Collapsed;
                };

            PresetPanelTransform.BeginAnimation(
                TranslateTransform.XProperty,
                animation);
        }

        private void ClosePresetMenuInstant()
        {
            presetMenuOpen = false;

            PresetPanelTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);

            double panelWidth =
                PresetPanel.ActualWidth;

            if (panelWidth <= 0)
                panelWidth = PresetPanel.Width;

            PresetPanelTransform.X =
                panelWidth + 2;

            PresetBackdrop.Visibility =
                Visibility.Collapsed;
        }

        private void BuildPresetMenu()
        {
            PresetListPanel.Children.Clear();

            if (settings.Presets.Count == 0)
            {
                TextBlock emptyText =
                    new TextBlock
                    {
                        Text = "NO PRESETS YET",
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground =
                            (Brush)FindResource(
                                "SecondaryTextBrush"),

                        HorizontalAlignment =
                            HorizontalAlignment.Center,

                        Margin =
                            new Thickness(0, 20, 0, 0)
                    };

                PresetListPanel.Children.Add(
                    emptyText);

                return;
            }

            foreach (OpenClickerPreset preset in settings.Presets)
            {
                Grid row =
                    new Grid
                    {
                        Margin =
                            new Thickness(0, 4, 0, 4)
                    };

                row.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width = new GridLength(1, GridUnitType.Star)
                    });

                row.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width = new GridLength(38)
                    });

                Button loadButton =
                    new Button
                    {
                        Content = preset.Name,
                        Height = 40,
                        Margin =
                            new Thickness(0, 0, 5, 0),
                        Style =
                            (Style)FindResource(
                                "ThemeMenuButton")
                    };

                loadButton.Click +=
                    (sender, e) =>
                    {
                        ApplyPreset(preset);
                    };

                Button deleteButton =
                    new Button
                    {
                        Content = "×",
                        Height = 40,
                        Style =
                            (Style)FindResource(
                                "ModernButton"),

                        ToolTip = "Delete preset"
                    };

                Grid.SetColumn(
                    deleteButton,
                    1);

                deleteButton.Click +=
                    (sender, e) =>
                    {
                        settings.Presets.Remove(
                            preset);

                        SaveSettings();
                        BuildPresetMenu();
                    };

                row.Children.Add(
                    loadButton);

                row.Children.Add(
                    deleteButton);

                PresetListPanel.Children.Add(
                    row);
            }
        }

        private OpenClickerPreset GetCurrentPreset()
        {
            int interval =
                settings.Interval;

            if (int.TryParse(
                    IntervalBox.Text,
                    out int parsedInterval) &&
                parsedInterval >= 1)
            {
                interval = parsedInterval;
            }

            int limit =
                settings.ClickLimit;

            if (int.TryParse(
                    ClickLimitBox.Text,
                    out int parsedLimit) &&
                parsedLimit >= 1)
            {
                limit = parsedLimit;
            }

            return new OpenClickerPreset
            {
                Name = "Preset",
                Interval = interval,
                ClickType = GetSelectedClickType(),
                ActionType = GetSelectedActionType(),
                ClickKey = clickKey.ToString(),
                ClickLimitEnabled =
                    ClickLimitCheckBox.IsChecked == true,
                ClickLimit = limit
            };
        }

        private void SavePresetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenClickerPreset newPreset =
                GetCurrentPreset();

            string name =
                PresetNameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                int number =
                    settings.Presets.Count + 1;

                name =
                    $"Preset {number}";
            }

            newPreset.Name = name;

            int existingIndex =
                settings.Presets.FindIndex(
                    p =>
                        p.Name.Equals(
                            name,
                            StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                settings.Presets[existingIndex] =
                    newPreset;
            }
            else
            {
                settings.Presets.Add(
                    newPreset);
            }

            PresetNameBox.Clear();

            SaveSettings();
            BuildPresetMenu();
        }

        private void ApplyPreset(
            OpenClickerPreset preset)
        {
            if (isRunning)
                StopClicker();

            suppressSave = true;

            IntervalBox.Text =
                Math.Max(
                    1,
                    preset.Interval).ToString();

            if (preset.ClickType == "Right")
            {
                RightClickRadio.IsChecked = true;
            }
            else if (preset.ClickType == "Key")
            {
                KeyClickRadio.IsChecked = true;
            }
            else
            {
                LeftClickRadio.IsChecked = true;
            }

            if (preset.ActionType == "Hold")
            {
                HoldActionRadio.IsChecked = true;
            }
            else
            {
                ClickActionRadio.IsChecked = true;
            }

            if (Enum.TryParse(
                    preset.ClickKey,
                    true,
                    out Key presetKey))
            {
                clickKey = presetKey;

                ClickKeyButton.Content =
                    GetKeyDisplayName(
                        clickKey);
            }

            ClickLimitCheckBox.IsChecked =
                preset.ClickLimitEnabled;

            ClickLimitBox.Text =
                Math.Max(
                    1,
                    preset.ClickLimit).ToString();

            suppressSave = false;

            ApplyClickTypeUI();
            ApplyActionTypeUI();
            ApplyClickLimitUI();

            SaveSettings();
            UpdateProjectedCpsUI();
            ClosePresetMenu();
        }

        private void TitleBar_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton ==
                MouseButton.Left)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            WindowState =
                WindowState.Minimized;
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private void MainWindow_SourceInitialized(
            object? sender,
            EventArgs e)
        {
            RegisterGlobalHotkey();
        }

        private void RegisterGlobalHotkey()
        {
            if (toggleKey == Key.None)
                return;

            var helper =
                new WindowInteropHelper(this);

            uint virtualKey =
                (uint)KeyInterop.VirtualKeyFromKey(
                    toggleKey);

            if (virtualKey == 0)
                return;

            hotkeyRegistered =
                RegisterHotKey(
                    helper.Handle,
                    HOTKEY_ID,
                    MOD_NONE,
                    virtualKey);

            hwndSource =
                HwndSource.FromHwnd(
                    helper.Handle);

            hwndSource?.AddHook(
                WndProc);
        }

        private void UnregisterGlobalHotkey()
        {
            var helper =
                new WindowInteropHelper(this);

            if (hotkeyRegistered)
            {
                UnregisterHotKey(
                    helper.Handle,
                    HOTKEY_ID);

                hotkeyRegistered = false;
            }

            if (hwndSource != null)
            {
                hwndSource.RemoveHook(
                    WndProc);

                hwndSource = null;
            }
        }

        private IntPtr WndProc(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (msg == WM_HOTKEY &&
                wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleClicker();
                handled = true;
            }

            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            uint fsModifiers,
            uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(
            IntPtr hWnd,
            int id);

        private void StartButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ToggleClicker();
        }

        private void ToggleClicker()
        {
            if (isRunning)
                StopClicker();
            else
                StartClicker();
        }

        private void StartClicker()
        {
            if (!int.TryParse(
                    IntervalBox.Text,
                    out int interval))
            {
                MessageBox.Show(
                    "Please enter a valid delay in milliseconds.");

                return;
            }

            if (interval < 1)
            {
                MessageBox.Show(
                    "The delay must be at least 1 ms.");

                return;
            }

            bool clickMode =
                ClickActionRadio.IsChecked == true;

            bool limitEnabled =
                ClickLimitCheckBox.IsChecked == true &&
                clickMode;

            int limit =
                settings.ClickLimit;

            if (limitEnabled)
            {
                if (!int.TryParse(
                        ClickLimitBox.Text,
                        out limit) ||
                    limit < 1)
                {
                    MessageBox.Show(
                        "Please enter a valid click limit.");

                    return;
                }
            }

            string clickType =
                GetSelectedClickType();

            string actionType =
                GetSelectedActionType();

            Key selectedClickKey =
                clickKey;

            SaveSettings();

            clicksPerformed = 0;

            isRunning = true;

            StartButton.Content =
                "STOP";

            SetRunningStatus();

            ShowOverlay();

            clickCancellation =
                new CancellationTokenSource();

            _ = ClickLoop(
                interval,
                clickType,
                actionType,
                selectedClickKey,
                limitEnabled,
                limit,
                clickCancellation.Token);
        }

        private void StopClicker()
        {
            isRunning = false;

            clickCancellation?.Cancel();
            clickCancellation?.Dispose();

            clickCancellation = null;

            ReleaseHeldInput();

            StartButton.Content =
                "START";

            SetStoppedStatus();

            HideOverlay();

            SaveSettings();
        }

        private void OverlayCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (!IsInitialized ||
                suppressSave)
            {
                return;
            }

            settings.OverlayEnabled =
                OverlayCheckBox.IsChecked == true;

            SaveSettings();

            if (!settings.OverlayEnabled)
            {
                HideOverlay();
            }
            else if (isRunning)
            {
                ShowOverlay();
            }
        }

        private void ShowOverlay()
        {
            if (!settings.OverlayEnabled)
                return;

            if (overlayWindow == null)
            {
                overlayWindow =
                    new OverlayWindow();

                overlayWindow.RefreshTheme();
            }

            overlayWindow.SetToggleKey(
                GetKeyDisplayName(
                    toggleKey));

            overlayWindow.SetStats(
                GetProjectedCps(),
                lifetimeClicks);

            if (!overlayWindow.IsVisible)
                overlayWindow.Show();

            overlayWindow.SetRunning(
                true);
        }

        private void HideOverlay()
        {
            if (overlayWindow != null)
            {
                overlayWindow.SetRunning(
                    false);

                overlayWindow.Hide();
            }
        }

        private void SetStoppedStatus()
        {
            StoppedStatus.Visibility =
                Visibility.Visible;

            RunningStatus.Visibility =
                Visibility.Collapsed;

            RunningTransform1.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            RunningTransform2.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            RunningTransform1.Y = 0;
            RunningTransform2.Y = 0;
        }

        private void SetRunningStatus()
        {
            StoppedStatus.Visibility =
                Visibility.Collapsed;

            RunningStatus.Visibility =
                Visibility.Visible;

            StartStatusAnimation();
        }

        private void StartStatusAnimation()
        {
            RunningTransform1.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            RunningTransform2.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            RunningTransform1.Y = 0;
            RunningTransform2.Y = 0;

            DoubleAnimation bob1 =
                new DoubleAnimation
                {
                    From = 0,
                    To = -4,
                    Duration =
                        TimeSpan.FromMilliseconds(350),

                    AutoReverse = true,

                    RepeatBehavior =
                        RepeatBehavior.Forever
                };

            DoubleAnimation bob2 =
                new DoubleAnimation
                {
                    From = 0,
                    To = -4,
                    Duration =
                        TimeSpan.FromMilliseconds(350),

                    AutoReverse = true,

                    BeginTime =
                        TimeSpan.FromMilliseconds(170),

                    RepeatBehavior =
                        RepeatBehavior.Forever
                };

            RunningTransform1.BeginAnimation(
                TranslateTransform.YProperty,
                bob1);

            RunningTransform2.BeginAnimation(
                TranslateTransform.YProperty,
                bob2);
        }

        private async Task ClickLoop(
            int interval,
            string clickType,
            string actionType,
            Key selectedClickKey,
            bool limitEnabled,
            int limit,
            CancellationToken cancellationToken)
        {
            if (actionType == "Hold")
            {
                HoldInput(
                    clickType,
                    selectedClickKey);

                try
                {
                    await Task.Delay(
                        Timeout.Infinite,
                        cancellationToken);
                }
                catch (TaskCanceledException)
                {
                }
                finally
                {
                    ReleaseHeldInput();
                }

                return;
            }

            while (
                !cancellationToken.IsCancellationRequested)
            {
                PerformClick(
                    clickType,
                    selectedClickKey);

                clicksPerformed++;
                lifetimeClicks++;

                UpdateLifetimeClicksUI();

                if (lifetimeClicks % 1000 == 0)
                {
                    Dispatcher.BeginInvoke(
                        new Action(
                            SaveSettings));
                }

                if (limitEnabled &&
                    clicksPerformed >= limit)
                {
                    Dispatcher.BeginInvoke(
                        new Action(
                            StopClicker));

                    return;
                }

                try
                {
                    await Task.Delay(
                        interval,
                        cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private void PerformClick(
            string clickType,
            Key selectedClickKey)
        {
            if (clickType == "Left")
            {
                MouseClick(
                    MOUSEEVENTF_LEFTDOWN);

                MouseClick(
                    MOUSEEVENTF_LEFTUP);
            }
            else if (clickType == "Right")
            {
                MouseClick(
                    MOUSEEVENTF_RIGHTDOWN);

                MouseClick(
                    MOUSEEVENTF_RIGHTUP);
            }
            else
            {
                PressKey(
                    selectedClickKey);
            }
        }

        private void HoldInput(
            string clickType,
            Key selectedClickKey)
        {
            heldInputType =
                clickType;

            heldInputKey =
                selectedClickKey;

            if (clickType == "Left")
            {
                MouseClick(
                    MOUSEEVENTF_LEFTDOWN);

                isMouseHeld = true;
            }
            else if (clickType == "Right")
            {
                MouseClick(
                    MOUSEEVENTF_RIGHTDOWN);

                isMouseHeld = true;
            }
            else
            {
                PressKeyDown(
                    selectedClickKey);

                isKeyHeld = true;
            }
        }

        private void ReleaseHeldInput()
        {
            if (isMouseHeld)
            {
                if (heldInputType == "Left")
                {
                    MouseClick(
                        MOUSEEVENTF_LEFTUP);
                }
                else if (heldInputType == "Right")
                {
                    MouseClick(
                        MOUSEEVENTF_RIGHTUP);
                }

                isMouseHeld = false;
            }

            if (isKeyHeld)
            {
                PressKeyUp(
                    heldInputKey);

                isKeyHeld = false;
            }
        }

        [DllImport("user32.dll")]
        private static extern void mouse_event(
            uint dwFlags,
            uint dx,
            uint dy,
            uint dwData,
            UIntPtr dwExtraInfo);

        private const uint
            MOUSEEVENTF_LEFTDOWN = 0x0002;

        private const uint
            MOUSEEVENTF_LEFTUP = 0x0004;

        private const uint
            MOUSEEVENTF_RIGHTDOWN = 0x0008;

        private const uint
            MOUSEEVENTF_RIGHTUP = 0x0010;

        private void MouseClick(
            uint button)
        {
            mouse_event(
                button,
                0,
                0,
                0,
                UIntPtr.Zero);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;

            [FieldOffset(0)]
            public KEYBDINPUT ki;

            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        private static extern uint SendInput(
            uint nInputs,
            INPUT[] pInputs,
            int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(
            uint uCode,
            uint uMapType);

        private void PressKey(
            Key key)
        {
            PressKeyDown(key);

            Thread.Sleep(1);

            PressKeyUp(key);
        }

        private void PressKeyDown(
            Key key)
        {
            int virtualKey =
                KeyInterop.VirtualKeyFromKey(
                    key);

            if (virtualKey <= 0)
                return;

            SendKeyboardInput(
                (ushort)virtualKey,
                false);
        }

        private void PressKeyUp(
            Key key)
        {
            int virtualKey =
                KeyInterop.VirtualKeyFromKey(
                    key);

            if (virtualKey <= 0)
                return;

            SendKeyboardInput(
                (ushort)virtualKey,
                true);
        }

        private void SendKeyboardInput(
            ushort virtualKey,
            bool keyUp)
        {
            uint scanCode =
                MapVirtualKey(
                    virtualKey,
                    0);

            if (scanCode == 0)
                return;

            bool extended =
                IsExtendedVirtualKey(
                    virtualKey);

            INPUT input =
                new INPUT
                {
                    type =
                        INPUT_KEYBOARD,

                    U =
                        new InputUnion
                        {
                            ki =
                                new KEYBDINPUT
                                {
                                    wVk = 0,

                                    wScan =
                                        (ushort)scanCode,

                                    dwFlags =
                                        KEYEVENTF_SCANCODE |
                                        (keyUp
                                            ? KEYEVENTF_KEYUP
                                            : 0) |
                                        (extended
                                            ? KEYEVENTF_EXTENDEDKEY
                                            : 0),

                                    time = 0,

                                    dwExtraInfo =
                                        IntPtr.Zero
                                }
                        }
                };

            INPUT[] inputs =
            {
                input
            };

            SendInput(
                1,
                inputs,
                Marshal.SizeOf<INPUT>());
        }

        private bool IsExtendedVirtualKey(
            ushort virtualKey)
        {
            return
                virtualKey == 0x21 ||
                virtualKey == 0x22 ||
                virtualKey == 0x23 ||
                virtualKey == 0x24 ||
                virtualKey == 0x25 ||
                virtualKey == 0x26 ||
                virtualKey == 0x27 ||
                virtualKey == 0x28 ||
                virtualKey == 0x2D ||
                virtualKey == 0x2E ||
                virtualKey == 0x5B ||
                virtualKey == 0x5C ||
                virtualKey == 0x6F ||
                virtualKey == 0x90 ||
                virtualKey == 0xA3 ||
                virtualKey == 0xA5;
        }

        private void ClickType_Checked(
            object sender,
            RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            ApplyClickTypeUI();

            UpdateProjectedCpsUI();

            SaveSettings();
        }

        private void ApplyClickTypeUI()
        {
            if (KeyClickRadio.IsChecked == true)
            {
                ClickKeyLabel.Visibility =
                    Visibility.Visible;

                ClickKeyButton.Visibility =
                    Visibility.Visible;
            }
            else
            {
                ClickKeyLabel.Visibility =
                    Visibility.Collapsed;

                ClickKeyButton.Visibility =
                    Visibility.Collapsed;
            }

            UpdateWindowSize();
        }

        private void ActionType_Checked(
            object sender,
            RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            ApplyActionTypeUI();

            UpdateProjectedCpsUI();

            SaveSettings();
        }

        private void ApplyActionTypeUI()
        {
            if (ClickActionRadio.IsChecked == true)
            {
                IntervalBox.IsEnabled = true;

                IntervalLabel.Opacity =
                    1.0;

                ClickLimitCheckBox.IsEnabled =
                    true;

                ClickLimitBox.IsEnabled =
                    ClickLimitCheckBox.IsChecked == true;
            }
            else
            {
                IntervalBox.IsEnabled =
                    false;

                IntervalLabel.Opacity =
                    0.45;

                ClickLimitCheckBox.IsEnabled =
                    false;

                ClickLimitBox.IsEnabled =
                    false;
            }

            UpdateWindowSize();
        }

        private void ClickLimit_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            ApplyClickLimitUI();

            SaveSettings();
        }

        private void ApplyClickLimitUI()
        {
            ClickLimitBox.IsEnabled =
                ClickLimitCheckBox.IsChecked == true &&
                ClickActionRadio.IsChecked == true;

            UpdateWindowSize();
        }

        private void ClickLimitBox_LostFocus(
            object sender,
            RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void IntervalBox_LostFocus(
            object sender,
            RoutedEventArgs e)
        {
            SaveSettings();
            UpdateProjectedCpsUI();
        }

        private void IntervalBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (!IsInitialized)
                return;

            UpdateProjectedCpsUI();
        }

        private void UpdateWindowSize()
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        UpdateLayout();

                        SizeToContent =
                            SizeToContent.Height;
                    }));
        }

        private string GetKeyDisplayName(
    Key key)
{
    return key switch
    {
        Key.OemOpenBrackets => "[",
        Key.Oem6 => "]",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.Oem1 => ";",
        Key.Oem7 => "'",
        Key.Oem5 => "\\",
        Key.Oem102 => "< >",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemTilde => "`",

        Key.Space => "SPACE",
        Key.Return => "ENTER",
        Key.Back => "BACKSPACE",
        Key.Tab => "TAB",

        Key.LeftShift => "LEFT SHIFT",
        Key.RightShift => "RIGHT SHIFT",

        Key.LeftCtrl => "LEFT CTRL",
        Key.RightCtrl => "RIGHT CTRL",

        Key.LeftAlt => "LEFT ALT",
        Key.RightAlt => "RIGHT ALT",

        Key.CapsLock => "CAPS LOCK",
        Key.NumLock => "NUM LOCK",
        Key.Scroll => "SCROLL LOCK",

        Key.Insert => "INSERT",
        Key.Delete => "DELETE",
        Key.Home => "HOME",
        Key.End => "END",
        Key.PageUp => "PAGE UP",
        Key.PageDown => "PAGE DOWN",

        Key.PrintScreen => "PRINT SCREEN",
        Key.Pause => "PAUSE",

        Key.Up => "↑",
        Key.Down => "↓",
        Key.Left => "←",
        Key.Right => "→",

        Key.LWin => "LEFT WIN",
        Key.RWin => "RIGHT WIN",
        Key.Apps => "MENU",

        _ => key.ToString().ToUpperInvariant()
    };
}

        private void HotkeyButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            waitingForClickKey = false;
            waitingForHotkey = true;

            HotkeyButton.Content =
                "PRESS KEY...";

            HotkeyButton.Focus();
        }

        private void ClickKeyButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            waitingForHotkey = false;
            waitingForClickKey = true;

            ClickKeyButton.Content =
                "PRESS KEY...";

            ClickKeyButton.Focus();
        }

        private void Window_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (!waitingForHotkey &&
                !waitingForClickKey)
            {
                return;
            }

            Key pressedKey =
                e.Key == Key.System
                    ? e.SystemKey
                    : e.Key;

            if (pressedKey == Key.None)
                return;

            if (pressedKey == Key.Escape)
            {
                waitingForHotkey = false;
                waitingForClickKey = false;

                HotkeyButton.Content =
                    GetKeyDisplayName(
                        toggleKey);

                ClickKeyButton.Content =
                    GetKeyDisplayName(
                        clickKey);

                e.Handled = true;

                return;
            }

            if (waitingForHotkey)
            {
                UnregisterGlobalHotkey();

                toggleKey =
                    pressedKey;

                HotkeyButton.Content =
                    GetKeyDisplayName(
                        toggleKey);

                waitingForHotkey = false;

                RegisterGlobalHotkey();

                SaveSettings();

                overlayWindow?.SetToggleKey(
                    GetKeyDisplayName(
                        toggleKey));

                e.Handled = true;

                return;
            }

            if (waitingForClickKey)
            {
                clickKey =
                    pressedKey;

                ClickKeyButton.Content =
                    GetKeyDisplayName(
                        clickKey);

                waitingForClickKey = false;

                SaveSettings();

                e.Handled = true;
            }
        }

        private void TryLaunchUpdater()
        {
            if (!settings.AutoUpdateEnabled)
                return;

            try
            {
                string baseDirectory =
                    AppContext.BaseDirectory;

                string[] updaterNames =
                {
                    "OpenClickerUpdater.exe",
                    "OpenClickerUpdate.exe",
                    "Updater.exe"
                };

                string? updaterPath = null;

                foreach (string name in updaterNames)
                {
                    string candidate =
                        Path.Combine(
                            baseDirectory,
                            name);

                    if (File.Exists(candidate))
                    {
                        updaterPath =
                            candidate;

                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(
                        updaterPath))
                {
                    return;
                }

                string? appPath =
                    Environment.ProcessPath;

                if (string.IsNullOrWhiteSpace(
                        appPath))
                {
                    return;
                }

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName =
                            updaterPath,

                        Arguments =
                            $"--app \"{appPath}\" --silent",

                        WorkingDirectory =
                            baseDirectory,

                        UseShellExecute =
                            true
                    });
            }
            catch
            {
            }
        }

        private void MainWindow_Closed(
            object? sender,
            EventArgs e)
        {
            suppressSave = false;

            StopClicker();

            SaveSettings();

            statsTimer.Stop();

            UnregisterGlobalHotkey();

            if (overlayWindow != null)
            {
                overlayWindow.Close();
                overlayWindow = null;
            }
        }
    }
}