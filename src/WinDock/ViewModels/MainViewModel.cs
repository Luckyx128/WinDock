global using System.Windows.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinDock.Interop;
using WinDock.Models;
using WinDock.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Application = System.Windows.Application;

namespace WinDock.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public enum DockWidgetSize
{
    Size1x1 = 1,
    Size1x2 = 2,
    Size1x3 = 3
}

public sealed class DockWidgetItem : INotifyPropertyChanged
{
    private bool _isVisible;
    private DockWidgetSize _size;

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string IconGlyph { get; init; }
    public required string Description { get; init; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public DockWidgetSize Size
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TargetWidth));
                OnPropertyChanged(nameof(IsSize1x1));
                OnPropertyChanged(nameof(IsSize1x2));
                OnPropertyChanged(nameof(IsSize1x3));
                OnPropertyChanged(nameof(SizeLabel));
            }
        }
    }

    public double TargetWidth => Size switch
    {
        DockWidgetSize.Size1x1 => 44.0,
        DockWidgetSize.Size1x2 => 98.0,
        DockWidgetSize.Size1x3 => 156.0,
        _ => 44.0
    };

    public bool IsSize1x1 => Size == DockWidgetSize.Size1x1;
    public bool IsSize1x2 => Size == DockWidgetSize.Size1x2;
    public bool IsSize1x3 => Size == DockWidgetSize.Size1x3;
    public string SizeLabel => Size switch
    {
        DockWidgetSize.Size1x1 => "1x1 (44px)",
        DockWidgetSize.Size1x2 => "1x2 (98px)",
        DockWidgetSize.Size1x3 => "1x3 (156px)",
        _ => "1x1"
    };

    public ICommand SetSize1x1Command { get; }
    public ICommand SetSize1x2Command { get; }
    public ICommand SetSize1x3Command { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DockWidgetItem()
    {
        SetSize1x1Command = new RelayCommand(() => Size = DockWidgetSize.Size1x1);
        SetSize1x2Command = new RelayCommand(() => Size = DockWidgetSize.Size1x2);
        SetSize1x3Command = new RelayCommand(() => Size = DockWidgetSize.Size1x3);
    }

    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

public sealed class PinnedShortcutItem : INotifyPropertyChanged
{
    private bool _isPinned = true;
    private bool _isRunning;

    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required string Name { get; init; }
    public required string TargetPath { get; init; }
    public string Arguments { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = "🚀";
    public ImageSource? CustomIcon { get; set; }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand LaunchCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PinnedShortcutItem(Action<PinnedShortcutItem> onLaunch)
    {
        LaunchCommand = new RelayCommand(() => onLaunch(this));
    }

    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

/// <summary>
/// Unified 1x1 Dock App Item combining user-pinned apps and open windows into one item.
/// </summary>
public sealed class DockAppItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string? _executablePath;
    private string _processName = string.Empty;
    private IntPtr _windowHandle = IntPtr.Zero;
    private bool _isOpen;
    private bool _isFocused;
    private bool _isMinimized;
    private bool _isPinned;
    private string? _shortcutId;
    private ImageSource? _icon;

    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    public string? ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (_executablePath != value)
            {
                _executablePath = value;
                OnPropertyChanged();
            }
        }
    }

    public string ProcessName
    {
        get => _processName;
        set
        {
            if (_processName != value)
            {
                _processName = value;
                OnPropertyChanged();
            }
        }
    }

    public IntPtr WindowHandle
    {
        get => _windowHandle;
        set
        {
            if (_windowHandle != value)
            {
                _windowHandle = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen != value)
            {
                _isOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused != value)
            {
                _isFocused = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsMinimized
    {
        get => _isMinimized;
        set
        {
            if (_isMinimized != value)
            {
                _isMinimized = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PinMenuText));
            }
        }
    }

    public string? ShortcutId
    {
        get => _shortcutId;
        set
        {
            if (_shortcutId != value)
            {
                _shortcutId = value;
                OnPropertyChanged();
            }
        }
    }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => Icon != null;

    public string PinMenuText => IsPinned ? "Desafixar da Dock" : "Fixar na Dock";

    public ICommand ClickCommand { get; }
    public ICommand TogglePinCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DockAppItem(Action<DockAppItem> onClick, Action<DockAppItem> onTogglePin)
    {
        ClickCommand = new RelayCommand(() => onClick(this));
        TogglePinCommand = new RelayCommand(() => onTogglePin(this));
    }

    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

/// <summary>
/// Helper item for 1-click pinning of currently open applications in CustomizationWindow.
/// </summary>
public sealed class OpenAppPinItem : INotifyPropertyChanged
{
    private bool _isPinned;

    public required AppWindowInfo WindowInfo { get; init; }
    public required string? ExecutablePath { get; init; }

    public string Title => WindowInfo.Title;
    public string ProcessName => WindowInfo.ProcessName;
    public ImageSource? Icon => WindowInfo.Icon;

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ButtonText));
            }
        }
    }

    public string ButtonText => IsPinned ? "✓ Fixado (Desafixar)" : "+ Fixar na Dock";

    public ICommand TogglePinCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OpenAppPinItem(Action<OpenAppPinItem> onTogglePin)
    {
        TogglePinCommand = new RelayCommand(() => onTogglePin(this));
    }

    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

public sealed class ThemePreset
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Color BarBackgroundColor { get; init; }
    public required Color BarBorderColor { get; init; }
    public required Color PillBackgroundColor { get; init; }
    public required Color PillHoverColor { get; init; }
    public required Color PillActiveColor { get; init; }
    public required Color WindowBackgroundColor { get; init; }
    public required Color WindowCardColor { get; init; }
    public required Color WindowBorderColor { get; init; }
    public required Color DefaultAccent { get; init; }
}

public sealed class AccentColorOption
{
    public required string Name { get; init; }
    public required Color Color { get; init; }
    public SolidColorBrush Brush => new(Color);
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<IntPtr, string?> ExePathCache = new();

    private readonly IAppBarService _appBarService;
    private readonly INativeTaskbarService _nativeTaskbarService;
    private readonly IWindowTrackerService _windowTrackerService;
    private readonly IHardwareMonitorService _hardwareMonitorService;
    private readonly IMediaIntegrationService _mediaIntegrationService;
    private readonly ISteamIntegrationService _steamIntegrationService;
    private readonly IAiUsageService _aiUsageService;
    private readonly IDiscordStatusService _discordStatusService;
    private readonly IDockSettingsService _dockSettingsService;

    private readonly DispatcherTimer _fastTimer;
    private readonly DispatcherTimer _telemetryTimer;
    private bool _isRefreshingTelemetry;
    private bool _isDisposed;

    private IntPtr _excludeWindowHandle;
    private BarEdge _currentEdge = BarEdge.Bottom;
    private bool _areWidgetsVisible = false; // Default clean: widgets OFF on startup
    private bool _isFullscreenActive = false;

    private string _currentTimeText = DateTime.Now.ToString("HH:mm");
    private string _currentSecondsText = DateTime.Now.ToString("ss");
    private string _currentDateText = DateTime.Now.ToString("ddd, dd MMM");
    private string _greetingText = GetGreeting();

    private HardwareMetrics _hardware = new();
    private MediaInfo _currentMedia = new();
    private SteamStatus _steamStatus = new();
    private SteamGameInfo? _selectedSteamGame;
    private AiUsageSnapshot _aiUsage = new();
    private DiscordStatus _discordStatus = new();

