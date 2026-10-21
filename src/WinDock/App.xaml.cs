using System;
using System.Windows;
using WinDock.Models;
using WinDock.Services;
using WinDock.ViewModels;
using WinDock.Views;

namespace WinDock;

public partial class App : System.Windows.Application
{
    private INativeTaskbarService? _nativeTaskbarService;
    private IAppBarService? _appBarService;
    private TaskbarWindow? _taskbarWindow;
    private DesktopWidgetsWindow? _widgetsWindow;
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _nativeTaskbarService = new NativeTaskbarService();
        _appBarService = new AppBarService();

        var windowTracker = new WindowTrackerService();
        var hardwareMonitor = new HardwareMonitorService();
        var mediaService = new MediaIntegrationService();
        var steamService = new SteamIntegrationService();
        var aiUsageService = new AiUsageService();
        var discordService = new DiscordStatusService();

        // Ensure native taskbar is always restored if process exits unexpectedly
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeRestoreTaskbar();
        DispatcherUnhandledException += (_, args) =>
        {
            SafeRestoreTaskbar();
            args.Handled = true;
        };

        _mainViewModel = new MainViewModel(
            _appBarService,
            _nativeTaskbarService,
            windowTracker,
            hardwareMonitor,
            mediaService,
            steamService,
            aiUsageService,
            discordService);

        _widgetsWindow = new DesktopWidgetsWindow(_mainViewModel, _appBarService);
        _widgetsWindow.Show();

        _taskbarWindow = new TaskbarWindow(_mainViewModel, _appBarService, _widgetsWindow);
        _taskbarWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SafeRestoreTaskbar();
        _mainViewModel?.Dispose();
        base.OnExit(e);
    }

    private void SafeRestoreTaskbar()
    {
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
