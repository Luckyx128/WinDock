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
        public double HeightDip { get; set; }
        public BarEdge Edge { get; set; }
        public bool IsUpdating { get; set; }
    }

    private readonly Dictionary<IntPtr, AppBarRegistration> _registeredBars = new();

    public void RegisterBar(Window window, double heightDip, BarEdge edge = BarEdge.Top)
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

            var source = HwndSource.FromHwnd(hwnd);
            if (source != null)
            {
                HwndSourceHook hook = (IntPtr _, int msg, IntPtr wParam, IntPtr _, ref bool _) =>
                {
                    if ((uint)msg == registration.CallbackMessageId &&
                        wParam.ToInt32() == Win32Interop.ABN_POSCHANGED &&
                        !registration.IsUpdating)
                    {
                        UpdateBarPosition(registration.Window, registration.HeightDip, registration.Edge);
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

        UpdateBarPosition(window, heightDip, edge);
    }

    public void UpdateBarPosition(Window window, double heightDip, BarEdge edge = BarEdge.Top)
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
            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            int screenWidthPx = Win32Interop.GetSystemMetrics(Win32Interop.SM_CXSCREEN);
            int screenHeightPx = Win32Interop.GetSystemMetrics(Win32Interop.SM_CYSCREEN);
            int barHeightPx = Math.Max(1, (int)Math.Round(heightDip * scaleY));

            uint uEdge = edge == BarEdge.Bottom ? Win32Interop.ABE_BOTTOM : Win32Interop.ABE_TOP;

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

            if (edge == BarEdge.Bottom)
            {
                abd.rc.top = abd.rc.bottom - barHeightPx;
            }
            else
            {
                abd.rc.bottom = abd.rc.top + barHeightPx;
            }

            Win32Interop.SHAppBarMessage(Win32Interop.ABM_SETPOS, ref abd);

            int widthPx = Math.Max(1, abd.rc.right - abd.rc.left);
            int heightPx = Math.Max(1, abd.rc.bottom - abd.rc.top);

            window.Left = abd.rc.left / scaleX;
            window.Top = abd.rc.top / scaleY;
            window.Width = widthPx / scaleX;
            window.Height = heightPx / scaleY;

            Win32Interop.MoveWindow(hwnd, abd.rc.left, abd.rc.top, widthPx, heightPx, true);
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

        Win32Interop.SetWindowPos(
            hwnd,
            Win32Interop.HWND_BOTTOM,
            0,
            0,
            0,
            0,
            Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE);
    }
}
