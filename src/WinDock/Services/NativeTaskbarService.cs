using System;
using WinDock.Interop;
using WinDock.Models;

namespace WinDock.Services;

public sealed class NativeTaskbarService : INativeTaskbarService
{
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
    }
}
