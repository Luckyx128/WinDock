using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WinDock.Models;
using WinDock.ViewModels;

namespace WinDock.Views;

public partial class TaskbarWindow : Window
{
    public const double BarHeightDip = 48.0;

    private readonly MainViewModel _viewModel;
    private readonly IAppBarService _appBarService;
    private readonly DesktopWidgetsWindow _widgetsWindow;
    private bool _isBarRegistered;

    public TaskbarWindow(
        MainViewModel viewModel,
        IAppBarService appBarService,
        DesktopWidgetsWindow widgetsWindow)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _appBarService = appBarService ?? throw new ArgumentNullException(nameof(appBarService));
        _widgetsWindow = widgetsWindow ?? throw new ArgumentNullException(nameof(widgetsWindow));

        DataContext = _viewModel;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;

        _viewModel.EdgeChanged += OnEdgeChanged;
        _viewModel.WidgetsVisibilityChanged += OnWidgetsVisibilityChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _viewModel.ExcludeWindowHandle = handle;

        RegisterOrUpdateBar();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RegisterOrUpdateBar();
        _widgetsWindow.RepositionForBarEdge(_viewModel.CurrentEdge, BarHeightDip);
    }

    private void RegisterOrUpdateBar()
    {
        try
        {
            if (!_isBarRegistered)
            {
                _appBarService.RegisterBar(this, BarHeightDip, _viewModel.CurrentEdge);
                _isBarRegistered = true;
            }
            else
            {
                _appBarService.UpdateBarPosition(this, BarHeightDip, _viewModel.CurrentEdge);
            }
        }
        catch
        {
            // Fallback positioning if AppBar registration fails
            Width = SystemParameters.PrimaryScreenWidth;
            Height = BarHeightDip;
            Left = 0;
            Top = _viewModel.CurrentEdge == BarEdge.Top
                ? 0
                : Math.Max(0, SystemParameters.PrimaryScreenHeight - BarHeightDip);
        }
    }

    private void OnEdgeChanged(object? sender, EventArgs e)
    {
        try
        {
            _appBarService.UpdateBarPosition(this, BarHeightDip, _viewModel.CurrentEdge);
        }
        catch
        {
            Top = _viewModel.CurrentEdge == BarEdge.Top
                ? 0
                : Math.Max(0, SystemParameters.PrimaryScreenHeight - BarHeightDip);
        }

        _widgetsWindow.RepositionForBarEdge(_viewModel.CurrentEdge, BarHeightDip);
    }

    private void OnWidgetsVisibilityChanged(object? sender, EventArgs e)
    {
        if (_viewModel.AreWidgetsVisible)
        {
            _widgetsWindow.Show();
            _widgetsWindow.PinToDesktopLayer();
        }
        else
        {
            _widgetsWindow.Hide();
        }
    }

    private void DockScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DockScrollViewer is null)
        {
            return;
        }

        double offset = DockScrollViewer.HorizontalOffset - (e.Delta * 0.5);
        DockScrollViewer.ScrollToHorizontalOffset(Math.Max(0, offset));
        e.Handled = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.EdgeChanged -= OnEdgeChanged;
        _viewModel.WidgetsVisibilityChanged -= OnWidgetsVisibilityChanged;

        if (_isBarRegistered)
        {
            try
            {
                _appBarService.UnregisterBar(this);
            }
            catch
            {
                // Ignore cleanup failure
            }

            _isBarRegistered = false;
        }

        if (_widgetsWindow.IsLoaded)
        {
            _widgetsWindow.Close();
        }
    }
}