    private ThemePreset _currentTheme;
    private AccentColorOption _currentAccent;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? EdgeChanged;
    public event EventHandler? WidgetsVisibilityChanged;
    public event EventHandler<bool>? FullscreenStateChanged;
    public event Action? OpenCustomizationRequested;

    /// <summary>
    /// Unified Dock collection containing both open apps and user-pinned apps as 1x1 (40x40) items.
    /// </summary>
    public ObservableCollection<DockAppItem> DockApps { get; } = new();

    /// <summary>
    /// Open windows list (also bound in CustomizationWindow for 1-click pinning).
    /// </summary>
    public ObservableCollection<AppWindowInfo> OpenWindows { get; } = new();

    /// <summary>
    /// Items exposed for 1-click pinning in CustomizationWindow.
    /// </summary>
    public ObservableCollection<OpenAppPinItem> OpenAppsForPinning { get; } = new();

    public ObservableCollection<PinnedShortcutItem> PinnedShortcuts { get; } = new();
    public ObservableCollection<DockWidgetItem> DockWidgets { get; } = new();
    public ObservableCollection<ThemePreset> AvailableThemes { get; } = new();
    public ObservableCollection<AccentColorOption> AvailableAccents { get; } = new();
    public ObservableCollection<SteamGameInfo> InstalledGames { get; } = new();
    public ObservableCollection<AiProviderUsage> AiProviders { get; } = new();

    public ICommand WindowClickCommand { get; }
    public ICommand DockAppClickCommand { get; }
    public ICommand TogglePinAppCommand { get; }
    public ICommand PinOpenWindowCommand { get; }

    public ICommand PlayPauseCommand { get; }
    public ICommand NextTrackCommand { get; }
    public ICommand PreviousTrackCommand { get; }
    public ICommand OpenYouTubeCommand { get; }
    public ICommand LaunchSteamGameCommand { get; }
    public ICommand LaunchSelectedSteamGameCommand { get; }
    public ICommand OpenSteamCommand { get; }
    public ICommand FocusDiscordCommand { get; }
    public ICommand ToggleNativeTaskbarCommand { get; }
    public ICommand ToggleBarEdgeCommand { get; }
    public ICommand ToggleWidgetsCommand { get; }
    public ICommand RefreshAllCommand { get; }
    public ICommand ExitAppCommand { get; }
    public ICommand OpenCustomizationCommand { get; }

    public ICommand AddCustomShortcutCommand { get; }
    public ICommand RemoveShortcutCommand { get; }
    public ICommand MoveShortcutUpCommand { get; }
    public ICommand MoveShortcutDownCommand { get; }
    public ICommand MoveWidgetUpCommand { get; }
    public ICommand MoveWidgetDownCommand { get; }
    public ICommand ApplyThemeCommand { get; }
    public ICommand ApplyAccentCommand { get; }

    public MainViewModel(
        IAppBarService appBarService,
        INativeTaskbarService nativeTaskbarService,
        IWindowTrackerService windowTrackerService,
        IHardwareMonitorService hardwareMonitorService,
        IMediaIntegrationService mediaIntegrationService,
        ISteamIntegrationService steamIntegrationService,
        IAiUsageService aiUsageService,
        IDiscordStatusService discordStatusService,
        IDockSettingsService? dockSettingsService = null)
    {
        _appBarService = appBarService;
        _nativeTaskbarService = nativeTaskbarService;
        _windowTrackerService = windowTrackerService;
        _hardwareMonitorService = hardwareMonitorService;
        _mediaIntegrationService = mediaIntegrationService;
        _steamIntegrationService = steamIntegrationService;
        _aiUsageService = aiUsageService;
        _discordStatusService = discordStatusService;
        _dockSettingsService = dockSettingsService ?? new DockSettingsService();

        // Base Commands
        WindowClickCommand = new RelayCommand(ExecuteWindowClick);
        DockAppClickCommand = new RelayCommand(param =>
        {
            if (param is DockAppItem item) ExecuteDockAppClick(item);
        });
        TogglePinAppCommand = new RelayCommand(param =>
        {
            if (param is DockAppItem item) TogglePinApp(item);
        });
        PinOpenWindowCommand = new RelayCommand(param =>
        {
            if (param is AppWindowInfo win) PinOpenWindow(win);
            else if (param is OpenAppPinItem pinItem) TogglePinOpenAppItem(pinItem);
        });

        PlayPauseCommand = new RelayCommand(ExecutePlayPause);
        NextTrackCommand = new RelayCommand(ExecuteNextTrack);
        PreviousTrackCommand = new RelayCommand(ExecutePreviousTrack);
        OpenYouTubeCommand = new RelayCommand(ExecuteOpenYouTube);
        LaunchSteamGameCommand = new RelayCommand(ExecuteLaunchSteamGame);
        LaunchSelectedSteamGameCommand = new RelayCommand(ExecuteLaunchSelectedSteamGame);
        OpenSteamCommand = new RelayCommand(() => SafeInvoke(_steamIntegrationService.OpenSteam));
        FocusDiscordCommand = new RelayCommand(() => SafeInvoke(_discordStatusService.FocusOrLaunchDiscord));
        ToggleNativeTaskbarCommand = new RelayCommand(ExecuteToggleNativeTaskbar);
        ToggleBarEdgeCommand = new RelayCommand(ExecuteToggleBarEdge);
        ToggleWidgetsCommand = new RelayCommand(ExecuteToggleWidgets);
        RefreshAllCommand = new RelayCommand(() => _ = RefreshTelemetryAsync());
        ExitAppCommand = new RelayCommand(ExecuteExitApp);
        OpenCustomizationCommand = new RelayCommand(() => OpenCustomizationRequested?.Invoke());

        // Customization Commands
        AddCustomShortcutCommand = new RelayCommand(ExecuteAddCustomShortcut);
        RemoveShortcutCommand = new RelayCommand(ExecuteRemoveShortcut);
        MoveShortcutUpCommand = new RelayCommand(ExecuteMoveShortcutUp);
        MoveShortcutDownCommand = new RelayCommand(ExecuteMoveShortcutDown);
        MoveWidgetUpCommand = new RelayCommand(ExecuteMoveWidgetUp);
        MoveWidgetDownCommand = new RelayCommand(ExecuteMoveWidgetDown);
        ApplyThemeCommand = new RelayCommand(ExecuteApplyTheme);
        ApplyAccentCommand = new RelayCommand(ExecuteApplyAccent);

        // Initialize Themes & Accents
        InitializeThemesAndAccents();
        _currentTheme = AvailableThemes[0];
        _currentAccent = AvailableAccents[0];

        // Clean Dock initialization: no fake emoji shortcuts & all dock widgets IsVisible = false by default!
        InitializeCleanPinnedShortcuts();
        InitializeDockWidgets();

        // Start Fast Timer: exactly 500ms for responsive fullscreen auto-hide and dock app updates
        _fastTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _fastTimer.Tick += (_, _) => RefreshFastState();
        _fastTimer.Start();

        _telemetryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _telemetryTimer.Tick += async (_, _) => await RefreshTelemetryAsync();
        _telemetryTimer.Start();

        RefreshFastState();
        _ = RefreshTelemetryAsync();
    }

