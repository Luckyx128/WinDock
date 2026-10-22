using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using WinDock.Models;
using WinDock.Services;
using WinDock.ViewModels;
using WinDock.Views;

namespace WinDock;

public partial class App : System.Windows.Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinDock",
        "crash.log");

    public static void LogDiagnostic(string message)
    {
        try
        {
            string? dir = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(CrashLogPath, entry);
        }
        catch { }
    }

    private INativeTaskbarService? _nativeTaskbarService;
    private IAppBarService? _appBarService;
    private TaskbarWindow? _taskbarWindow;
    private DesktopWidgetsWindow? _widgetsWindow;
    private CustomizationWindow? _customizationWindow;
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LogDiagnostic("[OnStartup] WinDock starting up.");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogDiagnostic($"[AppDomain.UnhandledException] IsTerminating={args.IsTerminating}: {args.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogDiagnostic($"[TaskScheduler.UnobservedTaskException] {args.Exception}");
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            LogDiagnostic($"[DispatcherUnhandledException] {args.Exception}");
            SafeRestoreTaskbar();
            args.Handled = true;
        };

        _nativeTaskbarService = new NativeTaskbarService();
        _appBarService = new AppBarService();

        var windowTracker = new WindowTrackerService();
        var hardwareMonitor = new HardwareMonitorService();
        var mediaService = new MediaIntegrationService();
        var steamService = new SteamIntegrationService();
        var aiUsageService = new AiUsageService();
        var discordService = new DiscordStatusService();
        var dockSettingsService = new DockSettingsService();

        // Ensure native taskbar is always restored if process exits unexpectedly
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeRestoreTaskbar();

        // Automatically hide the native Windows taskbar on startup so WinDock takes its exact place
        try
        {
            _nativeTaskbarService.HideNativeTaskbar();
        }
        catch
        {
            // Ignore handle issues on early startup
        }

        _mainViewModel = new MainViewModel(
            _appBarService,
            _nativeTaskbarService,
            windowTracker,
            hardwareMonitor,
            mediaService,
            steamService,
            aiUsageService,
            discordService,
            dockSettingsService);

        // Listen for user opening customization settings
        _mainViewModel.OpenCustomizationRequested += OnOpenCustomizationRequested;

        _widgetsWindow = new DesktopWidgetsWindow(_mainViewModel, _appBarService);
        // Clean startup: desktop widgets window starts hidden if AreWidgetsVisible is false
        if (_mainViewModel.AreWidgetsVisible)
        {
            _widgetsWindow.Show();
        }

        _taskbarWindow = new TaskbarWindow(_mainViewModel, _appBarService, _widgetsWindow);
        _taskbarWindow.Show();
    }

    private void OnOpenCustomizationRequested()
    {
        if (_mainViewModel == null) return;

        if (_customizationWindow == null || !_customizationWindow.IsLoaded)
        {
            _customizationWindow = new CustomizationWindow(_mainViewModel);
            _customizationWindow.Closed += (_, _) => _customizationWindow = null;
            _customizationWindow.Show();
        }
        else
        {
            if (_customizationWindow.WindowState == WindowState.Minimized)
            {
                _customizationWindow.WindowState = WindowState.Normal;
            }
            _customizationWindow.Activate();
            _customizationWindow.Focus();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogDiagnostic("[OnExit] ExitCode: " + e.ApplicationExitCode);
        SafeRestoreTaskbar();
        _mainViewModel?.Dispose();
        base.OnExit(e);
    }

    private void SafeRestoreTaskbar()
    {
        LogDiagnostic("[SafeRestoreTaskbar] Stack: " + Environment.StackTrace);
        try
        {
            if (_taskbarWindow != null && _appBarService != null)
            {
                _appBarService.UnregisterBar(_taskbarWindow);
            }
            _nativeTaskbarService?.ShowNativeTaskbar();
        }
        catch
        {
            // Ignore cleanup errors on exit
        }
    }
}
