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
    public const double DefaultBarHeightDip = 48.0;
    public double BarHeightDip => _viewModel?.BarHeightDip ?? DefaultBarHeightDip;

    private readonly MainViewModel _viewModel;
    private readonly IAppBarService _appBarService;
    private readonly DesktopWidgetsWindow _widgetsWindow;
    private bool _isBarRegistered;

    private Point _dragStartPoint;
    private IDockOrderableItem? _draggedItem;
    private FrameworkElement? _draggedElement;
    private bool _isDraggingDockItem;

    private bool _isResizingBar;
    private double _resizeStartScreenY;
    private double _resizeStartHeight;

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
        Deactivated += (_, _) => ResetDockItemDrag();

        _viewModel.EdgeChanged += OnEdgeChanged;
        _viewModel.BarHeightChanged += OnBarHeightChanged;
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
        _widgetsWindow.RepositionForBarEdge(_viewModel.CurrentEdge, _viewModel.BarHeightDip);
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
            Height = _viewModel.BarHeightDip;
            if (!_isBarRegistered)
            {
                _appBarService.RegisterBar(this, _viewModel.BarHeightDip, _viewModel.CurrentEdge);
                _isBarRegistered = true;
            }
            else
            {
                _appBarService.UpdateBarPosition(this, _viewModel.BarHeightDip, _viewModel.CurrentEdge);
            }
        }
        catch
        {
            // Fallback positioning if AppBar registration fails
            Width = SystemParameters.PrimaryScreenWidth;
            Height = _viewModel.BarHeightDip;
            Left = 0;
            Top = _viewModel.CurrentEdge == BarEdge.Top
                ? 0
                : Math.Max(0, SystemParameters.PrimaryScreenHeight - _viewModel.BarHeightDip);
        }
    }

    private void OnEdgeChanged(object? sender, EventArgs e)
    {
        try
        {
            _appBarService.UpdateBarPosition(this, _viewModel.BarHeightDip, _viewModel.CurrentEdge);
        }
        catch
        {
            Top = _viewModel.CurrentEdge == BarEdge.Top
                ? 0
                : Math.Max(0, SystemParameters.PrimaryScreenHeight - _viewModel.BarHeightDip);
        }

        _widgetsWindow.RepositionForBarEdge(_viewModel.CurrentEdge, _viewModel.BarHeightDip);
    }

    private void OnBarHeightChanged(object? sender, double newHeight)
    {
        try
        {
            Height = newHeight;
            _appBarService.UpdateBarPosition(this, newHeight, _viewModel.CurrentEdge);
            _widgetsWindow.RepositionForBarEdge(_viewModel.CurrentEdge, newHeight);
        }
        catch
        {
            Height = newHeight;
            Top = _viewModel.CurrentEdge == BarEdge.Top
                ? 0
                : Math.Max(0, SystemParameters.PrimaryScreenHeight - newHeight);
        }
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

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double step = e.Delta > 0 ? 2.0 : -2.0;
            _viewModel.BarHeightDip += step;
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseWheel(e);
    }

    private void DockScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double step = e.Delta > 0 ? 2.0 : -2.0;
            _viewModel.BarHeightDip += step;
            e.Handled = true;
            return;
        }

        if (DockScrollViewer is null)
        {
            return;
        }

        double offset = DockScrollViewer.HorizontalOffset - (e.Delta * 0.5);
        DockScrollViewer.ScrollToHorizontalOffset(Math.Max(0, offset));
        e.Handled = true;
    }

    private void DockItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is IDockOrderableItem item)
        {
            _dragStartPoint = e.GetPosition(this);
            _draggedItem = item;
            _draggedElement = element;
            _isDraggingDockItem = false;
        }
    }

    private void DockItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedItem == null || _draggedElement == null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDockItemDrag();
            return;
        }

        Point currentPoint = e.GetPosition(this);
        double diffX = Math.Abs(currentPoint.X - _dragStartPoint.X);
        double diffY = Math.Abs(currentPoint.Y - _dragStartPoint.Y);

        if (!_isDraggingDockItem)
        {
            if (diffX > SystemParameters.MinimumHorizontalDragDistance || diffY > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDraggingDockItem = true;
                _draggedItem.IsDragging = true;
                _draggedElement.CaptureMouse();
            }
        }

        if (_isDraggingDockItem)
        {
            // Hit-test UnifiedDockItemsControl to find if cursor is over another IDockOrderableItem (app or widget)
            if (UnifiedDockItemsControl != null)
            {
                Point ptInItems = e.GetPosition(UnifiedDockItemsControl);
                HitTestResult hitResult = VisualTreeHelper.HitTest(UnifiedDockItemsControl, ptInItems);
                if (hitResult?.VisualHit != null)
                {
                    DependencyObject? current = hitResult.VisualHit;
                    while (current != null && current != UnifiedDockItemsControl)
                    {
                        if (current is FrameworkElement fe && fe.DataContext is IDockOrderableItem targetItem)
                        {
                            if (!ReferenceEquals(targetItem, _draggedItem))
                            {
                                int fromIdx = _viewModel.DockStripItems.IndexOf(_draggedItem);
                                int toIdx = _viewModel.DockStripItems.IndexOf(targetItem);
                                if (fromIdx >= 0 && toIdx >= 0 && fromIdx != toIdx)
                                {
                                    _viewModel.MoveDockItem(fromIdx, toIdx);
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

    private void DockItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingDockItem)
        {
            // Drag operation finished: swallow event so click is not triggered
            e.Handled = true;
            ResetDockItemDrag();
        }
        else
        {
            ResetDockItemDrag();
            if (sender is FrameworkElement fe && fe.DataContext is DockAppItem app)
            {
                App.LogDiagnostic($"[PreviewMouseUp] Executing ClickCommand for app: {app.Title}");
                app.ClickCommand?.Execute(null);
            }
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_isDraggingDockItem)
        {
            e.Handled = true;
            ResetDockItemDrag();
            return;
        }
        base.OnPreviewMouseLeftButtonUp(e);
    }

    private void ResetDockItemDrag()
    {
        if (_draggedItem != null)
        {
            _draggedItem.IsDragging = false;
        }

        if (_isDraggingDockItem && _draggedElement != null && _draggedElement.IsMouseCaptured)
        {
            _draggedElement.ReleaseMouseCapture();
        }

        _draggedItem = null;
        _draggedElement = null;
        _isDraggingDockItem = false;
    }

    public void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement grip)
        {
            _isResizingBar = true;
            _resizeStartScreenY = PointToScreen(e.GetPosition(this)).Y;
            _resizeStartHeight = _viewModel.BarHeightDip;
            grip.CaptureMouse();
            e.Handled = true;
        }
    }

    public void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizingBar || sender is not FrameworkElement grip || !grip.IsMouseCaptured)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            grip.ReleaseMouseCapture();
            _isResizingBar = false;
            return;
        }

        double currentScreenY = PointToScreen(e.GetPosition(this)).Y;
        double deltaPixels = currentScreenY - _resizeStartScreenY;
        double dpiScaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
        double deltaDip = deltaPixels / (dpiScaleY > 0 ? dpiScaleY : 1.0);

        // If bar is at the bottom, dragging UP (negative delta) increases height!
        // If bar is at the top, dragging DOWN (positive delta) increases height!
        double newHeight = _viewModel.CurrentEdge == BarEdge.Bottom
            ? _resizeStartHeight - deltaDip
            : _resizeStartHeight + deltaDip;

        _viewModel.BarHeightDip = newHeight;
        e.Handled = true;
    }

    public void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isResizingBar)
        {
            if (sender is FrameworkElement grip && grip.IsMouseCaptured)
            {
                grip.ReleaseMouseCapture();
            }
            _isResizingBar = false;
            e.Handled = true;
        }
    }

    private void DockAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDraggingDockItem) return;

        if (sender is FrameworkElement fe && fe.DataContext is DockAppItem app)
        {
            app.ClickCommand?.Execute(null);
        }
    }

    // Backward-compatibility forwarders
    private void DockAppButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DockItem_PreviewMouseLeftButtonDown(sender, e);
    private void DockAppButton_PreviewMouseMove(object sender, MouseEventArgs e) => DockItem_PreviewMouseMove(sender, e);
    private void DockAppButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => DockItem_PreviewMouseLeftButtonUp(sender, e);
    private void ResetDockAppDrag() => ResetDockItemDrag();

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.EdgeChanged -= OnEdgeChanged;
        _viewModel.BarHeightChanged -= OnBarHeightChanged;
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