    private void InitializeThemesAndAccents()
    {
        AvailableAccents.Add(new AccentColorOption { Name = "Ciano Elétrico", Color = (Color)ColorConverter.ConvertFromString("#38BDF8") });
        AvailableAccents.Add(new AccentColorOption { Name = "Esmeralda Neon", Color = (Color)ColorConverter.ConvertFromString("#10B981") });
        AvailableAccents.Add(new AccentColorOption { Name = "Roxo Cósmico", Color = (Color)ColorConverter.ConvertFromString("#A855F7") });
        AvailableAccents.Add(new AccentColorOption { Name = "Rosa Choque", Color = (Color)ColorConverter.ConvertFromString("#F43F5E") });
        AvailableAccents.Add(new AccentColorOption { Name = "Âmbar Solar", Color = (Color)ColorConverter.ConvertFromString("#F59E0B") });
        AvailableAccents.Add(new AccentColorOption { Name = "Azul Safira", Color = (Color)ColorConverter.ConvertFromString("#3B82F6") });
        AvailableAccents.Add(new AccentColorOption { Name = "Laranja Chama", Color = (Color)ColorConverter.ConvertFromString("#F97316") });

        AvailableThemes.Add(new ThemePreset
        {
            Id = "dark_glass",
            Name = "Dark Glass (Padrão)",
            Description = "Fundo escuro profundo com blur e reflexos sutis",
            BarBackgroundColor = (Color)ColorConverter.ConvertFromString("#111318"),
            BarBorderColor = (Color)ColorConverter.ConvertFromString("#223344"),
            PillBackgroundColor = (Color)ColorConverter.ConvertFromString("#1A1E29"),
            PillHoverColor = (Color)ColorConverter.ConvertFromString("#262F40"),
            PillActiveColor = (Color)ColorConverter.ConvertFromString("#2E3A50"),
            WindowBackgroundColor = (Color)ColorConverter.ConvertFromString("#0E1117"),
            WindowCardColor = (Color)ColorConverter.ConvertFromString("#161B24"),
            WindowBorderColor = (Color)ColorConverter.ConvertFromString("#2B3442"),
            DefaultAccent = (Color)ColorConverter.ConvertFromString("#38BDF8")
        });

        AvailableThemes.Add(new ThemePreset
        {
            Id = "oled_pure",
            Name = "OLED Blackout",
            Description = "Preto absoluto com alto contraste para telas OLED",
            BarBackgroundColor = (Color)ColorConverter.ConvertFromString("#000000"),
            BarBorderColor = (Color)ColorConverter.ConvertFromString("#1A1A1A"),
            PillBackgroundColor = (Color)ColorConverter.ConvertFromString("#0F0F0F"),
            PillHoverColor = (Color)ColorConverter.ConvertFromString("#1E1E1E"),
            PillActiveColor = (Color)ColorConverter.ConvertFromString("#2A2A2A"),
            WindowBackgroundColor = (Color)ColorConverter.ConvertFromString("#050505"),
            WindowCardColor = (Color)ColorConverter.ConvertFromString("#111111"),
            WindowBorderColor = (Color)ColorConverter.ConvertFromString("#222222"),
            DefaultAccent = (Color)ColorConverter.ConvertFromString("#10B981")
        });

        AvailableThemes.Add(new ThemePreset
        {
            Id = "cyberpunk",
            Name = "Cyberpunk Synth",
            Description = "Paleta vibrante com tons de neon, violeta e ciano",
            BarBackgroundColor = (Color)ColorConverter.ConvertFromString("#0F0D1C"),
            BarBorderColor = (Color)ColorConverter.ConvertFromString("#382352"),
            PillBackgroundColor = (Color)ColorConverter.ConvertFromString("#1B172E"),
            PillHoverColor = (Color)ColorConverter.ConvertFromString("#2D264E"),
            PillActiveColor = (Color)ColorConverter.ConvertFromString("#3B3166"),
            WindowBackgroundColor = (Color)ColorConverter.ConvertFromString("#0A0914"),
            WindowCardColor = (Color)ColorConverter.ConvertFromString("#161226"),
            WindowBorderColor = (Color)ColorConverter.ConvertFromString("#3D295C"),
            DefaultAccent = (Color)ColorConverter.ConvertFromString("#F43F5E")
        });

        AvailableThemes.Add(new ThemePreset
        {
            Id = "midnight_blue",
            Name = "Midnight Blue",
            Description = "Azul marinho noturno elegante e sóbrio",
            BarBackgroundColor = (Color)ColorConverter.ConvertFromString("#0B1120"),
            BarBorderColor = (Color)ColorConverter.ConvertFromString("#1E293B"),
            PillBackgroundColor = (Color)ColorConverter.ConvertFromString("#131C31"),
            PillHoverColor = (Color)ColorConverter.ConvertFromString("#1E2D4E"),
            PillActiveColor = (Color)ColorConverter.ConvertFromString("#253860"),
            WindowBackgroundColor = (Color)ColorConverter.ConvertFromString("#070B14"),
            WindowCardColor = (Color)ColorConverter.ConvertFromString("#0F172A"),
            WindowBorderColor = (Color)ColorConverter.ConvertFromString("#1E293B"),
            DefaultAccent = (Color)ColorConverter.ConvertFromString("#3B82F6")
        });
    }

    /// <summary>
    /// Requirement (1): Remove all fake pre-populated emoji shortcuts (Explorer, Google, PowerShell, etc.).
    /// Starts 100% clean with only real user-pinned apps saved in settings.
    /// </summary>
    private void InitializeCleanPinnedShortcuts()
    {
        PinnedShortcuts.Clear();

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string settingsFile = Path.Combine(appData, "WinDock", "dock_settings.json");

        // If dock_settings.json didn't exist yet, clear any pre-populated defaults so dock starts 100% clean
        if (!File.Exists(settingsFile))
        {
            _dockSettingsService.CurrentSettings.PinnedShortcuts.Clear();
            _dockSettingsService.SaveSettings();
        }
        else
        {
            // Clean out any obsolete emoji placeholders if present in existing json
            _dockSettingsService.CurrentSettings.PinnedShortcuts.RemoveAll(s =>
                s.TargetPath.Equals("discord", StringComparison.OrdinalIgnoreCase) ||
                s.TargetPath.Equals("steam", StringComparison.OrdinalIgnoreCase) ||
                s.TargetPath.Contains("google.com", StringComparison.OrdinalIgnoreCase) ||
                s.TargetPath.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
                s.TargetPath.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase));
            _dockSettingsService.SaveSettings();
        }

