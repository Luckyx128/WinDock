using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinDock.Models;
using WinDock.ViewModels;
using Point = System.Windows.Point;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace WinDock.Views;

public partial class TaskbarWindow : Window
{
    public const double BarHeightDip = 48.0;

    private readonly MainViewModel _viewModel;
    private readonly IAppBarService _appBarService;
    private readonly DesktopWidgetsWindow _widgetsWindow;
    private bool _isBarRegistered;

    private Point _dragStartPoint;
    private DockAppItem? _draggedItem;
    private Button? _draggedButton;
    private bool _isDraggingDockApp;

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
        Deactivated += (_, _) => ResetDockAppDrag();

        _viewModel.EdgeChanged += OnEdgeChanged;
        _viewModel.WidgetsVisibilityChanged += OnWidgetsVisibilityChanged;
        _viewModel.FullscreenStateChanged += OnFullscreenStateChanged;
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

    /// <summary>
    /// Requirement (3): FULLSCREEN AUTO-HIDE FIX.
    /// When true (e.g. fullscreen game or fullscreen YouTube video), hide TaskbarWindow (Topmost = false; Visibility = Visibility.Collapsed)
    /// so it NEVER stays on top of fullscreen games or videos!
    /// When false, restore TaskbarWindow (Visibility = Visibility.Visible; Topmost = true).
    /// </summary>
    private void OnFullscreenStateChanged(object? sender, bool isFullscreen)
    {
        if (isFullscreen)
        {
            if (Visibility != Visibility.Collapsed)
            {
                Topmost = false;
                Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
                Topmost = true;
            }
        }
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

    private void DockAppButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.DataContext is DockAppItem item)
        {
            _dragStartPoint = e.GetPosition(this);
            _draggedItem = item;
            _draggedButton = button;
            _isDraggingDockApp = false;
        }
    }

    private void DockAppButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedItem == null || _draggedButton == null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDockAppDrag();
            return;
        }

        Point currentPoint = e.GetPosition(this);
        double diffX = Math.Abs(currentPoint.X - _dragStartPoint.X);
        double diffY = Math.Abs(currentPoint.Y - _dragStartPoint.Y);

        if (!_isDraggingDockApp)
        {
            if (diffX > SystemParameters.MinimumHorizontalDragDistance || diffY > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDraggingDockApp = true;
                _draggedItem.IsDragging = true;
                _draggedButton.CaptureMouse();
            }
        }

        if (_isDraggingDockApp)
        {
            // Hit-test DockAppsItemsControl to find if cursor is over another DockAppItem
            if (DockAppsItemsControl != null)
            {
                Point ptInItems = e.GetPosition(DockAppsItemsControl);
                HitTestResult hitResult = VisualTreeHelper.HitTest(DockAppsItemsControl, ptInItems);
                if (hitResult?.VisualHit != null)
                {
                    DependencyObject? current = hitResult.VisualHit;
                    while (current != null && current != DockAppsItemsControl)
                    {
                        if (current is FrameworkElement fe && fe.DataContext is DockAppItem targetItem)
                        {
                            if (!ReferenceEquals(targetItem, _draggedItem))
                            {
                                int fromIdx = _viewModel.DockApps.IndexOf(_draggedItem);
                                int toIdx = _viewModel.DockApps.IndexOf(targetItem);
                                if (fromIdx >= 0 && toIdx >= 0 && fromIdx != toIdx)
                                {
                                    _viewModel.MoveDockApp(fromIdx, toIdx);
                                }
                            }
                            break;
                        }
                        current = VisualTreeHelper.GetParent(current);
                    }
                }
            }

            // Auto-scroll DockScrollViewer if dragging near edges
            if (DockScrollViewer != null)
            {
                Point ptInScroll = e.GetPosition(DockScrollViewer);
                if (ptInScroll.X < 25)
                {
                    DockScrollViewer.ScrollToHorizontalOffset(Math.Max(0, DockScrollViewer.HorizontalOffset - 12));
                }
                else if (ptInScroll.X > DockScrollViewer.ActualWidth - 25)
                {
                    DockScrollViewer.ScrollToHorizontalOffset(DockScrollViewer.HorizontalOffset + 12);
                }
            }
        }
    }

    private void DockAppButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingDockApp)
        {
            // Drag operation finished: swallow event so Button.Click is NOT executed
            e.Handled = true;
            ResetDockAppDrag();
        }
        else
        {
            ResetDockAppDrag();
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_isDraggingDockApp)
        {
            e.Handled = true;
            ResetDockAppDrag();
            return;
        }
        base.OnPreviewMouseLeftButtonUp(e);
    }

    private void ResetDockAppDrag()
    {
        if (_draggedItem != null)
        {
            _draggedItem.IsDragging = false;
        }

        if (_draggedButton != null && _draggedButton.IsMouseCaptured)
        {
            _draggedButton.ReleaseMouseCapture();
        }

        _draggedItem = null;
        _draggedButton = null;
        _isDraggingDockApp = false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.EdgeChanged -= OnEdgeChanged;
        _viewModel.WidgetsVisibilityChanged -= OnWidgetsVisibilityChanged;
        _viewModel.FullscreenStateChanged -= OnFullscreenStateChanged;

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
