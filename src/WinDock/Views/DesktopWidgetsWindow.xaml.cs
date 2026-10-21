using System;
using System.Windows;
using System.Windows.Input;
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
        RepositionForBarEdge(_viewModel.CurrentEdge, _viewModel.BarHeightDip);
        PinToDesktopLayer();
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
