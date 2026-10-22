using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinDock.Interop;
using WinDock.Models;

namespace WinDock.Services;

public sealed class AppBarService : IAppBarService
{
    private sealed class AppBarRegistration
    {
        public required Window Window { get; init; }
        public required uint CallbackMessageId { get; init; }
        public HwndSourceHook? Hook { get; set; }
        public DependencyPropertyChangedEventHandler? VisibilityChangedHandler { get; set; }
        public double HeightDip { get; set; }
        public BarEdge Edge { get; set; }
        public bool IsUpdating { get; set; }
        public bool IsFullscreenActive { get; set; }
    }

    private readonly Dictionary<IntPtr, AppBarRegistration> _registeredBars = new();

    public void RegisterBar(Window window, double heightDip, BarEdge edge = BarEdge.Bottom)
    {
        ArgumentNullException.ThrowIfNull(window);

        var helper = new WindowInteropHelper(window);
        IntPtr hwnd = helper.Handle != IntPtr.Zero ? helper.Handle : helper.EnsureHandle();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Apply WS_EX_TOOLWINDOW so the AppBar is excluded from Alt+Tab
        int exStyle = Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE);
        exStyle |= Win32Interop.WS_EX_TOOLWINDOW;
        exStyle &= ~Win32Interop.WS_EX_APPWINDOW;
        Win32Interop.SetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE, exStyle);

        if (!_registeredBars.TryGetValue(hwnd, out var registration))
        {
            uint callbackId = Win32Interop.RegisterWindowMessage($"WinDock_AppBar_{hwnd}");

            var abd = new Win32Interop.APPBARDATA
            {
                cbSize = Marshal.SizeOf<Win32Interop.APPBARDATA>(),
                hWnd = hwnd,
                uCallbackMessage = callbackId
            };

            Win32Interop.SHAppBarMessage(Win32Interop.ABM_NEW, ref abd);

            registration = new AppBarRegistration
            {
                Window = window,
                CallbackMessageId = callbackId,
                HeightDip = heightDip,
                Edge = edge
            };

            DependencyPropertyChangedEventHandler onVisibilityChanged = (_, _) =>
            {
                if (!_registeredBars.TryGetValue(hwnd, out var reg))
                {
                    return;
                }

                if (window.IsVisible && window.Visibility == Visibility.Visible)
                {
                    UpdateBarPosition(window, reg.HeightDip, reg.Edge);
                }
                else if (!reg.IsFullscreenActive)
                {
                    // Cleanly clear work area reservation so hidden dock never blocks other apps
                    uint uEdge = reg.Edge == BarEdge.Bottom ? Win32Interop.ABE_BOTTOM : Win32Interop.ABE_TOP;
                    var abdHidden = new Win32Interop.APPBARDATA
                    {
                        cbSize = Marshal.SizeOf<Win32Interop.APPBARDATA>(),
                        hWnd = hwnd,
                        uEdge = uEdge,
                        rc = new Win32Interop.RECT { left = 0, top = 0, right = 0, bottom = 0 }
                    };
                    Win32Interop.SHAppBarMessage(Win32Interop.ABM_SETPOS, ref abdHidden);

                    Win32Interop.SetWindowPos(
                        hwnd,
                        Win32Interop.HWND_NOTOPMOST,
                        0, 0, 0, 0,
                        Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE | Win32Interop.SWP_HIDEWINDOW);
                    Win32Interop.ShowWindow(hwnd, Win32Interop.SW_HIDE);
                }
            };

            registration.VisibilityChangedHandler = onVisibilityChanged;
            window.IsVisibleChanged += onVisibilityChanged;

            var source = HwndSource.FromHwnd(hwnd);
            if (source != null)
            {
                HwndSourceHook hook = (IntPtr _, int msg, IntPtr wParam, IntPtr lParam, ref bool _) =>
                {
                    if ((uint)msg == registration.CallbackMessageId)
                    {
                        switch (wParam.ToInt32())
                        {
                            case Win32Interop.ABN_POSCHANGED:
                                if (!registration.IsUpdating && !registration.IsFullscreenActive && registration.Window.IsVisible)
                                {
                                    UpdateBarPosition(registration.Window, registration.HeightDip, registration.Edge);
                                }
                                break;

                            case Win32Interop.ABN_FULLSCREENAPP:
                                bool isFullscreen = lParam != IntPtr.Zero;
                                HandleFullscreenApp(registration.Window, isFullscreen);
                                break;
                        }
                    }
                    return IntPtr.Zero;
                };

                registration.Hook = hook;
                source.AddHook(hook);
            }

            _registeredBars[hwnd] = registration;
        }
        else
        {
            registration.HeightDip = heightDip;
            registration.Edge = edge;
        }

        // Only position and reserve workarea if the window is currently visible and not in fullscreen
        if (window.IsVisible && window.Visibility == Visibility.Visible && !registration.IsFullscreenActive)
        {
            UpdateBarPosition(window, heightDip, edge);
        }
    }

    public void UpdateBarPosition(Window window, double heightDip, BarEdge edge = BarEdge.Bottom)
    {
        ArgumentNullException.ThrowIfNull(window);

        var helper = new WindowInteropHelper(window);
        IntPtr hwnd = helper.Handle != IntPtr.Zero ? helper.Handle : helper.EnsureHandle();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_registeredBars.TryGetValue(hwnd, out var registration))
        {
            if (registration.IsUpdating)
            {
                return;
            }
            registration.HeightDip = heightDip;
            registration.Edge = edge;
            registration.IsUpdating = true;
        }

        try
        {
            uint uEdge = edge == BarEdge.Bottom ? Win32Interop.ABE_BOTTOM : Win32Interop.ABE_TOP;

            // If the window is hidden, do NOT reserve space with ABM_SETPOS and do NOT show window!
            if (!window.IsVisible || window.Visibility != Visibility.Visible)
            {
                if (registration?.IsFullscreenActive != true)
                {
                    var abdHidden = new Win32Interop.APPBARDATA
                    {
                        cbSize = Marshal.SizeOf<Win32Interop.APPBARDATA>(),
                        hWnd = hwnd,
                        uEdge = uEdge,
                        rc = new Win32Interop.RECT { left = 0, top = 0, right = 0, bottom = 0 }
                    };
                    Win32Interop.SHAppBarMessage(Win32Interop.ABM_SETPOS, ref abdHidden);
                }

                Win32Interop.SetWindowPos(
                    hwnd,
                    Win32Interop.HWND_NOTOPMOST,
                    0, 0, 0, 0,
                    Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE | Win32Interop.SWP_HIDEWINDOW);
                Win32Interop.ShowWindow(hwnd, Win32Interop.SW_HIDE);
                return;
            }

            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            int screenWidthPx = Win32Interop.GetSystemMetrics(Win32Interop.SM_CXSCREEN);
            int screenHeightPx = Win32Interop.GetSystemMetrics(Win32Interop.SM_CYSCREEN);
            int barHeightPx = Math.Max(1, (int)Math.Round(heightDip * scaleY));

            var abd = new Win32Interop.APPBARDATA
            {
                cbSize = Marshal.SizeOf<Win32Interop.APPBARDATA>(),
                hWnd = hwnd,
                uEdge = uEdge,
                rc = new Win32Interop.RECT
                {
                    left = 0,
                    right = screenWidthPx,
                    top = edge == BarEdge.Bottom ? screenHeightPx - barHeightPx : 0,
                    bottom = edge == BarEdge.Bottom ? screenHeightPx : barHeightPx
                }
            };

            Win32Interop.SHAppBarMessage(Win32Interop.ABM_QUERYPOS, ref abd);

            // CRITICAL: In AppBarService.UpdateBarPosition, when edge is BarEdge.Bottom,
            // ensure the dock is placed at the exact bottom of the screen (bottom = screenHeightPx, top = screenHeightPx - barHeightPx)
            // in place of the hidden Windows taskbar, NOT stacked above it!
            if (edge == BarEdge.Bottom)
            {
                abd.rc.left = 0;
                abd.rc.right = screenWidthPx;
                abd.rc.bottom = screenHeightPx;
                abd.rc.top = screenHeightPx - barHeightPx;
            }
            else
            {
                abd.rc.left = 0;
                abd.rc.right = screenWidthPx;
                abd.rc.top = 0;
                abd.rc.bottom = barHeightPx;
            }

            Win32Interop.SHAppBarMessage(Win32Interop.ABM_SETPOS, ref abd);

            // Explicit target coordinates to guarantee the dock sits flush at the bottom
            int targetLeftPx = 0;
            int targetTopPx = edge == BarEdge.Bottom ? screenHeightPx - barHeightPx : 0;
            int targetWidthPx = screenWidthPx;
            int targetHeightPx = barHeightPx;

            window.Left = targetLeftPx / scaleX;
            window.Top = targetTopPx / scaleY;
            window.Width = targetWidthPx / scaleX;
            window.Height = targetHeightPx / scaleY;

            Win32Interop.MoveWindow(hwnd, targetLeftPx, targetTopPx, targetWidthPx, targetHeightPx, true);

            Win32Interop.SetWindowPos(
                hwnd,
                Win32Interop.HWND_TOPMOST,
                targetLeftPx,
                targetTopPx,
                targetWidthPx,
                targetHeightPx,
                Win32Interop.SWP_NOACTIVATE | Win32Interop.SWP_SHOWWINDOW);
        }
        finally
        {
            if (registration != null)
            {
                registration.IsUpdating = false;
            }
        }
    }

    public void UnregisterBar(Window window)
    {
        if (window == null)
        {
            return;
        }

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_registeredBars.Remove(hwnd, out var registration))
        {
            if (registration.VisibilityChangedHandler != null)
            {
                registration.Window.IsVisibleChanged -= registration.VisibilityChangedHandler;
            }

            if (registration.Hook != null)
            {
                var source = HwndSource.FromHwnd(hwnd);
                source?.RemoveHook(registration.Hook);
            }

            var abd = new Win32Interop.APPBARDATA
            {
                cbSize = Marshal.SizeOf<Win32Interop.APPBARDATA>(),
                hWnd = hwnd
            };

            Win32Interop.SHAppBarMessage(Win32Interop.ABM_REMOVE, ref abd);
        }
    }

    public void PinToDesktop(Window widgetWindow)
    {
        ArgumentNullException.ThrowIfNull(widgetWindow);

        var helper = new WindowInteropHelper(widgetWindow);
        IntPtr hwnd = helper.Handle != IntPtr.Zero ? helper.Handle : helper.EnsureHandle();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int exStyle = Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE);
        exStyle |= Win32Interop.WS_EX_TOOLWINDOW | Win32Interop.WS_EX_NOACTIVATE;
        exStyle &= ~Win32Interop.WS_EX_APPWINDOW;
        Win32Interop.SetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE, exStyle);

        if (widgetWindow.Visibility != Visibility.Visible)
        {
            widgetWindow.Visibility = Visibility.Visible;
        }
        widgetWindow.Opacity = 1.0;

        Win32Interop.SetWindowPos(
            hwnd,
            Win32Interop.HWND_NOTOPMOST,
            0,
            0,
            0,
            0,
            Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE | Win32Interop.SWP_SHOWWINDOW);
    }

    private void HandleFullscreenApp(Window window, bool isFullscreen)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (_registeredBars.TryGetValue(hwnd, out var registration))
            {
                registration.IsFullscreenActive = isFullscreen;
            }

            if (isFullscreen)
            {
                // Fullscreen app (game, browser video) opened: lower dock behind it
                Win32Interop.SetWindowPos(
                    hwnd,
                    Win32Interop.HWND_BOTTOM,
                    0, 0, 0, 0,
                    Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE);
            }
            else
            {
                // Fullscreen app closed: restore topmost dock position
                Win32Interop.SetWindowPos(
                    hwnd,
                    Win32Interop.HWND_TOPMOST,
                    0, 0, 0, 0,
                    Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE);
            }
        }
        catch
        {
            // Suppress errors during state change
        }
    }
}