        SyncPinnedShortcutsFromSettings();
    }

    private void SyncPinnedShortcutsFromSettings()
    {
        PinnedShortcuts.Clear();
        foreach (var shortcut in _dockSettingsService.CurrentSettings.PinnedShortcuts)
        {
            var icon = ExtractIconForPath(shortcut.TargetPath);
            PinnedShortcuts.Add(new PinnedShortcutItem(LaunchPinnedShortcut)
            {
                Id = shortcut.Id,
                Name = shortcut.Title,
                TargetPath = shortcut.TargetPath,
                Arguments = shortcut.Arguments,
                CustomIcon = icon
            });
        }
    }

    /// <summary>
    /// Requirement (1): Set all DockWidgets IsVisible=false by default so the dock starts 100% clean.
    /// </summary>
    private void InitializeDockWidgets()
    {
        DockWidgets.Clear();

        DockWidgets.Add(new DockWidgetItem
        {
            Id = "hardware",
            Title = "Monitor de Hardware",
            IconGlyph = "⚡",
            Description = "Gauges de CPU, GPU e memória RAM em tempo real",
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        DockWidgets.Add(new DockWidgetItem
        {
            Id = "media",
            Title = "Mídia & YouTube",
            IconGlyph = "▶",
            Description = "Player com reprodução ativa, YouTube e controles de faixa",
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        DockWidgets.Add(new DockWidgetItem
        {
            Id = "steam",
            Title = "Steam Game Status",
            IconGlyph = "🎮",
            Description = "Status da Steam e launcher rápido de jogos",
            IsVisible = false,
            Size = DockWidgetSize.Size1x1
        });

        DockWidgets.Add(new DockWidgetItem
        {
            Id = "discord",
            Title = "Discord Presence",
            IconGlyph = "💬",
            Description = "Presença do Discord e atividade em execução",
            IsVisible = false,
            Size = DockWidgetSize.Size1x1
        });

        DockWidgets.Add(new DockWidgetItem
        {
            Id = "ai",
            Title = "Resumo IA",
            IconGlyph = "✨",
            Description = "Contador de requisições de IA e modelos ativos",
            IsVisible = false,
            Size = DockWidgetSize.Size1x1
        });

        DockWidgets.Add(new DockWidgetItem
        {
            Id = "clock",
            Title = "Relógio & Calendário",
            IconGlyph = "🕒",
            Description = "Horário preciso, data e dia da semana",
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });
    }

    public IntPtr ExcludeWindowHandle
    {
        get => _excludeWindowHandle;
        set
        {
            if (_excludeWindowHandle != value)
            {
                _excludeWindowHandle = value;
                OnPropertyChanged();
                RefreshOpenWindowsAndDockApps();
            }
        }
    }

    public BarEdge CurrentEdge
    {
        get => _currentEdge;
        set
        {
            if (_currentEdge != value)
            {
                _currentEdge = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EdgeLabel));
                OnPropertyChanged(nameof(IsAtBottom));
                EdgeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsAtBottom => CurrentEdge == BarEdge.Bottom;
    public string EdgeLabel => CurrentEdge == BarEdge.Top ? "Topo" : "Base";

    /// <summary>
    /// Requirement (1): AreWidgetsVisible = false by default on startup.
    /// </summary>
    public bool AreWidgetsVisible
    {
        get => _areWidgetsVisible;
        set
        {
            if (_areWidgetsVisible != value)
            {
                _areWidgetsVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WidgetsToggleText));
                WidgetsVisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Requirement (3): Active fullscreen detection state.
    /// </summary>
    public bool IsFullscreenActive
    {
        get => _isFullscreenActive;
        private set
        {
            if (_isFullscreenActive != value)
            {
                _isFullscreenActive = value;
                OnPropertyChanged();
                FullscreenStateChanged?.Invoke(this, value);
            }
        }
    }

    public string WidgetsToggleText => AreWidgetsVisible ? "Widgets ON" : "Widgets OFF";
    public bool IsNativeTaskbarHidden => _nativeTaskbarService.IsNativeTaskbarHidden;
    public string NativeTaskbarStatusText => IsNativeTaskbarHidden ? "WinBar Oculta" : "WinBar Visível";

    public string CurrentTimeText
    {
        get => _currentTimeText;
        private set => SetField(ref _currentTimeText, value);
    }

    public string CurrentSecondsText
    {
        get => _currentSecondsText;
        private set => SetField(ref _currentSecondsText, value);
    }

    public string CurrentDateText
    {
        get => _currentDateText;
        private set => SetField(ref _currentDateText, value);
    }

    public string GreetingText
    {
        get => _greetingText;
        private set => SetField(ref _greetingText, value);
    }

    public HardwareMetrics Hardware
    {
        get => _hardware;
        private set
        {
            _hardware = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CpuPercent));
            OnPropertyChanged(nameof(GpuPercent));
            OnPropertyChanged(nameof(RamPercent));
            OnPropertyChanged(nameof(CpuText));
            OnPropertyChanged(nameof(GpuText));
            OnPropertyChanged(nameof(RamText));
            OnPropertyChanged(nameof(RamDetailText));
        }
    }

    public double CpuPercent => Math.Clamp(Hardware.CpuPercent, 0, 100);
    public double GpuPercent => Math.Clamp(Hardware.GpuPercent, 0, 100);
    public double RamPercent => Math.Clamp(Hardware.RamPercent, 0, 100);
    public string CpuText => $"{CpuPercent:0}%";
    public string GpuText => $"{GpuPercent:0}%";
    public string RamText => $"{RamPercent:0}%";
    public string RamDetailText => Hardware.RamTotalGb > 0
        ? $"{Hardware.RamUsedGb:0.0} / {Hardware.RamTotalGb:0.0} GB ({RamPercent:0}%)"
        : $"{RamPercent:0}%";

    public MediaInfo CurrentMedia
    {
        get => _currentMedia;
        private set
        {
            _currentMedia = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActiveMedia));
            OnPropertyChanged(nameof(IsYouTubeActive));
            OnPropertyChanged(nameof(MediaTitle));
            OnPropertyChanged(nameof(MediaSourceBadge));
            OnPropertyChanged(nameof(MediaSubTitle));
        }
    }

    public bool HasActiveMedia => CurrentMedia.HasActiveMedia;
    public bool IsYouTubeActive => CurrentMedia.IsYouTube;
    public string MediaTitle => string.IsNullOrWhiteSpace(CurrentMedia.Title)
        ? "Nenhuma mídia ativa"
        : CurrentMedia.Title;
    public string MediaSourceBadge => CurrentMedia.IsYouTube
        ? "YOUTUBE"
        : (!string.IsNullOrWhiteSpace(CurrentMedia.SourceApp) ? CurrentMedia.SourceApp.ToUpperInvariant() : "MÍDIA");
    public string MediaSubTitle => CurrentMedia.HasActiveMedia
        ? (CurrentMedia.IsYouTube ? "Reproduzindo no YouTube" : $"Fonte: {CurrentMedia.SourceApp}")
        : "Use os controles ou abra o YouTube";

    public SteamStatus SteamStatus
    {
        get => _steamStatus;
        private set
        {
            _steamStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSteamRunning));
            OnPropertyChanged(nameof(IsSteamGameRunning));
            OnPropertyChanged(nameof(SteamHeadlineText));
            OnPropertyChanged(nameof(SteamSubText));
            OnPropertyChanged(nameof(HasInstalledGames));
        }
    }

    public bool IsSteamRunning => SteamStatus.IsRunning;
    public bool IsSteamGameRunning => SteamStatus.RunningAppId > 0 && !string.IsNullOrWhiteSpace(SteamStatus.RunningGameName);
    public bool HasInstalledGames => InstalledGames.Count > 0;
    public string SteamHeadlineText => IsSteamGameRunning
        ? SteamStatus.RunningGameName
        : (SteamStatus.IsRunning ? "Steam Online" : (SteamStatus.IsInstalled ? "Steam Pronto" : "Steam N/D"));
    public string SteamSubText => IsSteamGameRunning
        ? $"Em execução (AppID {SteamStatus.RunningAppId})"
        : $"{InstalledGames.Count} jogo(s) local(is)";

    public SteamGameInfo? SelectedSteamGame
    {
        get => _selectedSteamGame;
        set => SetField(ref _selectedSteamGame, value);
    }

    public AiUsageSnapshot AiUsage
    {
        get => _aiUsage;
        private set
        {
            _aiUsage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalAiInteractionsToday));
            OnPropertyChanged(nameof(AiActiveModelSummary));
            OnPropertyChanged(nameof(AiBadgeText));
            OnPropertyChanged(nameof(HasAiProviders));
        }
    }

    public int TotalAiInteractionsToday => AiUsage.TotalInteractionsToday;
    public string AiActiveModelSummary => string.IsNullOrWhiteSpace(AiUsage.ActiveModelSummary)
        ? "Inativo"
        : AiUsage.ActiveModelSummary;
    public string AiBadgeText => $"IA: {TotalAiInteractionsToday} reqs · {AiActiveModelSummary}";
    public bool HasAiProviders => AiProviders.Count > 0;

    public DiscordStatus DiscordStatus
    {
        get => _discordStatus;
        private set
        {
            _discordStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDiscordRunning));
            OnPropertyChanged(nameof(IsDiscordIpcAvailable));
            OnPropertyChanged(nameof(DiscordActivityText));
            OnPropertyChanged(nameof(DiscordSubText));
        }
    }

    public bool IsDiscordRunning => DiscordStatus.IsRunning;
    public bool IsDiscordIpcAvailable => DiscordStatus.IsIpcAvailable;
    public string DiscordActivityText => string.IsNullOrWhiteSpace(DiscordStatus.ActivitySummary)
        ? (DiscordStatus.IsRunning ? "Discord Ativo" : "Offline")
        : DiscordStatus.ActivitySummary;
    public string DiscordSubText => DiscordStatus.IsIpcAvailable
        ? "Rich Presence IPC conectado"
        : (DiscordStatus.IsRunning ? "Processo ativo em 2º plano" : "Clique para abrir o Discord");

    public ThemePreset CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (_currentTheme != value && value != null)
            {
                _currentTheme = value;
                OnPropertyChanged();
                ApplyTheme(value, _currentAccent?.Color);
            }
        }
    }

    public AccentColorOption CurrentAccent
    {
        get => _currentAccent;
        set
        {
            if (_currentAccent != value && value != null)
            {
                _currentAccent = value;
                OnPropertyChanged();
                ApplyTheme(_currentTheme, value.Color);
            }
        }
    }

    public void ApplyTheme(ThemePreset theme, Color? customAccent = null)
    {
        try
        {
            var res = Application.Current?.Resources;
            if (res == null) return;

            Color accent = customAccent ?? theme.DefaultAccent;

            res["BarBackgroundBrush"] = new SolidColorBrush(theme.BarBackgroundColor);
            res["BarBorderBrush"] = new SolidColorBrush(theme.BarBorderColor);
            res["PillBackgroundBrush"] = new SolidColorBrush(theme.PillBackgroundColor);
            res["PillHoverBrush"] = new SolidColorBrush(theme.PillHoverColor);
            res["PillActiveBrush"] = new SolidColorBrush(theme.PillActiveColor);

            res["WindowBackgroundBrush"] = new SolidColorBrush(theme.WindowBackgroundColor);
            res["WindowCardBrush"] = new SolidColorBrush(theme.WindowCardColor);
            res["WindowCardHoverBrush"] = new SolidColorBrush(theme.PillHoverColor);
            res["WindowBorderBrush"] = new SolidColorBrush(theme.WindowBorderColor);

            res["AccentColorBrush"] = new SolidColorBrush(accent);
            res["AccentCyanBrush"] = new SolidColorBrush(accent);
        }
        catch
        {
            // Ignore resource update issues during shutdown
        }
    }

    private void ExecuteApplyTheme(object? parameter)
    {
        if (parameter is ThemePreset theme)
        {
            CurrentTheme = theme;
        }
    }

    private void ExecuteApplyAccent(object? parameter)
    {
        if (parameter is AccentColorOption accent)
        {
            CurrentAccent = accent;
        }
    }

    /// <summary>
    /// Executes every 500ms:
    /// (1) Checks fullscreen state (excluding WinDock itself)
    /// (2) Updates time and date
    /// (3) Synchronizes OpenWindows and unified DockApps
    /// </summary>
    private void RefreshFastState()
    {
        if (_isDisposed)
        {
            return;
        }

        var now = DateTime.Now;
        CurrentTimeText = now.ToString("HH:mm");
        CurrentSecondsText = now.ToString("ss");
        CurrentDateText = now.ToString("ddd, dd MMM");
        GreetingText = GetGreeting();

        // Requirement (3): Fullscreen auto-hide check
        CheckFullscreenState();

        // Requirement (2): Refresh Open Windows & Unified DockApps
        RefreshOpenWindowsAndDockApps();
    }

    /// <summary>
    /// Requirement (3): In fast timer (every 500ms), check if foreground window is fullscreen.
    /// Exclude WinDock handle so the dock itself is not considered fullscreen.
    /// </summary>
    private void CheckFullscreenState()
    {
        bool isFullscreen = false;
        try
        {
            isFullscreen = _windowTrackerService.IsForegroundWindowFullscreen(_excludeWindowHandle);
        }
        catch
        {
            isFullscreen = false;
        }

        IsFullscreenActive = isFullscreen;
    }

    /// <summary>
    /// Requirement (2): UNIFIED DOCK APPS.
    /// Combines user-pinned apps and open windows into one unified collection of 1x1 (40x40) items.
    /// If a pinned app is open, merges it into its pinned position with its real icon and active focus indicator.
    /// </summary>
    public void RefreshOpenWindowsAndDockApps()
    {
        if (_isDisposed)
        {
            return;
        }

        IReadOnlyList<AppWindowInfo> windows;
        try
        {
            windows = _windowTrackerService.GetOpenWindows(_excludeWindowHandle);
        }
        catch
        {
            return;
        }

        SyncOpenWindows(windows);

        // Build unified target items list
        var pinnedShortcuts = _dockSettingsService.CurrentSettings.PinnedShortcuts;
        var targetItems = new List<DockAppItem>();
        var matchedWindowHandles = new HashSet<IntPtr>();

        // 1. Process all pinned shortcuts in order
        foreach (var pinned in pinnedShortcuts)
        {
            string? pinnedExe = pinned.TargetPath;
            string cleanPinnedName = Path.GetFileNameWithoutExtension(pinnedExe);

            // Check if any open window corresponds to this pinned app
            AppWindowInfo? matchedWindow = null;
            foreach (var win in windows)
            {
                if (matchedWindowHandles.Contains(win.Handle))
                {
                    continue;
                }

                if (IsWindowMatchForShortcut(win, pinned))
                {
                    matchedWindow = win;
                    break;
                }
            }

            if (matchedWindow != null)
            {
                matchedWindowHandles.Add(matchedWindow.Handle);

                var icon = matchedWindow.Icon ?? ExtractIconForPath(pinnedExe);
                var item = new DockAppItem(ExecuteDockAppClick, TogglePinApp)
                {
                    Title = !string.IsNullOrWhiteSpace(pinned.Title) ? pinned.Title : matchedWindow.Title,
                    ExecutablePath = pinnedExe,
                    ProcessName = matchedWindow.ProcessName,
                    WindowHandle = matchedWindow.Handle,
                    IsOpen = true,
                    IsFocused = matchedWindow.IsFocused,
                    IsMinimized = matchedWindow.IsMinimized,
                    IsPinned = true,
                    ShortcutId = pinned.Id,
                    Icon = icon
                };
                targetItems.Add(item);
            }
            else
            {
                // Pinned app is currently closed
                var icon = ExtractIconForPath(pinnedExe);
                var item = new DockAppItem(ExecuteDockAppClick, TogglePinApp)
                {
                    Title = pinned.Title,
                    ExecutablePath = pinnedExe,
                    ProcessName = cleanPinnedName,
                    WindowHandle = IntPtr.Zero,
                    IsOpen = false,
                    IsFocused = false,
                    IsMinimized = false,
                    IsPinned = true,
                    ShortcutId = pinned.Id,
                    Icon = icon
                };
                targetItems.Add(item);
            }
        }

        // 2. Process all open windows that were not already merged with a pinned app
        foreach (var win in windows)
        {
            if (matchedWindowHandles.Contains(win.Handle))
            {
                continue;
            }

            string? exePath = !string.IsNullOrWhiteSpace(win.ExecutablePath) ? win.ExecutablePath : TryGetExecutablePath(win.Handle);
            var icon = win.Icon ?? (exePath != null ? ExtractIconForPath(exePath) : null);

            var item = new DockAppItem(ExecuteDockAppClick, TogglePinApp)
            {
                Title = win.Title,
                ExecutablePath = exePath,
                ProcessName = win.ProcessName,
                WindowHandle = win.Handle,
                IsOpen = true,
                IsFocused = win.IsFocused,
                IsMinimized = win.IsMinimized,
                IsPinned = false,
                ShortcutId = null,
                Icon = icon
            };
            targetItems.Add(item);
        }

        // Synchronize into DockApps collection in-place to avoid visual flickering
        SyncDockApps(targetItems);

        // Also update OpenAppsForPinning (for CustomizationWindow 1-click pinning)
        SyncOpenAppsForPinning(windows, pinnedShortcuts);
    }

    private static bool IsWindowMatchForShortcut(AppWindowInfo win, DockShortcut shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut.TargetPath))
        {
            return false;
        }

        string cleanShortcutName = Path.GetFileNameWithoutExtension(shortcut.TargetPath);
        if (cleanShortcutName.Equals(win.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (shortcut.TargetPath.EndsWith(win.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? winExe = TryGetExecutablePath(win.Handle);
        if (!string.IsNullOrWhiteSpace(winExe) && string.Equals(winExe, shortcut.TargetPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private void SyncDockApps(List<DockAppItem> target)
    {
        for (int i = 0; i < target.Count; i++)
        {
            var newItem = target[i];
            if (i < DockApps.Count)
            {
                var existing = DockApps[i];
                if (CanUpdateInPlace(existing, newItem))
                {
                    existing.Title = newItem.Title;
                    existing.ExecutablePath = newItem.ExecutablePath;
                    existing.ProcessName = newItem.ProcessName;
                    existing.WindowHandle = newItem.WindowHandle;
                    existing.IsOpen = newItem.IsOpen;
                    existing.IsFocused = newItem.IsFocused;
                    existing.IsMinimized = newItem.IsMinimized;
                    existing.IsPinned = newItem.IsPinned;
                    existing.ShortcutId = newItem.ShortcutId;
                    if (newItem.Icon != null)
                    {
                        existing.Icon = newItem.Icon;
                    }
                }
                else
                {
                    DockApps[i] = newItem;
                }
            }
            else
            {
                DockApps.Add(newItem);
            }
        }

        while (DockApps.Count > target.Count)
        {
            DockApps.RemoveAt(DockApps.Count - 1);
        }
    }

    private static bool CanUpdateInPlace(DockAppItem existing, DockAppItem target)
    {
        if (existing.IsPinned && target.IsPinned && string.Equals(existing.ShortcutId, target.ShortcutId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (existing.WindowHandle != IntPtr.Zero && existing.WindowHandle == target.WindowHandle)
        {
            return true;
        }

        if (string.Equals(existing.ProcessName, target.ProcessName, StringComparison.OrdinalIgnoreCase) && existing.IsPinned == target.IsPinned)
        {
            return true;
        }

        return false;
    }

    private void SyncOpenWindows(IReadOnlyList<AppWindowInfo> latest)
    {
        for (int i = 0; i < latest.Count; i++)
        {
            var incoming = latest[i];
            if (i < OpenWindows.Count)
            {
                var current = OpenWindows[i];
                if (!AreSameWindowState(current, incoming))
                {
                    OpenWindows[i] = incoming;
                }
            }
            else
            {
                OpenWindows.Add(incoming);
            }
        }

        while (OpenWindows.Count > latest.Count)
        {
            OpenWindows.RemoveAt(OpenWindows.Count - 1);
        }
    }

    private void SyncOpenAppsForPinning(IReadOnlyList<AppWindowInfo> windows, List<DockShortcut> pinned)
    {
        var pinnedPaths = pinned.Select(p => p.TargetPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pinnedNames = pinned.Select(p => Path.GetFileNameWithoutExtension(p.TargetPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < windows.Count; i++)
        {
            var win = windows[i];
            string? exe = !string.IsNullOrWhiteSpace(win.ExecutablePath) ? win.ExecutablePath : TryGetExecutablePath(win.Handle);
            bool isPinned = (!string.IsNullOrWhiteSpace(exe) && pinnedPaths.Contains(exe))
                || pinnedNames.Contains(win.ProcessName);

            if (i < OpenAppsForPinning.Count)
            {
                var existing = OpenAppsForPinning[i];
                if (existing.WindowInfo.Handle == win.Handle)
                {
                    existing.IsPinned = isPinned;
                }
                else
                {
                    OpenAppsForPinning[i] = new OpenAppPinItem(TogglePinOpenAppItem)
                    {
                        WindowInfo = win,
                        ExecutablePath = exe,
                        IsPinned = isPinned
                    };
                }
            }
            else
            {
                OpenAppsForPinning.Add(new OpenAppPinItem(TogglePinOpenAppItem)
                {
                    WindowInfo = win,
                    ExecutablePath = exe,
                    IsPinned = isPinned
                });
            }
        }

        while (OpenAppsForPinning.Count > windows.Count)
        {
            OpenAppsForPinning.RemoveAt(OpenAppsForPinning.Count - 1);
        }
    }

    private static bool AreSameWindowState(AppWindowInfo a, AppWindowInfo b)
    {
        return a.Handle == b.Handle
               && string.Equals(a.Title, b.Title, StringComparison.Ordinal)
               && a.IsFocused == b.IsFocused
               && a.IsMinimized == b.IsMinimized
               && string.Equals(a.ProcessName, b.ProcessName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requirement (2): Clicking an open app calls ToggleOrActivateWindow(handle),
    /// and clicking a closed pinned app launches its ExecutablePath!
    /// </summary>
    private void ExecuteDockAppClick(DockAppItem item)
    {
        if (item == null) return;

        if (item.IsOpen && item.WindowHandle != IntPtr.Zero)
        {
            SafeInvoke(() => _windowTrackerService.ToggleOrActivateWindow(item.WindowHandle));
            RefreshFastState();
        }
        else if (!string.IsNullOrWhiteSpace(item.ExecutablePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(item.ExecutablePath)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback attempt with process name
                try
                {
                    Process.Start(new ProcessStartInfo(item.ProcessName)
                    {
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Ignore start error
                }
            }
        }
    }

    /// <summary>
    /// Requirement (2): Pin/unpin that app's ExecutablePath and save to JSON immediately!
    /// </summary>
    public void PinApp(DockAppItem item)
    {
        if (item == null) return;

        string? path = item.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) && item.WindowHandle != IntPtr.Zero)
        {
            path = TryGetExecutablePath(item.WindowHandle);
            item.ExecutablePath = path;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var existing = _dockSettingsService.CurrentSettings.PinnedShortcuts
            .FirstOrDefault(s => string.Equals(s.TargetPath, path, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            var shortcut = new DockShortcut
            {
                Id = Guid.NewGuid().ToString(),
                Title = !string.IsNullOrWhiteSpace(item.Title) ? item.Title : Path.GetFileNameWithoutExtension(path),
                TargetPath = path
            };

            // AddShortcut calls SaveSettings() immediately to persist to dock_settings.json
            _dockSettingsService.AddShortcut(shortcut);
            item.ShortcutId = shortcut.Id;
            item.IsPinned = true;
        }
        else
        {
            item.ShortcutId = existing.Id;
            item.IsPinned = true;
        }

        SyncPinnedShortcutsFromSettings();
        RefreshFastState();
    }

    /// <summary>
    /// Requirement (2): Unpin app and save to JSON immediately!
    /// </summary>
    public void UnpinApp(DockAppItem item)
    {
        if (item == null) return;

        if (!string.IsNullOrWhiteSpace(item.ShortcutId))
        {
            _dockSettingsService.RemoveShortcut(item.ShortcutId);
        }
        else if (!string.IsNullOrWhiteSpace(item.ExecutablePath))
        {
            var match = _dockSettingsService.CurrentSettings.PinnedShortcuts
                .FirstOrDefault(s => string.Equals(s.TargetPath, item.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _dockSettingsService.RemoveShortcut(match.Id);
            }
        }

        item.IsPinned = false;
        item.ShortcutId = null;

        SyncPinnedShortcutsFromSettings();
        RefreshFastState();
    }

    public void TogglePinApp(DockAppItem item)
    {
        if (item == null) return;
        if (item.IsPinned)
        {
            UnpinApp(item);
        }
        else
        {
            PinApp(item);
        }
    }

    /// <summary>
    /// Requirement (2): 1-click pinning of open apps inside CustomizationWindow!
    /// </summary>
    public void PinOpenWindow(AppWindowInfo window)
    {
        if (window == null) return;

        string? path = !string.IsNullOrWhiteSpace(window.ExecutablePath) ? window.ExecutablePath : TryGetExecutablePath(window.Handle);
        if (string.IsNullOrWhiteSpace(path)) return;

        var existing = _dockSettingsService.CurrentSettings.PinnedShortcuts
            .FirstOrDefault(s => string.Equals(s.TargetPath, path, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            // Already pinned: unpin and save immediately
            _dockSettingsService.RemoveShortcut(existing.Id);
        }
        else
        {
            // Pin and save immediately
            var shortcut = new DockShortcut
            {
                Id = Guid.NewGuid().ToString(),
                Title = !string.IsNullOrWhiteSpace(window.Title) ? window.Title : Path.GetFileNameWithoutExtension(path),
                TargetPath = path
            };
            _dockSettingsService.AddShortcut(shortcut);
        }

        SyncPinnedShortcutsFromSettings();
        RefreshFastState();
    }

    private void TogglePinOpenAppItem(OpenAppPinItem pinItem)
    {
        if (pinItem == null) return;
        PinOpenWindow(pinItem.WindowInfo);
    }

    private void ExecuteWindowClick(object? parameter)
    {
        if (parameter is AppWindowInfo windowInfo && windowInfo.Handle != IntPtr.Zero)
        {
            SafeInvoke(() => _windowTrackerService.ToggleOrActivateWindow(windowInfo.Handle));
            RefreshFastState();
        }
        else if (parameter is IntPtr handle && handle != IntPtr.Zero)
        {
            SafeInvoke(() => _windowTrackerService.ToggleOrActivateWindow(handle));
            RefreshFastState();
        }
        else if (parameter is DockAppItem dockItem)
        {
            ExecuteDockAppClick(dockItem);
        }
    }

    private void LaunchPinnedShortcut(PinnedShortcutItem shortcut)
    {
        try
        {
            Process.Start(new ProcessStartInfo(shortcut.TargetPath, shortcut.Arguments)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore process start failure
        }
    }

    public void AddShortcut(string name, string targetPath, string arguments = "", string glyph = "🚀")
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        var shortcut = new DockShortcut
        {
            Id = Guid.NewGuid().ToString(),
            Title = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(targetPath) : name,
            TargetPath = targetPath,
            Arguments = arguments
        };

        _dockSettingsService.AddShortcut(shortcut);
        SyncPinnedShortcutsFromSettings();
        RefreshFastState();
    }

    private void ExecuteAddCustomShortcut(object? parameter)
    {
        if (parameter is string path && !string.IsNullOrWhiteSpace(path))
        {
            AddShortcut(Path.GetFileNameWithoutExtension(path), path);
        }
    }

    private void ExecuteRemoveShortcut(object? parameter)
    {
        if (parameter is PinnedShortcutItem item)
        {
            _dockSettingsService.RemoveShortcut(item.Id);
            PinnedShortcuts.Remove(item);
            RefreshFastState();
        }
    }

    private void ExecuteMoveShortcutUp(object? parameter)
    {
        if (parameter is PinnedShortcutItem item)
        {
            int idx = PinnedShortcuts.IndexOf(item);
            if (idx > 0)
            {
                PinnedShortcuts.Move(idx, idx - 1);
                var list = _dockSettingsService.CurrentSettings.PinnedShortcuts;
                var found = list.FirstOrDefault(s => s.Id == item.Id);
                if (found != null)
                {
                    int sIdx = list.IndexOf(found);
                    if (sIdx > 0)
                    {
                        list.RemoveAt(sIdx);
                        list.Insert(sIdx - 1, found);
                        _dockSettingsService.SaveSettings();
                    }
                }
                RefreshFastState();
            }
        }
    }

    private void ExecuteMoveShortcutDown(object? parameter)
    {
        if (parameter is PinnedShortcutItem item)
        {
            int idx = PinnedShortcuts.IndexOf(item);
            if (idx >= 0 && idx < PinnedShortcuts.Count - 1)
            {
                PinnedShortcuts.Move(idx, idx + 1);
                var list = _dockSettingsService.CurrentSettings.PinnedShortcuts;
                var found = list.FirstOrDefault(s => s.Id == item.Id);
                if (found != null)
                {
                    int sIdx = list.IndexOf(found);
                    if (sIdx >= 0 && sIdx < list.Count - 1)
                    {
                        list.RemoveAt(sIdx);
                        list.Insert(sIdx + 1, found);
                        _dockSettingsService.SaveSettings();
                    }
                }
                RefreshFastState();
            }
        }
    }

    private void ExecuteMoveWidgetUp(object? parameter)
    {
        if (parameter is DockWidgetItem item)
        {
            int idx = DockWidgets.IndexOf(item);
            if (idx > 0)
            {
                DockWidgets.Move(idx, idx - 1);
            }
        }
    }

    private void ExecuteMoveWidgetDown(object? parameter)
    {
        if (parameter is DockWidgetItem item)
        {
            int idx = DockWidgets.IndexOf(item);
            if (idx >= 0 && idx < DockWidgets.Count - 1)
            {
                DockWidgets.Move(idx, idx + 1);
            }
        }
    }

    private void ExecutePlayPause()
    {
        SafeInvoke(_mediaIntegrationService.PlayPause);
        _ = RefreshMediaImmediateAsync();
    }

    private void ExecuteNextTrack()
    {
        SafeInvoke(_mediaIntegrationService.NextTrack);
        _ = RefreshMediaImmediateAsync();
    }

    private void ExecutePreviousTrack()
    {
        SafeInvoke(_mediaIntegrationService.PreviousTrack);
        _ = RefreshMediaImmediateAsync();
    }

    private async Task RefreshMediaImmediateAsync()
    {
        await Task.Delay(250);
        try
        {
            var media = await Task.Run(() => _mediaIntegrationService.GetCurrentMedia());
            if (!_isDisposed)
            {
                CurrentMedia = media;
            }
        }
        catch
        {
            // Ignore transient media errors
        }
    }

    private static void ExecuteOpenYouTube()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.youtube.com")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell launch error
        }
    }

    private void ExecuteLaunchSteamGame(object? parameter)
    {
        if (parameter is SteamGameInfo game && game.AppId > 0)
        {
            SafeInvoke(() => _steamIntegrationService.LaunchGame(game.AppId));
        }
        else if (parameter is int appId && appId > 0)
        {
            SafeInvoke(() => _steamIntegrationService.LaunchGame(appId));
        }
    }

    private void ExecuteLaunchSelectedSteamGame()
    {
        if (SelectedSteamGame is { AppId: > 0 } game)
        {
            SafeInvoke(() => _steamIntegrationService.LaunchGame(game.AppId));
        }
        else
        {
            SafeInvoke(_steamIntegrationService.OpenSteam);
        }
    }

    private void ExecuteToggleNativeTaskbar()
    {
        SafeInvoke(_nativeTaskbarService.ToggleNativeTaskbar);
        OnPropertyChanged(nameof(IsNativeTaskbarHidden));
        OnPropertyChanged(nameof(NativeTaskbarStatusText));
    }

    private void ExecuteToggleBarEdge()
    {
        CurrentEdge = CurrentEdge == BarEdge.Top ? BarEdge.Bottom : BarEdge.Top;
    }

    private void ExecuteToggleWidgets()
    {
        AreWidgetsVisible = !AreWidgetsVisible;
    }

    private void ExecuteExitApp()
    {
        SafeInvoke(_nativeTaskbarService.ShowNativeTaskbar);
        Application.Current?.Shutdown();
    }

    private async Task RefreshTelemetryAsync()
    {
        if (_isDisposed || _isRefreshingTelemetry)
        {
            return;
        }

        _isRefreshingTelemetry = true;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                HardwareMetrics hw;
                try { hw = _hardwareMonitorService.GetCurrentMetrics(); }
                catch { hw = new HardwareMetrics(); }

                MediaInfo media;
                try { media = _mediaIntegrationService.GetCurrentMedia(); }
                catch { media = new MediaInfo(); }

                SteamStatus steam;
                try { steam = _steamIntegrationService.GetStatus(); }
                catch { steam = new SteamStatus(); }

                AiUsageSnapshot ai;
                try { ai = _aiUsageService.GetUsageSnapshot(); }
                catch { ai = new AiUsageSnapshot(); }

                DiscordStatus discord;
                try { discord = _discordStatusService.GetStatus(); }
                catch { discord = new DiscordStatus(); }

                return (hw, media, steam, ai, discord);
            });

            if (_isDisposed)
            {
                return;
            }

            Hardware = snapshot.hw;
            CurrentMedia = snapshot.media;
            UpdateSteamState(snapshot.steam);
            UpdateAiState(snapshot.ai);
            DiscordStatus = snapshot.discord;
            OnPropertyChanged(nameof(IsNativeTaskbarHidden));
            OnPropertyChanged(nameof(NativeTaskbarStatusText));
        }
        finally
        {
            _isRefreshingTelemetry = false;
        }
    }

    private void UpdateSteamState(SteamStatus status)
    {
        var games = status.InstalledGames ?? Array.Empty<SteamGameInfo>();
        bool gamesChanged = games.Count != InstalledGames.Count
                            || !games.Select(g => g.AppId).SequenceEqual(InstalledGames.Select(g => g.AppId));

        if (gamesChanged)
        {
            var previousSelectedId = SelectedSteamGame?.AppId;
            InstalledGames.Clear();
            foreach (var game in games)
            {
                InstalledGames.Add(game);
            }

            SelectedSteamGame = InstalledGames.FirstOrDefault(g => g.AppId == previousSelectedId)
                                ?? InstalledGames.FirstOrDefault();
        }

        SteamStatus = status;
    }

    private void UpdateAiState(AiUsageSnapshot snapshot)
    {
        var providers = snapshot.Providers ?? Array.Empty<AiProviderUsage>();
        AiProviders.Clear();
        foreach (var provider in providers)
        {
            AiProviders.Add(provider);
        }

        AiUsage = snapshot;
    }

    public static ImageSource? ExtractIconForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (IconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            string resolvedPath = Environment.ExpandEnvironmentVariables(path);
            if (!File.Exists(resolvedPath))
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (File.Exists(Path.Combine(sys32, resolvedPath)))
                {
                    resolvedPath = Path.Combine(sys32, resolvedPath);
                }
                else if (File.Exists(Path.Combine(winDir, resolvedPath)))
                {
                    resolvedPath = Path.Combine(winDir, resolvedPath);
                }
            }

            if (File.Exists(resolvedPath))
            {
                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(resolvedPath);
                if (sysIcon != null)
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    IconCache[path] = bitmap;
                    return bitmap;
                }
            }
        }
        catch
        {
            // Ignore icon extraction failure
        }

        IconCache[path] = null;
        return null;
    }

    public static string? TryGetExecutablePath(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return null;

        if (ExePathCache.TryGetValue(hWnd, out var cached))
        {
            return cached;
        }

        string? exePath = null;
        try
        {
            Win32Interop.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid > 0)
            {
                IntPtr hProcess = Win32Interop.OpenProcess(Win32Interop.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (Win32Interop.QueryFullProcessImageName(hProcess, 0, sb, ref size) && size > 0)
                        {
                            exePath = sb.ToString();
                        }
                    }
                    finally
                    {
                        Win32Interop.CloseHandle(hProcess);
                    }
                }

                if (string.IsNullOrWhiteSpace(exePath))
                {
                    using var proc = Process.GetProcessById((int)pid);
                    exePath = proc.MainModule?.FileName;
                }
            }
        }
        catch
        {
            // Ignore access errors on system processes
        }

        ExePathCache[hWnd] = exePath;
        return exePath;
    }

    private static void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Prevent integration errors from crashing UI
        }
    }

    private static string GetGreeting()
    {
        int hour = DateTime.Now.Hour;
        return hour switch
        {
            >= 5 and < 12 => "Bom dia",
            >= 12 and < 18 => "Boa tarde",
            _ => "Boa noite"
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _fastTimer.Stop();
        _telemetryTimer.Stop();
    }
}
