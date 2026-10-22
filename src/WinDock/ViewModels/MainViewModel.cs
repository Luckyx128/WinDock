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
using System.Windows.Data;
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

public interface IDockOrderableItem : INotifyPropertyChanged
{
    string DockOrderKey { get; }
    string Title { get; }
    bool IsWidget { get; }
    bool IsDragging { get; set; }
    ICommand MoveLeftCommand { get; }
    ICommand MoveRightCommand { get; }
}

public enum DockWidgetSize
{
    Size1x1 = 1,
    Size1x2 = 2,
    Size1x3 = 3
}

public sealed class DockWidgetItem : IDockOrderableItem, INotifyPropertyChanged
{
    private bool _isVisible;
    private DockWidgetSize _size;
    private bool _isDragging;

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string IconGlyph { get; init; }
    public required string Description { get; init; }

    public string AppGroup { get; init; } = "Geral";
    public string AppIconGlyph { get; init; } = "🧩";
    public string AppGroupDescription { get; init; } = string.Empty;
    public int AppGroupOrder { get; init; } = 99;

    public string DockOrderKey => $"widget:{Id}";
    public bool IsWidget => true;

    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging != value)
            {
                _isDragging = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand MoveLeftCommand { get; set; } = new RelayCommand(() => { });
    public ICommand MoveRightCommand { get; set; } = new RelayCommand(() => { });

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
        DockWidgetSize.Size1x1 => 40.0,
        DockWidgetSize.Size1x2 => 82.0,
        DockWidgetSize.Size1x3 => 124.0,
        _ => 40.0
    };

    public bool IsSize1x1 => Size == DockWidgetSize.Size1x1;
    public bool IsSize1x2 => Size == DockWidgetSize.Size1x2;
    public bool IsSize1x3 => Size == DockWidgetSize.Size1x3;
    public string SizeLabel => Size switch
    {
        DockWidgetSize.Size1x1 => "1x1 (40px)",
        DockWidgetSize.Size1x2 => "1x2 (82px)",
        DockWidgetSize.Size1x3 => "1x3 (124px)",
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
public sealed class DockAppItem : IDockOrderableItem, INotifyPropertyChanged
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
    private bool _isDragging;

    public string DockOrderKey => MainViewModel.GetDockAppKey(this);
    public bool IsWidget => false;

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

    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging != value)
            {
                _isDragging = value;
                OnPropertyChanged();
            }
        }
    }

    public string PinMenuText => IsPinned ? "Desafixar da Dock" : "Fixar na Dock";

    public ICommand ClickCommand { get; }
    public ICommand TogglePinCommand { get; }
    public ICommand MoveLeftCommand { get; }
    public ICommand MoveRightCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DockAppItem(
        Action<DockAppItem> onClick,
        Action<DockAppItem> onTogglePin,
        Action<DockAppItem>? onMoveLeft = null,
        Action<DockAppItem>? onMoveRight = null)
    {
        ClickCommand = new RelayCommand(() => onClick(this));
        TogglePinCommand = new RelayCommand(() => onTogglePin(this));
        MoveLeftCommand = new RelayCommand(() => onMoveLeft?.Invoke(this));
        MoveRightCommand = new RelayCommand(() => onMoveRight?.Invoke(this));
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
    public string ColorHex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
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
    private readonly List<string> _customDockOrderKeys = new();
    private bool _isRefreshingTelemetry;
    private bool _isDisposed;
    private bool _isLoadingCustomizations;

    public const double MinBarHeight = 36.0;
    public const double MaxBarHeight = 80.0;
    public const double DefaultBarHeight = 48.0;

    private IntPtr _excludeWindowHandle;
    private BarEdge _currentEdge = BarEdge.Bottom;
    private double _barHeightDip = DefaultBarHeight;
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
    /// Unified interleaved dock strip containing apps and visible modular widgets in customizable order.
    /// </summary>
    public ObservableCollection<IDockOrderableItem> DockStripItems { get; } = new();

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

    /// <summary>
    /// Requirement (Sprint 11-A): Grouped dock widgets by App/Category with real-time filtering.
    /// </summary>
    public ICollectionView GroupedDockWidgets { get; }
    public ICollectionView AllGroupedDockWidgets { get; }

    private string _selectedWidgetGroupFilter = "Todos";
    public string SelectedWidgetGroupFilter
    {
        get => _selectedWidgetGroupFilter;
        set
        {
            if (_selectedWidgetGroupFilter != value)
            {
                _selectedWidgetGroupFilter = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFilterAll));
                OnPropertyChanged(nameof(IsFilterSteam));
                OnPropertyChanged(nameof(IsFilterDiscord));
                OnPropertyChanged(nameof(IsFilterMedia));
                OnPropertyChanged(nameof(IsFilterSystem));
                GroupedDockWidgets.Refresh();
            }
        }
    }

    public bool IsFilterAll => string.Equals(_selectedWidgetGroupFilter, "Todos", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterSteam => string.Equals(_selectedWidgetGroupFilter, "Steam", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterDiscord => string.Equals(_selectedWidgetGroupFilter, "Discord", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterMedia => string.Equals(_selectedWidgetGroupFilter, "Mídia & YouTube", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterSystem => string.Equals(_selectedWidgetGroupFilter, "Sistema & Hardware", StringComparison.OrdinalIgnoreCase);

    public int TotalWidgetsCount => DockWidgets.Count;
    public int SteamWidgetsCount => DockWidgets.Count(w => w.AppGroup == "Steam");
    public int DiscordWidgetsCount => DockWidgets.Count(w => w.AppGroup == "Discord");
    public int MediaWidgetsCount => DockWidgets.Count(w => w.AppGroup == "Mídia & YouTube");
    public int SystemWidgetsCount => DockWidgets.Count(w => w.AppGroup == "Sistema & Hardware");
    public int ActiveWidgetsCount => DockWidgets.Count(w => w.IsVisible);

    public ICommand SetWidgetFilterCommand { get; }
    public ObservableCollection<ThemePreset> AvailableThemes { get; } = new();
    public ObservableCollection<AccentColorOption> AvailableAccents { get; } = new();
    public ObservableCollection<SteamGameInfo> InstalledGames { get; } = new();
    public ObservableCollection<AiProviderUsage> AiProviders { get; } = new();

    public ICommand WindowClickCommand { get; }
    public ICommand DockAppClickCommand { get; }
    public ICommand TogglePinAppCommand { get; }
    public ICommand PinOpenWindowCommand { get; }
    public ICommand MoveDockAppLeftCommand { get; }
    public ICommand MoveDockAppRightCommand { get; }
    public ICommand MoveDockItemLeftCommand { get; }
    public ICommand MoveDockItemRightCommand { get; }

    public ICommand PlayPauseCommand { get; }
    public ICommand NextTrackCommand { get; }
    public ICommand PreviousTrackCommand { get; }
    public ICommand OpenYouTubeCommand { get; }
    public ICommand LaunchSteamGameCommand { get; }
    public ICommand LaunchSelectedSteamGameCommand { get; }
    public ICommand OpenSteamCommand { get; }
    public ICommand LaunchLastPlayedSteamGameCommand { get; }
    public ICommand LaunchRecentSteamGame1Command { get; }
    public ICommand LaunchRecentSteamGame2Command { get; }
    public ICommand LaunchRecentSteamGame3Command { get; }
    public ICommand OpenSteamDownloadsCommand { get; }
    public ICommand OpenSteamGamesCommand { get; }
    public ICommand OpenSteamFriendsCommand { get; }
    public ICommand OpenSteamScreenshotsCommand { get; }
    public ICommand OpenSteamStorageCommand { get; }
    public ICommand FocusDiscordCommand { get; }
    public ICommand ToggleDiscordMuteCommand { get; }
    public ICommand ToggleDiscordDeafenCommand { get; }
    public ICommand DisconnectDiscordVoiceCommand { get; }
    public ICommand OpenDiscordDMsCommand { get; }
    public ICommand OpenDiscordSettingsCommand { get; }
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
    public ICommand SetBarHeightCommand { get; }

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
        MoveDockAppLeftCommand = new RelayCommand(param =>
        {
            if (param is DockAppItem item) MoveDockAppLeft(item);
            else if (param is IDockOrderableItem orderable) MoveDockItemLeft(orderable);
        });
        MoveDockAppRightCommand = new RelayCommand(param =>
        {
            if (param is DockAppItem item) MoveDockAppRight(item);
            else if (param is IDockOrderableItem orderable) MoveDockItemRight(orderable);
        });
        MoveDockItemLeftCommand = new RelayCommand(param =>
        {
            if (param is IDockOrderableItem item) MoveDockItemLeft(item);
        });
        MoveDockItemRightCommand = new RelayCommand(param =>
        {
            if (param is IDockOrderableItem item) MoveDockItemRight(item);
        });

        PlayPauseCommand = new RelayCommand(ExecutePlayPause);
        NextTrackCommand = new RelayCommand(ExecuteNextTrack);
        PreviousTrackCommand = new RelayCommand(ExecutePreviousTrack);
        OpenYouTubeCommand = new RelayCommand(ExecuteOpenYouTube);
        LaunchSteamGameCommand = new RelayCommand(ExecuteLaunchSteamGame);
        LaunchSelectedSteamGameCommand = new RelayCommand(ExecuteLaunchSelectedSteamGame);
        OpenSteamCommand = new RelayCommand(() => SafeInvoke(_steamIntegrationService.OpenSteam));
        LaunchLastPlayedSteamGameCommand = new RelayCommand(ExecuteLaunchLastPlayedSteamGame);
        LaunchRecentSteamGame1Command = new RelayCommand(() => ExecuteLaunchRecentSteamGame(0));
        LaunchRecentSteamGame2Command = new RelayCommand(() => ExecuteLaunchRecentSteamGame(1));
        LaunchRecentSteamGame3Command = new RelayCommand(() => ExecuteLaunchRecentSteamGame(2));
        OpenSteamDownloadsCommand = new RelayCommand(() => ExecuteOpenSteamProtocol("steam://open/downloads"));
        OpenSteamGamesCommand = new RelayCommand(() => ExecuteOpenSteamProtocol("steam://open/games"));
        OpenSteamFriendsCommand = new RelayCommand(() => ExecuteOpenSteamProtocol("steam://open/friends"));
        OpenSteamScreenshotsCommand = new RelayCommand(() => ExecuteOpenSteamProtocol("steam://open/screenshots"));
        OpenSteamStorageCommand = new RelayCommand(() => ExecuteOpenSteamProtocol("steam://open/storage"));
        FocusDiscordCommand = new RelayCommand(() => SafeInvoke(_discordStatusService.FocusOrLaunchDiscord));
        ToggleDiscordMuteCommand = new RelayCommand(() => SafeInvoke(_discordStatusService.ToggleMute));
        ToggleDiscordDeafenCommand = new RelayCommand(() => SafeInvoke(_discordStatusService.ToggleDeafen));
        DisconnectDiscordVoiceCommand = new RelayCommand(() => SafeInvoke(_discordStatusService.DisconnectVoice));
        OpenDiscordDMsCommand = new RelayCommand(() => SafeInvoke(_discordStatusService.OpenDMs));
        OpenDiscordSettingsCommand = new RelayCommand(() => SafeInvoke(_discordStatusService.OpenSettings));
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
        SetBarHeightCommand = new RelayCommand(param =>
        {
            if (param is double d) BarHeightDip = d;
            else if (param is float f) BarHeightDip = f;
            else if (param is int i) BarHeightDip = i;
            else if (param is string s && double.TryParse(s, out double parsed)) BarHeightDip = parsed;
        });

        // Initialize Themes & Accents
        InitializeThemesAndAccents();
        _currentTheme = AvailableThemes[0];
        _currentAccent = AvailableAccents[0];

        // Clean Dock initialization: no fake emoji shortcuts & all dock widgets IsVisible = false by default!
        InitializeCleanPinnedShortcuts();
        InitializeDockWidgets();

        // Sprint 11-A: Grouped Dock Widgets by App with sorting and category filter
        GroupedDockWidgets = new ListCollectionView(DockWidgets);
        GroupedDockWidgets.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DockWidgetItem.AppGroup)));
        GroupedDockWidgets.SortDescriptions.Add(new SortDescription(nameof(DockWidgetItem.AppGroupOrder), ListSortDirection.Ascending));
        GroupedDockWidgets.Filter = obj =>
        {
            if (obj is not DockWidgetItem item) return false;
            if (string.IsNullOrEmpty(_selectedWidgetGroupFilter) || _selectedWidgetGroupFilter == "Todos")
                return true;
            return string.Equals(item.AppGroup, _selectedWidgetGroupFilter, StringComparison.OrdinalIgnoreCase);
        };

        AllGroupedDockWidgets = new ListCollectionView(DockWidgets);
        AllGroupedDockWidgets.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DockWidgetItem.AppGroup)));
        AllGroupedDockWidgets.SortDescriptions.Add(new SortDescription(nameof(DockWidgetItem.AppGroupOrder), ListSortDirection.Ascending));

        SetWidgetFilterCommand = new RelayCommand(param =>
        {
            if (param is string filter)
            {
                SelectedWidgetGroupFilter = filter;
            }
        });

        // Restore and apply all saved user customizations (Theme, Accent, Edge, Widgets & Dock Order)
        LoadSavedCustomizations();

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
    /// When any widget is made visible, AreWidgetsVisible is automatically turned on.
    /// </summary>
    private void InitializeDockWidgets()
    {
        DockWidgets.Clear();

        void AddWidget(DockWidgetItem item)
        {
            item.MoveLeftCommand = new RelayCommand(() => MoveDockItemLeft(item));
            item.MoveRightCommand = new RelayCommand(() => MoveDockItemRight(item));
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DockWidgetItem.IsVisible))
                {
                    if (item.IsVisible)
                    {
                        AreWidgetsVisible = true;
                    }
                    OnPropertyChanged(nameof(ActiveWidgetsCount));
                    SyncUnifiedDockStrip();
                    SaveAllCustomizationsToDisk();
                }
                else if (e.PropertyName == nameof(DockWidgetItem.Size))
                {
                    SaveAllCustomizationsToDisk();
                }
            };
            DockWidgets.Add(item);
        }

        // 1. STEAM WIDGETS (6 modular widgets)
        AddWidget(new DockWidgetItem
        {
            Id = "steam-jump-in",
            Title = "Steam: Continuar Jogando",
            IconGlyph = "▶",
            Description = "1-clique para continuar o último jogo com banner e ícone oficial",
            AppGroup = "Steam",
            AppIconGlyph = "🎮",
            AppGroupDescription = "Módulos integrados ao cliente oficial da Steam",
            AppGroupOrder = 1,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        AddWidget(new DockWidgetItem
        {
            Id = "steam-recent",
            Title = "Steam: Jogos Recentes",
            IconGlyph = "🕹",
            Description = "Estante dos 1, 2 ou 3 últimos jogos com ícones originais",
            AppGroup = "Steam",
            AppIconGlyph = "🎮",
            AppGroupDescription = "Módulos integrados ao cliente oficial da Steam",
            AppGroupOrder = 1,
            IsVisible = false,
            Size = DockWidgetSize.Size1x3
        });

        AddWidget(new DockWidgetItem
        {
            Id = "steam-downloads",
            Title = "Steam: Downloads & Updates",
            IconGlyph = "⬇",
            Description = "Progresso de download/atualização com barra e porcentagem",
            AppGroup = "Steam",
            AppIconGlyph = "🎮",
            AppGroupDescription = "Módulos integrados ao cliente oficial da Steam",
            AppGroupOrder = 1,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        AddWidget(new DockWidgetItem
        {
            Id = "steam-now-playing",
            Title = "Steam: Sessão & Perfil",
            IconGlyph = "👤",
            Description = "Nome de perfil, avatar/ícone Steam e jogo em execução",
            AppGroup = "Steam",
            AppIconGlyph = "🎮",
            AppGroupDescription = "Módulos integrados ao cliente oficial da Steam",
            AppGroupOrder = 1,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        AddWidget(new DockWidgetItem
        {
            Id = "steam-hub",
            Title = "Steam: Atalhos Rápidos",
            IconGlyph = "🎮",
            Description = "Acesso rápido com ícone oficial (Biblioteca, Amigos, Prints)",
            AppGroup = "Steam",
            AppIconGlyph = "🎮",
            AppGroupDescription = "Módulos integrados ao cliente oficial da Steam",
            AppGroupOrder = 1,
            IsVisible = false,
            Size = DockWidgetSize.Size1x1
        });

        AddWidget(new DockWidgetItem
        {
            Id = "steam-storage",
            Title = "Steam: Espaço da Biblioteca",
            IconGlyph = "💾",
            Description = "Espaço ocupado no SSD e destaque do maior jogo instalado",
            AppGroup = "Steam",
            AppIconGlyph = "🎮",
            AppGroupDescription = "Módulos integrados ao cliente oficial da Steam",
            AppGroupOrder = 1,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        // 2. DISCORD WIDGETS (6 Módulos Oficiais)
        AddWidget(new DockWidgetItem
        {
            Id = "discord",
            Title = "Discord: Sessão & Status",
            IconGlyph = "💬",
            Description = "Status da sessão, presença rica e atalho de ativação",
            AppGroup = "Discord",
            AppIconGlyph = "💬",
            AppGroupDescription = "Integração completa com chamadas de voz e atividade",
            AppGroupOrder = 2,
            IsVisible = false,
            Size = DockWidgetSize.Size1x1
        });

        AddWidget(new DockWidgetItem
        {
            Id = "discord-voice",
            Title = "Discord: Chamada de Voz",
            IconGlyph = "🎧",
            Description = "Detecção de chamada de voz ativa com cronômetro em tempo real",
            AppGroup = "Discord",
            AppIconGlyph = "💬",
            AppGroupDescription = "Integração completa com chamadas de voz e atividade",
            AppGroupOrder = 2,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        AddWidget(new DockWidgetItem
        {
            Id = "discord-controls",
            Title = "Discord: Controles de Voz",
            IconGlyph = "🎙",
            Description = "Controles rápidos para mutar microfone e ensurdecer áudio",
            AppGroup = "Discord",
            AppIconGlyph = "💬",
            AppGroupDescription = "Integração completa com chamadas de voz e atividade",
            AppGroupOrder = 2,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        AddWidget(new DockWidgetItem
        {
            Id = "discord-mentions",
            Title = "Discord: Menções & Não Lidas",
            IconGlyph = "🔴",
            Description = "Contador de menções pendentes com badge numérico",
            AppGroup = "Discord",
            AppIconGlyph = "💬",
            AppGroupDescription = "Integração completa com chamadas de voz e atividade",
            AppGroupOrder = 2,
            IsVisible = false,
            Size = DockWidgetSize.Size1x1
        });

        AddWidget(new DockWidgetItem
        {
            Id = "discord-channel",
            Title = "Discord: Canal & Servidor",
            IconGlyph = "🏷",
            Description = "Canal e servidor em foco com pílula de iniciais",
            AppGroup = "Discord",
            AppIconGlyph = "💬",
            AppGroupDescription = "Integração completa com chamadas de voz e atividade",
            AppGroupOrder = 2,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        AddWidget(new DockWidgetItem
        {
            Id = "discord-hub",
            Title = "Discord: Atalhos Rápidos",
            IconGlyph = "⚡",
            Description = "Acesso rápido com links diretos para DMs, Amigos e Ajustes de Voz",
            AppGroup = "Discord",
            AppIconGlyph = "💬",
            AppGroupDescription = "Integração completa com chamadas de voz e atividade",
            AppGroupOrder = 2,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        // 3. MÍDIA & YOUTUBE WIDGET
        AddWidget(new DockWidgetItem
        {
            Id = "media",
            Title = "Mídia & YouTube",
            IconGlyph = "▶",
            Description = "Player com reprodução ativa, YouTube e controles de faixa",
            AppGroup = "Mídia & YouTube",
            AppIconGlyph = "▶",
            AppGroupDescription = "Controle multimídia do Windows e reprodução ativa",
            AppGroupOrder = 3,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        // 4. SISTEMA & HARDWARE WIDGETS
        AddWidget(new DockWidgetItem
        {
            Id = "hardware",
            Title = "Monitor de Hardware",
            IconGlyph = "⚡",
            Description = "Gauges de CPU, GPU e memória RAM em tempo real",
            AppGroup = "Sistema & Hardware",
            AppIconGlyph = "⚡",
            AppGroupDescription = "Sensores de CPU/RAM/GPU, contador de IA e relógio",
            AppGroupOrder = 4,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });

        AddWidget(new DockWidgetItem
        {
            Id = "ai",
            Title = "Resumo IA",
            IconGlyph = "✨",
            Description = "Contador de requisições de IA e modelos ativos",
            AppGroup = "Sistema & Hardware",
            AppIconGlyph = "⚡",
            AppGroupDescription = "Sensores de CPU/RAM/GPU, contador de IA e relógio",
            AppGroupOrder = 4,
            IsVisible = false,
            Size = DockWidgetSize.Size1x1
        });

        AddWidget(new DockWidgetItem
        {
            Id = "clock",
            Title = "Relógio & Calendário",
            IconGlyph = "🕒",
            Description = "Horário preciso, data e dia da semana",
            AppGroup = "Sistema & Hardware",
            AppIconGlyph = "⚡",
            AppGroupDescription = "Sensores de CPU/RAM/GPU, contador de IA e relógio",
            AppGroupOrder = 4,
            IsVisible = false,
            Size = DockWidgetSize.Size1x2
        });
    }

    private void LoadSavedCustomizations()
    {
        _isLoadingCustomizations = true;
        try
        {
            var settings = _dockSettingsService.CurrentSettings;

            // 1. Edge & BarHeightDip
            CurrentEdge = settings.Edge;
            if (settings.BarHeightDip >= MinBarHeight && settings.BarHeightDip <= MaxBarHeight)
            {
                _barHeightDip = settings.BarHeightDip;
                OnPropertyChanged(nameof(BarHeightDip));
                OnPropertyChanged(nameof(DockScale));
                OnPropertyChanged(nameof(BarHeightText));
            }

            // 2. AreWidgetsVisible
            AreWidgetsVisible = settings.AreDesktopWidgetsVisible;

            // 3. Theme & Accent
            if (!string.IsNullOrWhiteSpace(settings.SelectedThemeId))
            {
                var matchedTheme = AvailableThemes.FirstOrDefault(t =>
                    string.Equals(t.Id, settings.SelectedThemeId, StringComparison.OrdinalIgnoreCase));
                if (matchedTheme != null)
                {
                    _currentTheme = matchedTheme;
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.SelectedAccentHex))
            {
                var matchedAccent = AvailableAccents.FirstOrDefault(a =>
                    string.Equals(a.ColorHex, settings.SelectedAccentHex, StringComparison.OrdinalIgnoreCase));
                if (matchedAccent != null)
                {
                    _currentAccent = matchedAccent;
                }
                else
                {
                    try
                    {
                        var col = (Color)ColorConverter.ConvertFromString(settings.SelectedAccentHex);
                        var customAccent = new AccentColorOption { Name = "Personalizado", Color = col };
                        AvailableAccents.Insert(0, customAccent);
                        _currentAccent = customAccent;
                    }
                    catch
                    {
                    }
                }
            }

            // Apply theme resources immediately
            ApplyTheme(_currentTheme, _currentAccent?.Color);
            OnPropertyChanged(nameof(CurrentTheme));
            OnPropertyChanged(nameof(CurrentAccent));

            // 4. Widget Visibility & Sizes
            if (settings.EnabledWidgets is { Count: > 0 })
            {
                foreach (var widget in DockWidgets)
                {
                    bool isEnabled = settings.EnabledWidgets.Any(w =>
                        string.Equals(w, widget.Id, StringComparison.OrdinalIgnoreCase) ||
                        (string.Equals(w, "steam", StringComparison.OrdinalIgnoreCase) && string.Equals(widget.Id, "steam-jump-in", StringComparison.OrdinalIgnoreCase)));
                    widget.IsVisible = isEnabled;

                    if (settings.WidgetSizes.TryGetValue(widget.Id, out var savedSize))
                    {
                        widget.Size = (DockWidgetSize)(int)savedSize;
                    }
                }
            }
            OnPropertyChanged(nameof(ActiveWidgetsCount));

            // 5. Dock Order Keys
            if (settings.DockOrderKeys is { Count: > 0 })
            {
                _customDockOrderKeys.Clear();
                _customDockOrderKeys.AddRange(settings.DockOrderKeys);
            }

            // 6. Sync Unified Dock Strip
            SyncUnifiedDockStrip();
        }
        catch
        {
            // Ignore error loading customizations
        }
        finally
        {
            _isLoadingCustomizations = false;
        }
    }

    public void SaveAllCustomizationsToDisk()
    {
        if (_isLoadingCustomizations || _isDisposed)
        {
            return;
        }

        try
        {
            var settings = _dockSettingsService.CurrentSettings;
            settings.Edge = CurrentEdge;
            settings.BarHeightDip = BarHeightDip;
            settings.AreDesktopWidgetsVisible = AreWidgetsVisible;
            settings.SelectedThemeId = CurrentTheme?.Id ?? "dark-modern";
            settings.SelectedAccentHex = CurrentAccent?.ColorHex ?? "#007ACC";

            settings.EnabledWidgets.Clear();
            foreach (var widget in DockWidgets)
            {
                if (widget.IsVisible)
                {
                    settings.EnabledWidgets.Add(widget.Id);
                }
                settings.WidgetSizes[widget.Id] = (WidgetSize)(int)widget.Size;
            }

            settings.DockOrderKeys.Clear();
            if (_customDockOrderKeys.Count > 0)
            {
                settings.DockOrderKeys.AddRange(_customDockOrderKeys);
            }
            else
            {
                foreach (var item in DockStripItems)
                {
                    settings.DockOrderKeys.Add(item.DockOrderKey);
                }
            }

            _dockSettingsService.SaveSettings();
        }
        catch
        {
            // Ignore save errors
        }
    }

    public void ToggleWidgetVisibility(DockWidgetItem? widget)
    {
        if (widget == null) return;
        widget.IsVisible = !widget.IsVisible;
        if (widget.IsVisible)
        {
            AreWidgetsVisible = true;
        }
        SyncUnifiedDockStrip();
        SaveAllCustomizationsToDisk();
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
                SaveAllCustomizationsToDisk();
            }
        }
    }

    public bool IsAtBottom => CurrentEdge == BarEdge.Bottom;
    public string EdgeLabel => CurrentEdge == BarEdge.Top ? "Topo" : "Base";

    public double BarHeightDip
    {
        get => _barHeightDip;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), MinBarHeight, MaxBarHeight);
            if (Math.Abs(_barHeightDip - clamped) > 0.1)
            {
                _barHeightDip = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DockScale));
                OnPropertyChanged(nameof(BarHeightText));
                BarHeightChanged?.Invoke(this, clamped);
                SaveAllCustomizationsToDisk();
            }
        }
    }

    public double DockScale => Math.Round(_barHeightDip / DefaultBarHeight, 3);
    public string BarHeightText => $"{BarHeightDip:0} px";

    public event EventHandler<double>? BarHeightChanged;

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
                SaveAllCustomizationsToDisk();
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
            OnPropertyChanged(nameof(LastPlayedGameTitle));
            OnPropertyChanged(nameof(LastPlayedGameIcon));
            OnPropertyChanged(nameof(LastPlayedGameHeader));
            OnPropertyChanged(nameof(RecentGames));
            OnPropertyChanged(nameof(RecentSteamGame1));
            OnPropertyChanged(nameof(RecentSteamGame2));
            OnPropertyChanged(nameof(RecentSteamGame3));
            OnPropertyChanged(nameof(SteamDownloadTitle));
            OnPropertyChanged(nameof(SteamDownloadIcon));
            OnPropertyChanged(nameof(SteamDownloadStatusText));
            OnPropertyChanged(nameof(SteamDownloadPercent));
            OnPropertyChanged(nameof(SteamSessionStatusText));
            OnPropertyChanged(nameof(SteamPersonaName));
            OnPropertyChanged(nameof(SteamClientIcon));
            OnPropertyChanged(nameof(SteamLargestGameText));
            OnPropertyChanged(nameof(SteamTotalStorageText));
            OnPropertyChanged(nameof(SteamLargestGameIcon));
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

    // Modular Steam Widgets properties (100% real artwork and icons)
    public string LastPlayedGameTitle => SteamStatus.LastPlayedGame?.Name ?? "Nenhum jogo recente";
    public ImageSource? LastPlayedGameIcon => SteamStatus.LastPlayedGame?.IconImage ?? SteamClientIcon;
    public ImageSource? LastPlayedGameHeader => SteamStatus.LastPlayedGame?.HeaderImage ?? LastPlayedGameIcon;

    public IReadOnlyList<SteamGameInfo> RecentGames =>
        InstalledGames.Where(g => g.LastPlayedUnix > 0)
            .OrderByDescending(g => g.LastPlayedUnix)
            .Take(3)
            .ToList() is { Count: > 0 } recent
            ? recent
            : InstalledGames.Take(3).ToList();

    public SteamGameInfo? RecentSteamGame1 => RecentGames.ElementAtOrDefault(0);
    public SteamGameInfo? RecentSteamGame2 => RecentGames.ElementAtOrDefault(1);
    public SteamGameInfo? RecentSteamGame3 => RecentGames.ElementAtOrDefault(2);

    public string SteamDownloadTitle => SteamStatus.ActiveDownloadGame?.Name
        ?? (InstalledGames.Any(g => g.IsDownloading) ? "Baixando..." : "Downloads em dia");
    public ImageSource? SteamDownloadIcon => SteamStatus.ActiveDownloadGame?.IconImage ?? SteamClientIcon;
    public string SteamDownloadStatusText => SteamStatus.ActiveDownloadGame != null
        ? $"{SteamStatus.ActiveDownloadGame.DownloadPercent:0}%"
        : "100%";
    public double SteamDownloadPercent => SteamStatus.ActiveDownloadGame != null
        ? SteamStatus.ActiveDownloadGame.DownloadPercent
        : 100.0;

    public string SteamSessionStatusText => IsSteamGameRunning
        ? $"Jogando {SteamStatus.RunningGameName}"
        : (SteamStatus.IsRunning ? "Online (Pronto)" : (SteamStatus.IsInstalled ? "Pronto para jogar" : "Steam offline"));
    public string SteamPersonaName => !string.IsNullOrWhiteSpace(SteamStatus.PersonaName) ? SteamStatus.PersonaName : "Luckyx";
    public ImageSource? SteamClientIcon => SteamStatus.SteamClientIcon;

    private ImageSource? _discordClientIcon;
    public ImageSource? DiscordClientIcon => _discordClientIcon ??= ResolveDiscordClientIcon();
    public bool HasDiscordClientIcon => DiscordClientIcon != null;

    private static ImageSource? ResolveDiscordClientIcon()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string discordBaseDir = Path.Combine(localAppData, "Discord");
            if (Directory.Exists(discordBaseDir))
            {
                var appDirs = Directory.GetDirectories(discordBaseDir, "app-*");
                if (appDirs.Length > 0)
                {
                    var latestAppDir = appDirs.OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase).First();
                    string discordExe = Path.Combine(latestAppDir, "Discord.exe");
                    if (File.Exists(discordExe))
                    {
                        var icon = ExtractIconForPath(discordExe);
                        if (icon != null) return icon;
                    }
                }

                string directExe = Path.Combine(discordBaseDir, "Discord.exe");
                if (File.Exists(directExe))
                {
                    var icon = ExtractIconForPath(directExe);
                    if (icon != null) return icon;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public string SteamLargestGameText => SteamStatus.LargestGame != null
        ? $"{SteamStatus.LargestGame.Name} ({SteamStatus.LargestGame.SizeFormatted})"
        : "Nenhum jogo instalado";
    public string SteamTotalStorageText => SteamStatus.TotalStorageFormatted;
    public ImageSource? SteamLargestGameIcon => SteamStatus.LargestGame?.IconImage ?? SteamClientIcon;

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
            OnPropertyChanged(nameof(DiscordMentionCount));
            OnPropertyChanged(nameof(HasDiscordMentions));
            OnPropertyChanged(nameof(DiscordMentionBadgeText));
            OnPropertyChanged(nameof(DiscordChannelName));
            OnPropertyChanged(nameof(DiscordServerName));
            OnPropertyChanged(nameof(DiscordServerInitials));
            OnPropertyChanged(nameof(IsDiscordInVoice));
            OnPropertyChanged(nameof(DiscordVoiceDurationText));
            OnPropertyChanged(nameof(DiscordVoiceStatusText));
            OnPropertyChanged(nameof(IsDiscordVoiceSpeaking));
            OnPropertyChanged(nameof(IsDiscordMuted));
            OnPropertyChanged(nameof(DiscordIsMuted));
            OnPropertyChanged(nameof(IsDiscordDeafened));
            OnPropertyChanged(nameof(DiscordIsDeafened));
            OnPropertyChanged(nameof(DiscordMuteTooltip));
            OnPropertyChanged(nameof(DiscordDeafenTooltip));
            if (value.DiscordClientIcon != null && _discordClientIcon == null)
            {
                _discordClientIcon = value.DiscordClientIcon;
                OnPropertyChanged(nameof(DiscordClientIcon));
                OnPropertyChanged(nameof(HasDiscordClientIcon));
            }
        }
    }

    public bool IsDiscordRunning => DiscordStatus.IsRunning;
    public bool IsDiscordIpcAvailable => DiscordStatus.IsIpcAvailable;
    public string DiscordActivityText => string.IsNullOrWhiteSpace(DiscordStatus.ActivitySummary)
        ? (DiscordStatus.IsRunning ? "Discord Ativo" : "Offline")
        : DiscordStatus.ActivitySummary;
    public string DiscordSubText => DiscordStatus.IsInVoiceCall
        ? $"Em chamada · {DiscordStatus.VoiceCallDurationFormatted}"
        : (DiscordStatus.IsIpcAvailable
            ? "Rich Presence IPC conectado"
            : (DiscordStatus.IsRunning ? "Processo ativo em 2º plano" : "Clique para abrir o Discord"));

    public int DiscordMentionCount => DiscordStatus.MentionCount;
    public bool HasDiscordMentions => DiscordStatus.MentionCount > 0;
    public string DiscordMentionBadgeText => DiscordStatus.MentionCount > 99 ? "99+" : DiscordStatus.MentionCount.ToString();
    public string DiscordChannelName => string.IsNullOrWhiteSpace(DiscordStatus.ChannelName) ? "#geral" : DiscordStatus.ChannelName;
    public string DiscordServerName => string.IsNullOrWhiteSpace(DiscordStatus.ServerName) ? "Discord" : DiscordStatus.ServerName;
    public string DiscordServerInitials => string.IsNullOrWhiteSpace(DiscordStatus.ServerInitials) ? "DC" : DiscordStatus.ServerInitials;
    public bool IsDiscordInVoice => DiscordStatus.IsInVoiceCall;
    public string DiscordVoiceDurationText => DiscordStatus.IsInVoiceCall ? DiscordStatus.VoiceCallDurationFormatted : "00:00";
    public string DiscordVoiceStatusText => DiscordStatus.IsInVoiceCall ? $"Em call · {DiscordStatus.VoiceCallDurationFormatted}" : "Fora de call";
    public bool IsDiscordVoiceSpeaking => DiscordStatus.IsVoiceSpeaking;
    public bool IsDiscordMuted => DiscordStatus.IsMuted;
    public bool DiscordIsMuted => DiscordStatus.IsMuted;
    public bool IsDiscordDeafened => DiscordStatus.IsDeafened;
    public bool DiscordIsDeafened => DiscordStatus.IsDeafened;
    public string DiscordMuteTooltip => DiscordStatus.IsMuted ? "Microfone Mutado (Clique para Desmutar)" : "Microfone Ativo (Clique para Mutar)";
    public string DiscordDeafenTooltip => DiscordStatus.IsDeafened ? "Áudio Ensurdecido (Clique para Ativar)" : "Áudio Ativo (Clique para Ensurdecer)";

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
                SaveAllCustomizationsToDisk();
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
                SaveAllCustomizationsToDisk();
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

        // Live update Discord voice duration if in call
        if (DiscordStatus.IsInVoiceCall)
        {
            OnPropertyChanged(nameof(DiscordVoiceDurationText));
            OnPropertyChanged(nameof(DiscordVoiceStatusText));
        }

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
    /// Preserves custom user order across all 500ms polling ticks.
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

        // Build unified candidate items list
        var pinnedShortcuts = _dockSettingsService.CurrentSettings.PinnedShortcuts;
        var candidates = new List<DockAppItem>();
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
                var item = new DockAppItem(ExecuteDockAppClick, TogglePinApp, MoveDockAppLeft, MoveDockAppRight)
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
                candidates.Add(item);
            }
            else
            {
                // Pinned app is currently closed
                var icon = ExtractIconForPath(pinnedExe);
                var item = new DockAppItem(ExecuteDockAppClick, TogglePinApp, MoveDockAppLeft, MoveDockAppRight)
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
                candidates.Add(item);
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

            var item = new DockAppItem(ExecuteDockAppClick, TogglePinApp, MoveDockAppLeft, MoveDockAppRight)
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
            candidates.Add(item);
        }

        // 3. Preserve custom order across polling ticks:
        if (_customDockOrderKeys.Count == 0 && DockApps.Count > 0)
        {
            foreach (var app in DockApps)
            {
                _customDockOrderKeys.Add(GetDockAppKey(app));
            }
        }

        var candidateByKey = new Dictionary<string, DockAppItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            string key = GetDockAppKey(c);
            if (!candidateByKey.ContainsKey(key))
            {
                candidateByKey[key] = c;
            }
        }

        var targetItems = new List<DockAppItem>();

        // Place all items with an established custom position
        foreach (var key in _customDockOrderKeys)
        {
            if (candidateByKey.TryGetValue(key, out var item))
            {
                targetItems.Add(item);
                candidateByKey.Remove(key);
            }
        }

        // Append any new candidates (newly opened windows or added pins)
        foreach (var remaining in candidateByKey.Values)
        {
            targetItems.Add(remaining);
        }

        // Synchronize into DockApps collection in-place to avoid visual flickering
        SyncDockApps(targetItems);

        // Also update OpenAppsForPinning (for CustomizationWindow 1-click pinning)
        SyncOpenAppsForPinning(windows, pinnedShortcuts);

        // Synchronize unified interleaved dock strip (apps + visible widgets)
        SyncUnifiedDockStrip();
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

    public static string GetDockAppKey(DockAppItem item)
    {
        if (item.IsPinned && !string.IsNullOrWhiteSpace(item.ShortcutId))
        {
            return $"pinned:{item.ShortcutId}";
        }
        if (item.WindowHandle != IntPtr.Zero)
        {
            return $"hwnd:{item.WindowHandle.ToInt64()}";
        }
        if (!string.IsNullOrWhiteSpace(item.ExecutablePath))
        {
            return $"exe:{item.ExecutablePath.ToLowerInvariant()}";
        }
        return $"proc:{item.ProcessName.ToLowerInvariant()}";
    }

    /// <summary>
    /// Requirement (Sprint 6): Unified engine for reordering any dock item (apps or widgets).
    /// </summary>
    public void MoveDockItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= DockStripItems.Count || toIndex < 0 || toIndex >= DockStripItems.Count || fromIndex == toIndex)
        {
            return;
        }

        DockStripItems.Move(fromIndex, toIndex);

        _customDockOrderKeys.Clear();
        foreach (var item in DockStripItems)
        {
            _customDockOrderKeys.Add(item.DockOrderKey);
        }

        SyncPinnedOrderFromDockApps();
        SyncDockWidgetsOrderFromStrip();
        SaveAllCustomizationsToDisk();
    }

    public void MoveDockItemLeft(IDockOrderableItem item)
    {
        if (item == null) return;
        int idx = DockStripItems.IndexOf(item);
        if (idx > 0)
        {
            MoveDockItem(idx, idx - 1);
        }
        else if (item is DockWidgetItem widget)
        {
            int wIdx = DockWidgets.IndexOf(widget);
            if (wIdx > 0)
            {
                DockWidgets.Move(wIdx, wIdx - 1);
                SyncUnifiedDockStrip();
                SaveAllCustomizationsToDisk();
            }
        }
    }

    public void MoveDockItemRight(IDockOrderableItem item)
    {
        if (item == null) return;
        int idx = DockStripItems.IndexOf(item);
        if (idx >= 0 && idx < DockStripItems.Count - 1)
        {
            MoveDockItem(idx, idx + 1);
        }
        else if (item is DockWidgetItem widget)
        {
            int wIdx = DockWidgets.IndexOf(widget);
            if (wIdx >= 0 && wIdx < DockWidgets.Count - 1)
            {
                DockWidgets.Move(wIdx, wIdx + 1);
                SyncUnifiedDockStrip();
                SaveAllCustomizationsToDisk();
            }
        }
    }

    public void MoveDockApp(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= DockApps.Count || toIndex < 0 || toIndex >= DockApps.Count || fromIndex == toIndex)
        {
            return;
        }

        var source = DockApps[fromIndex];
        var target = DockApps[toIndex];
        int sIdx = DockStripItems.IndexOf(source);
        int tIdx = DockStripItems.IndexOf(target);

        if (sIdx >= 0 && tIdx >= 0 && sIdx != tIdx)
        {
            MoveDockItem(sIdx, tIdx);
        }
        else
        {
            DockApps.Move(fromIndex, toIndex);
            SyncUnifiedDockStrip();
        }
    }

    public void MoveDockAppLeft(DockAppItem item) => MoveDockItemLeft(item);
    public void MoveDockAppRight(DockAppItem item) => MoveDockItemRight(item);

    public void SyncUnifiedDockStrip()
    {
        if (_isDisposed)
        {
            return;
        }

        var activeCandidates = new List<IDockOrderableItem>();
        foreach (var app in DockApps)
        {
            activeCandidates.Add(app);
        }
        foreach (var widget in DockWidgets)
        {
            if (widget.IsVisible)
            {
                activeCandidates.Add(widget);
            }
        }

        var candidateByKey = new Dictionary<string, IDockOrderableItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in activeCandidates)
        {
            candidateByKey[c.DockOrderKey] = c;
        }

        if (_customDockOrderKeys.Count == 0)
        {
            if (DockStripItems.Count > 0)
            {
                foreach (var item in DockStripItems)
                {
                    _customDockOrderKeys.Add(item.DockOrderKey);
                }
            }
            else
            {
                foreach (var item in activeCandidates)
                {
                    _customDockOrderKeys.Add(item.DockOrderKey);
                }
            }
        }

        var orderedStrip = new List<IDockOrderableItem>();
        var activeKeys = new List<string>();

        foreach (var key in _customDockOrderKeys)
        {
            if (candidateByKey.TryGetValue(key, out var item))
            {
                orderedStrip.Add(item);
                activeKeys.Add(key);
                candidateByKey.Remove(key);
            }
        }

        foreach (var remaining in candidateByKey.Values)
        {
            orderedStrip.Add(remaining);
            activeKeys.Add(remaining.DockOrderKey);
        }

        _customDockOrderKeys.Clear();
        _customDockOrderKeys.AddRange(activeKeys);

        SyncDockStripItems(orderedStrip);
    }

    private void SyncDockStripItems(List<IDockOrderableItem> target)
    {
        for (int i = 0; i < target.Count; i++)
        {
            var newItem = target[i];

            int existingIndex = -1;
            for (int j = i; j < DockStripItems.Count; j++)
            {
                if (string.Equals(DockStripItems[j].DockOrderKey, newItem.DockOrderKey, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex == i)
            {
                if (!ReferenceEquals(DockStripItems[i], newItem))
                {
                    DockStripItems[i] = newItem;
                }
            }
            else if (existingIndex > i)
            {
                DockStripItems.Move(existingIndex, i);
            }
            else
            {
                DockStripItems.Insert(i, newItem);
            }
        }

        while (DockStripItems.Count > target.Count)
        {
            DockStripItems.RemoveAt(DockStripItems.Count - 1);
        }
    }

    private void SyncDockWidgetsOrderFromStrip()
    {
        var widgetOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int order = 0;
        foreach (var item in DockStripItems)
        {
            if (item is DockWidgetItem w)
            {
                widgetOrder[w.Id] = order++;
            }
        }

        if (widgetOrder.Count > 0)
        {
            var sortedWidgets = DockWidgets
                .OrderBy(w => widgetOrder.TryGetValue(w.Id, out int idx) ? idx : int.MaxValue)
                .ToList();

            for (int i = 0; i < sortedWidgets.Count; i++)
            {
                int curIdx = DockWidgets.IndexOf(sortedWidgets[i]);
                if (curIdx != i && curIdx >= 0)
                {
                    DockWidgets.Move(curIdx, i);
                }
            }
        }
    }

    private void SyncPinnedOrderFromDockApps()
    {
        var currentPinned = _dockSettingsService.CurrentSettings.PinnedShortcuts;
        var pinnedMap = currentPinned.ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);

        var reorderedPinned = new List<DockShortcut>();
        foreach (var app in DockApps)
        {
            if (app.IsPinned && !string.IsNullOrWhiteSpace(app.ShortcutId) && pinnedMap.TryGetValue(app.ShortcutId, out var shortcut))
            {
                reorderedPinned.Add(shortcut);
                pinnedMap.Remove(app.ShortcutId);
            }
        }
        foreach (var remaining in pinnedMap.Values)
        {
            reorderedPinned.Add(remaining);
        }

        currentPinned.Clear();
        currentPinned.AddRange(reorderedPinned);
        _dockSettingsService.SaveSettings();

        SyncPinnedShortcutsFromSettings();
    }

    private void SyncDockApps(List<DockAppItem> target)
    {
        for (int i = 0; i < target.Count; i++)
        {
            var newItem = target[i];

            int existingIndex = -1;
            for (int j = i; j < DockApps.Count; j++)
            {
                if (CanUpdateInPlace(DockApps[j], newItem))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex == i)
            {
                UpdateDockAppProperties(DockApps[i], newItem);
            }
            else if (existingIndex > i)
            {
                DockApps.Move(existingIndex, i);
                UpdateDockAppProperties(DockApps[i], newItem);
            }
            else
            {
                DockApps.Insert(i, newItem);
            }
        }

        while (DockApps.Count > target.Count)
        {
            DockApps.RemoveAt(DockApps.Count - 1);
        }
    }

    private static void UpdateDockAppProperties(DockAppItem existing, DockAppItem newItem)
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

    private IntPtr _lastClickedHwnd = IntPtr.Zero;
    private long _lastClickTicks = 0;

    /// <summary>
    /// Requirement (2): Clicking an open app calls ToggleOrActivateWindow(handle),
    /// and clicking a closed pinned app launches its ExecutablePath!
    /// </summary>
    private void ExecuteDockAppClick(DockAppItem item)
    {
        if (item == null) return;

        if (item.IsOpen && item.WindowHandle != IntPtr.Zero && Win32Interop.IsWindow(item.WindowHandle))
        {
            long now = Environment.TickCount64;
            if (item.WindowHandle == _lastClickedHwnd && (now - _lastClickTicks) < 250)
            {
                return; // Deduplicate simultaneous triggers within 250ms
            }
            _lastClickedHwnd = item.WindowHandle;
            _lastClickTicks = now;

            IntPtr fg = Win32Interop.GetForegroundWindow();
            if (fg == item.WindowHandle && !Win32Interop.IsIconic(item.WindowHandle))
            {
                SafeInvoke(() => _windowTrackerService.MinimizeWindow(item.WindowHandle));
            }
            else
            {
                SafeInvoke(() => _windowTrackerService.ActivateWindow(item.WindowHandle));
            }
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

        string oldKey = GetDockAppKey(item);

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

        string newKey = GetDockAppKey(item);
        int keyIdx = _customDockOrderKeys.IndexOf(oldKey);
        if (keyIdx >= 0)
        {
            _customDockOrderKeys[keyIdx] = newKey;
        }

        SyncPinnedOrderFromDockApps();
        SyncPinnedShortcutsFromSettings();
        RefreshFastState();
        SaveAllCustomizationsToDisk();
    }

    /// <summary>
    /// Requirement (2): Unpin app and save to JSON immediately!
    /// </summary>
    public void UnpinApp(DockAppItem item)
    {
        if (item == null) return;

        string oldKey = GetDockAppKey(item);

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

        if (item.IsOpen && item.WindowHandle != IntPtr.Zero)
        {
            string newKey = GetDockAppKey(item);
            int keyIdx = _customDockOrderKeys.IndexOf(oldKey);
            if (keyIdx >= 0)
            {
                _customDockOrderKeys[keyIdx] = newKey;
            }
        }
        else
        {
            _customDockOrderKeys.Remove(oldKey);
        }

        SyncPinnedOrderFromDockApps();
        SyncPinnedShortcutsFromSettings();
        RefreshFastState();
        SaveAllCustomizationsToDisk();
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
        SaveAllCustomizationsToDisk();
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
        SaveAllCustomizationsToDisk();
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
            SaveAllCustomizationsToDisk();
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
                SaveAllCustomizationsToDisk();
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
                SaveAllCustomizationsToDisk();
            }
        }
    }

    private void ExecuteMoveWidgetUp(object? parameter)
    {
        if (parameter is IDockOrderableItem item)
        {
            MoveDockItemLeft(item);
        }
    }

    private void ExecuteMoveWidgetDown(object? parameter)
    {
        if (parameter is IDockOrderableItem item)
        {
            MoveDockItemRight(item);
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

    private void ExecuteLaunchLastPlayedSteamGame()
    {
        if (SteamStatus.LastPlayedGame is { AppId: > 0 } game)
        {
            SafeInvoke(() => _steamIntegrationService.LaunchGame(game.AppId));
        }
        else
        {
            SafeInvoke(_steamIntegrationService.OpenSteam);
        }
    }

    private void ExecuteLaunchRecentSteamGame(int index)
    {
        if (RecentGames.Count > index && RecentGames[index] is { AppId: > 0 } game)
        {
            SafeInvoke(() => _steamIntegrationService.LaunchGame(game.AppId));
        }
        else
        {
            SafeInvoke(_steamIntegrationService.OpenSteam);
        }
    }

    private void ExecuteOpenSteamProtocol(string protocol)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = protocol,
                UseShellExecute = true
            });
        }
        catch
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
