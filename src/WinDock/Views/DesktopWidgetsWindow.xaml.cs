using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WinDock.Interop;
using WinDock.Models;
using WinDock.ViewModels;

namespace WinDock.Views;

public partial class DesktopWidgetsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IAppBarService _appBarService;
    private bool _hasUserDraggedPosition;

    public DesktopWidgetsWindow(MainViewModel viewModel, IAppBarService appBarService)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _appBarService = appBarService ?? throw new ArgumentNullException(nameof(appBarService));

        DataContext = _viewModel;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Activated += OnActivated;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle != IntPtr.Zero ? helper.Handle : helper.EnsureHandle();

        // 1. Establish Desktop/Progman as Owner so Win + D recognizes widgets as part of Desktop
        IntPtr desktopHwnd = Win32Interop.FindDesktopHwnd();
        if (desktopHwnd != IntPtr.Zero)
        {
            try
            {
                helper.Owner = desktopHwnd;
            }
            catch
            {
                // Fallback to SetWindowLongPtr below
            }

            Win32Interop.SetWindowLongPtr(hwnd, Win32Interop.GWLP_HWNDPARENT, desktopHwnd);
        }

        // 2. Install HwndSourceHook to block Win+D minimize and hide commands
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(DesktopHook);

        RepositionForBarEdge(_viewModel.CurrentEdge, _viewModel.BarHeightDip);
        PinToDesktopLayer();
    }

    private IntPtr DesktopHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32Interop.WM_SYSCOMMAND:
                // Intercept SC_MINIMIZE command (triggered by Win+D or other shell actions)
                if ((wParam.ToInt32() & 0xFFF0) == Win32Interop.SC_MINIMIZE)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                break;

            case Win32Interop.WM_SHOWWINDOW:
                // If wParam == IntPtr.Zero, Explorer is attempting to hide windows during Show Desktop (Win + D).
                // Block the hide command while widgets are enabled so they stay visible on the wallpaper.
                if (wParam == IntPtr.Zero && _viewModel.AreWidgetsVisible)
                {
                    handled = true;
                    // Re-enforce HWND_BOTTOM so widgets remain right behind normal app windows
                    Win32Interop.SetWindowPos(
                        hwnd,
                        Win32Interop.HWND_BOTTOM,
                        0,
                        0,
                        0,
                        0,
                        Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE);
                    return IntPtr.Zero;
                }
                break;
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RepositionForBarEdge(_viewModel.CurrentEdge, _viewModel.BarHeightDip);
        PinToDesktopLayer();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        // Keep desktop widgets pinned behind normal application windows
        PinToDesktopLayer();
    }

    public void PinToDesktopLayer()
    {
        try
        {
            _appBarService.PinToDesktop(this);
        }
        catch
        {
            // Ignore pin errors during early initialization
        }
    }

    public void RepositionForBarEdge(BarEdge edge, double barHeightDip)
    {
        if (_hasUserDraggedPosition)
        {
            PinToDesktopLayer();
            return;
        }

        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        const double rightMargin = 16.0;
        const double verticalMargin = 14.0;

        Left = Math.Max(0, screenWidth - Width - rightMargin);
        Top = edge == BarEdge.Top
            ? barHeightDip + verticalMargin
            : verticalMargin;

        Height = Math.Min(740.0, Math.Max(480.0, screenHeight - barHeightDip - (verticalMargin * 2)));

        PinToDesktopLayer();
    }

    private void HeaderCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                _hasUserDraggedPosition = true;
                DragMove();
                PinToDesktopLayer();
            }
            catch
            {
                // Ignore DragMove exceptions if mouse button was released quickly
            }
        }
    }
}
