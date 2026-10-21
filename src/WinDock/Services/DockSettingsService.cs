using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinDock.Models;

namespace WinDock.Services;

public sealed class DockSettingsService : IDockSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly IReadOnlyList<DockTheme> PredefinedThemes = new List<DockTheme>
    {
        new()
        {
            Id = "dark-modern",
            Name = "Dark Modern",
            BackgroundColorHex = "#E61A1C23",
            BorderColorHex = "#33FFFFFF",
            AccentColorHex = "#007ACC",
            TextColorHex = "#FFFFFFFF",
            CardBackgroundColorHex = "#262A35",
            BlurRadius = 20.0,
            Opacity = 0.95
        },
        new()
        {
            Id = "oled-black",
            Name = "OLED Pure Black",
            BackgroundColorHex = "#F5050608",
            BorderColorHex = "#22FFFFFF",
            AccentColorHex = "#00B4D8",
            TextColorHex = "#FFFFFFFF",
            CardBackgroundColorHex = "#121318",
            BlurRadius = 15.0,
            Opacity = 0.98
        },
        new()
        {
            Id = "frosted-glass",
            Name = "Frosted Glass",
            BackgroundColorHex = "#B3283142",
            BorderColorHex = "#44E2E8F0",
            AccentColorHex = "#63B3ED",
            TextColorHex = "#FFFFFFFF",
            CardBackgroundColorHex = "#38455A",
            BlurRadius = 30.0,
            Opacity = 0.85
        },
        new()
        {
            Id = "cyberpunk",
            Name = "Cyberpunk Neon",
            BackgroundColorHex = "#F0120C1F",
            BorderColorHex = "#66FF007F",
            AccentColorHex = "#00F5D4",
            TextColorHex = "#FFFFFFFF",
            CardBackgroundColorHex = "#231738",
            BlurRadius = 24.0,
            Opacity = 0.96
        },
        new()
        {
            Id = "midnight-blue",
            Name = "Midnight Blue",
            BackgroundColorHex = "#E60F172A",
            BorderColorHex = "#3338BDF8",
            AccentColorHex = "#38BDF8",
            TextColorHex = "#F8FAFC",
            CardBackgroundColorHex = "#1E293B",
            BlurRadius = 20.0,
            Opacity = 0.95
        },
        new()
        {
            Id = "forest-emerald",
            Name = "Forest Emerald",
            BackgroundColorHex = "#E60E1E17",
            BorderColorHex = "#3334D399",
            AccentColorHex = "#10B981",
            TextColorHex = "#ECFDF5",
            CardBackgroundColorHex = "#162E23",
            BlurRadius = 20.0,
            Opacity = 0.95
        }
    };

    private readonly string _settingsFilePath;
    private readonly object _lock = new();
    private DockSettings _currentSettings;

    public DockSettings CurrentSettings
    {
        get
        {
            lock (_lock)
            {
                return _currentSettings;
            }
        }
        private set
        {
            lock (_lock)
            {
                _currentSettings = value;
            }
        }
    }

    public IReadOnlyList<DockTheme> AvailableThemes => PredefinedThemes;

    public event EventHandler<DockSettings>? SettingsChanged;

    public DockSettingsService(string? customSettingsPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customSettingsPath))
        {
            _settingsFilePath = customSettingsPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _settingsFilePath = Path.Combine(appData, "WinDock", "dock_settings.json");
        }

        _currentSettings = CreateDefaultSettings();
        LoadSettings();
    }

    public void LoadSettings()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<DockSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        EnsureSettingsIntegrity(loaded);
                        _currentSettings = loaded;
                        return;
                    }
                }
            }
            catch
            {
                // Fall back to default settings on read error or corrupted file
            }

            _currentSettings = CreateDefaultSettings();
            SaveSettings();
        }
    }

    public void SaveSettings()
    {
        lock (_lock)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(_currentSettings, JsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch
            {
                // Suppress disk write exceptions in background
            }
        }
    }

    public void UpdateSettings(DockSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_lock)
        {
            EnsureSettingsIntegrity(settings);
            _currentSettings = settings;
            SaveSettings();
        }

        SettingsChanged?.Invoke(this, _currentSettings);
    }

    public void ResetToDefaults()
    {
        lock (_lock)
        {
            _currentSettings = CreateDefaultSettings();
            SaveSettings();
        }

        SettingsChanged?.Invoke(this, _currentSettings);
    }

    public void AddShortcut(DockShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        lock (_lock)
        {
            _currentSettings.PinnedShortcuts.Add(shortcut);
            SaveSettings();
        }

        SettingsChanged?.Invoke(this, _currentSettings);
    }

    public void RemoveShortcut(string shortcutId)
    {
        if (string.IsNullOrWhiteSpace(shortcutId))
        {
            return;
        }

        lock (_lock)
        {
            int removed = _currentSettings.PinnedShortcuts.RemoveAll(s => string.Equals(s.Id, shortcutId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                SaveSettings();
            }
        }

        SettingsChanged?.Invoke(this, _currentSettings);
    }

    public void SetWidgetSize(string widgetName, WidgetSize size)
    {
        if (string.IsNullOrWhiteSpace(widgetName))
        {
            return;
        }

        lock (_lock)
        {
            _currentSettings.WidgetSizes[widgetName] = size;
            SaveSettings();
        }

        SettingsChanged?.Invoke(this, _currentSettings);
    }

    public void SetTheme(DockTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        lock (_lock)
        {
            _currentSettings.Theme = theme;
            SaveSettings();
        }

        SettingsChanged?.Invoke(this, _currentSettings);
    }

    private static DockSettings CreateDefaultSettings()
    {
        var settings = new DockSettings
        {
            Edge = BarEdge.Bottom,
            BarHeightDip = 48.0,
            AutoHideNativeTaskbar = true,
            AutoHideOnFullscreen = true,
            Theme = PredefinedThemes[0],
            SelectedThemeId = "dark-modern",
            SelectedAccentHex = "#007ACC",
            AreDesktopWidgetsVisible = true,
            DockOrderKeys = new List<string>(),
            AreDesktopWidgetsPinned = true,
            EnabledWidgets = new List<string> { "hardware", "media", "steam-jump-in", "discord" },
            WidgetSizes = new Dictionary<string, WidgetSize>(StringComparer.OrdinalIgnoreCase)
            {
                ["hardware"] = WidgetSize.Size1x2,
                ["media"] = WidgetSize.Size1x2,
                ["steam-jump-in"] = WidgetSize.Size1x2,
                ["steam-recent"] = WidgetSize.Size1x3,
                ["steam-downloads"] = WidgetSize.Size1x2,
                ["steam-now-playing"] = WidgetSize.Size1x2,
                ["steam-hub"] = WidgetSize.Size1x1,
                ["steam-storage"] = WidgetSize.Size1x2,
                ["discord"] = WidgetSize.Size1x1,
                ["ai"] = WidgetSize.Size1x1,
                ["clock"] = WidgetSize.Size1x2
            },
            PinnedShortcuts = new List<DockShortcut>
            {
                new()
                {
                    Title = "Explorador de Arquivos",
                    TargetPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\explorer.exe"
                }
            }
        };

        return settings;
    }

    private static void EnsureSettingsIntegrity(DockSettings settings)
    {
        settings.PinnedShortcuts ??= new List<DockShortcut>();
        settings.EnabledWidgets ??= new List<string> { "hardware", "media", "steam-jump-in", "discord" };
        settings.WidgetSizes ??= new Dictionary<string, WidgetSize>(StringComparer.OrdinalIgnoreCase);
        settings.Theme ??= PredefinedThemes[0];
        settings.DockOrderKeys ??= new List<string>();

        if (string.IsNullOrWhiteSpace(settings.SelectedThemeId))
        {
            settings.SelectedThemeId = settings.Theme.Id ?? "dark-modern";
        }
        if (string.IsNullOrWhiteSpace(settings.SelectedAccentHex))
        {
            settings.SelectedAccentHex = settings.Theme.AccentColorHex ?? "#007ACC";
        }

        if (settings.WidgetSizes.Count == 0)
        {
            settings.WidgetSizes["hardware"] = WidgetSize.Size1x2;
            settings.WidgetSizes["media"] = WidgetSize.Size1x2;
            settings.WidgetSizes["steam-jump-in"] = WidgetSize.Size1x2;
            settings.WidgetSizes["steam-recent"] = WidgetSize.Size1x3;
            settings.WidgetSizes["steam-downloads"] = WidgetSize.Size1x2;
            settings.WidgetSizes["steam-now-playing"] = WidgetSize.Size1x2;
            settings.WidgetSizes["steam-hub"] = WidgetSize.Size1x1;
            settings.WidgetSizes["steam-storage"] = WidgetSize.Size1x2;
            settings.WidgetSizes["discord"] = WidgetSize.Size1x1;
            settings.WidgetSizes["ai"] = WidgetSize.Size1x1;
            settings.WidgetSizes["clock"] = WidgetSize.Size1x2;
        }
    }
}
