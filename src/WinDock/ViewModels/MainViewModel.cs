using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using WinDock.Models;

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

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAppBarService _appBarService;
    private readonly INativeTaskbarService _nativeTaskbarService;
    private readonly IWindowTrackerService _windowTrackerService;
    private readonly IHardwareMonitorService _hardwareMonitorService;
    private readonly IMediaIntegrationService _mediaIntegrationService;
    private readonly ISteamIntegrationService _steamIntegrationService;
    private readonly IAiUsageService _aiUsageService;
    private readonly IDiscordStatusService _discordStatusService;

    private readonly DispatcherTimer _fastTimer;
    private readonly DispatcherTimer _telemetryTimer;
    private bool _isRefreshingTelemetry;
    private bool _isDisposed;

    private IntPtr _excludeWindowHandle;
    private BarEdge _currentEdge = BarEdge.Top;
    private bool _areWidgetsVisible = true;

    private string _currentTimeText = DateTime.Now.ToString("HH:mm:ss");
    private string _currentDateText = DateTime.Now.ToString("ddd, dd MMM");
    private string _greetingText = GetGreeting();

    private HardwareMetrics _hardware = new();
    private MediaInfo _currentMedia = new();
    private SteamStatus _steamStatus = new();
    private SteamGameInfo? _selectedSteamGame;
    private AiUsageSnapshot _aiUsage = new();
    private DiscordStatus _discordStatus = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? EdgeChanged;
    public event EventHandler? WidgetsVisibilityChanged;

    public MainViewModel(
        IAppBarService appBarService,
        INativeTaskbarService nativeTaskbarService,
        IWindowTrackerService windowTrackerService,
        IHardwareMonitorService hardwareMonitorService,
        IMediaIntegrationService mediaIntegrationService,
        ISteamIntegrationService steamIntegrationService,
        IAiUsageService aiUsageService,
        IDiscordStatusService discordStatusService)
    {
        _appBarService = appBarService;
        _nativeTaskbarService = nativeTaskbarService;
        _windowTrackerService = windowTrackerService;
        _hardwareMonitorService = hardwareMonitorService;
        _mediaIntegrationService = mediaIntegrationService;
        _steamIntegrationService = steamIntegrationService;
        _aiUsageService = aiUsageService;
        _discordStatusService = discordStatusService;

        OpenWindows = new ObservableCollection<AppWindowInfo>();
        InstalledGames = new ObservableCollection<SteamGameInfo>();
        AiProviders = new ObservableCollection<AiProviderUsage>();

        WindowClickCommand = new RelayCommand(ExecuteWindowClick);
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

        _fastTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(800)
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

    public ObservableCollection<AppWindowInfo> OpenWindows { get; }
    public ObservableCollection<SteamGameInfo> InstalledGames { get; }
    public ObservableCollection<AiProviderUsage> AiProviders { get; }

    public IntPtr ExcludeWindowHandle
    {
        get => _excludeWindowHandle;
        set
        {
            if (_excludeWindowHandle != value)
            {
                _excludeWindowHandle = value;
                OnPropertyChanged();
                RefreshOpenWindows();
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
                EdgeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string EdgeLabel => CurrentEdge == BarEdge.Top ? "Topo" : "Base";

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

    public string WidgetsToggleText => AreWidgetsVisible ? "Widgets: ON" : "Widgets: OFF";

    public bool IsNativeTaskbarHidden => _nativeTaskbarService.IsNativeTaskbarHidden;

    public string NativeTaskbarStatusText => IsNativeTaskbarHidden ? "WinBar: Oculta" : "WinBar: Visível";

    public string CurrentTimeText
    {
        get => _currentTimeText;
        private set => SetField(ref _currentTimeText, value);
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
        : $"{InstalledGames.Count} jogo(s) detectado(s) localmente";

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
        : (DiscordStatus.IsRunning ? "Processo ativo em segundo plano" : "Clique para iniciar o Discord");

    public ICommand WindowClickCommand { get; }
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

    private void RefreshFastState()
    {
        if (_isDisposed)
        {
            return;
        }

        var now = DateTime.Now;
        CurrentTimeText = now.ToString("HH:mm:ss");
        CurrentDateText = now.ToString("ddd, dd MMM");
        GreetingText = GetGreeting();

        RefreshOpenWindows();
    }

    public void RefreshOpenWindows()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            var windows = _windowTrackerService.GetOpenWindows(_excludeWindowHandle);
            SyncOpenWindows(windows);
        }
        catch
        {
            // Keep existing window list if enumeration transiently fails
        }
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

    private static bool AreSameWindowState(AppWindowInfo a, AppWindowInfo b)
    {
        return a.Handle == b.Handle
               && string.Equals(a.Title, b.Title, StringComparison.Ordinal)
               && a.IsFocused == b.IsFocused
               && a.IsMinimized == b.IsMinimized
               && string.Equals(a.ProcessName, b.ProcessName, StringComparison.Ordinal);
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

    private void ExecuteWindowClick(object? parameter)
    {
        if (parameter is AppWindowInfo windowInfo && windowInfo.Handle != IntPtr.Zero)
        {
            SafeInvoke(() => _windowTrackerService.ToggleOrActivateWindow(windowInfo.Handle));
            RefreshOpenWindows();
        }
        else if (parameter is IntPtr handle && handle != IntPtr.Zero)
        {
            SafeInvoke(() => _windowTrackerService.ToggleOrActivateWindow(handle));
            RefreshOpenWindows();
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
        System.Windows.Application.Current?.Shutdown();
    }

    private static void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Prevent external integration failures from crashing UI commands
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
