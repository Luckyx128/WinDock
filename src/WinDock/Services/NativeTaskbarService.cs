using System;
using System.Runtime.InteropServices;
using WinDock.Interop;
using WinDock.Models;

namespace WinDock.Services;

public sealed class NativeTaskbarService : INativeTaskbarService
{
    private const uint SPI_SETWORKAREA = 0x002F;
    private const uint SPI_GETWORKAREA = 0x0030;
    private const uint SPIF_UPDATEINIFILE = 0x0001;
    private const uint SPIF_SENDCHANGE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        ref Win32Interop.RECT pvParam,
        uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Win32Interop.RECT lpRect);

    private static Win32Interop.RECT? _savedWorkArea;

    public bool IsNativeTaskbarHidden { get; private set; }

    public void HideNativeTaskbar()
    {
        try
        {
            SetTaskbarVisibility(Win32Interop.SW_HIDE);
            IsNativeTaskbarHidden = true;
        }
        catch
        {
            // Safeguard against unexpected Win32 handle state
        }
    }

    public void ShowNativeTaskbar()
    {
        try
        {
            SetTaskbarVisibility(Win32Interop.SW_SHOW);
            IsNativeTaskbarHidden = false;
        }
        catch
        {
            // Safeguard against unexpected Win32 handle state
        }
    }

    public void ToggleNativeTaskbar()
    {
        if (IsNativeTaskbarHidden)
        {
            ShowNativeTaskbar();
        }
        else
        {
            HideNativeTaskbar();
        }
    }

    private static void SetTaskbarVisibility(int showCommand)
    {
        IntPtr primaryHwnd = Win32Interop.FindWindow(Win32Interop.ShellTrayWndClassName, null);
        if (primaryHwnd != IntPtr.Zero)
        {
            Win32Interop.ShowWindow(primaryHwnd, showCommand);
        }

        IntPtr secondaryHwnd = Win32Interop.FindWindow(Win32Interop.ShellSecondaryTrayWndClassName, null);
        if (secondaryHwnd != IntPtr.Zero)
        {
            Win32Interop.ShowWindow(secondaryHwnd, showCommand);
        }

        // Also handle any additional secondary monitors if present
        IntPtr nextSecondary = IntPtr.Zero;
        while ((nextSecondary = Win32Interop.FindWindowEx(
                   IntPtr.Zero,
                   nextSecondary,
                   Win32Interop.ShellSecondaryTrayWndClassName,
                   null)) != IntPtr.Zero)
        {
            if (nextSecondary != secondaryHwnd)
            {
                Win32Interop.ShowWindow(nextSecondary, showCommand);
            }
        }

        // When hiding/showing Shell_TrayWnd, call SystemParametersInfo(SPI_SETWORKAREA)
        // so Windows releases the bottom taskbar space immediately.
        UpdateDesktopWorkArea(showCommand, primaryHwnd);
    }

    private static void UpdateDesktopWorkArea(int showCommand, IntPtr primaryHwnd)
    {
        try
        {
            int screenWidth = Win32Interop.GetSystemMetrics(Win32Interop.SM_CXSCREEN);
            int screenHeight = Win32Interop.GetSystemMetrics(Win32Interop.SM_CYSCREEN);

            if (showCommand == Win32Interop.SW_HIDE)
            {
                // Capture work area before hiding if not yet saved
                if (!_savedWorkArea.HasValue)
                {
                    var currentArea = new Win32Interop.RECT();
                    if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref currentArea, 0) && currentArea.Bottom > 0)
                    {
                        _savedWorkArea = currentArea;
                    }
                }

                // Reserve 48px for WinDock so normal maximized windows stay within (0, 0, screenWidth, screenHeight - 48)
                var fullArea = new Win32Interop.RECT
                {
                    left = 0,
                    top = 0,
                    right = screenWidth,
                    bottom = screenHeight - 48
                };

                SystemParametersInfo(SPI_SETWORKAREA, 0, ref fullArea, SPIF_SENDCHANGE);
            }
            else
            {
                // Restoring taskbar: restore previous work area or calculate using taskbar bounds
                if (_savedWorkArea.HasValue)
                {
                    var restoredArea = _savedWorkArea.Value;
                    SystemParametersInfo(SPI_SETWORKAREA, 0, ref restoredArea, SPIF_SENDCHANGE);
                    _savedWorkArea = null;
                }
                else
                {
                    int taskbarTop = screenHeight - 48;
                    if (primaryHwnd != IntPtr.Zero && GetWindowRect(primaryHwnd, out var tbRect) && tbRect.top > 0)
                    {
                        taskbarTop = tbRect.top;
                    }

                    var defaultArea = new Win32Interop.RECT
                    {
                        left = 0,
                        top = 0,
                        right = screenWidth,
                        bottom = taskbarTop
                    };

                    SystemParametersInfo(SPI_SETWORKAREA, 0, ref defaultArea, SPIF_SENDCHANGE);
                }
            }
        }
        catch
        {
            // Fail-safe to avoid crashing on system metrics or permission restrictions
        }
    }
}
